import os
import string
import sys
from pathlib import Path, PurePosixPath

from PyQt5.QtCore import QObject, QSize, QThread, QTimer, Qt, pyqtSignal
from PyQt5.QtWidgets import (
    QApplication,
    QAbstractItemView,
    QComboBox,
    QDialog,
    QDialogButtonBox,
    QFileDialog,
    QFileIconProvider,
    QFormLayout,
    QGroupBox,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QLineEdit,
    QListWidget,
    QListWidgetItem,
    QMenu,
    QMessageBox,
    QPushButton,
    QSpinBox,
    QStyle,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
    QWidget,
)

from local_api import (
    connect_profile,
    copy_path,
    create_profile,
    create_folder,
    delete_profile,
    delete_path,
    disconnect_current,
    download_file,
    exists_path,
    get_active_profile_name,
    get_file_info,
    get_home,
    list_files,
    list_profiles,
    list_roots,
    move_path,
    rename_path,
    search_files,
    upload_file,
)


def human_size(size: int) -> str:
    units = ["B", "KB", "MB", "GB", "TB"]
    amount = float(size)
    for unit in units:
        if amount < 1024 or unit == units[-1]:
            if unit == "B":
                return f"{int(amount)} B"
            return f"{amount:.1f} {unit}"
        amount /= 1024
    return f"{size} B"


class TextPromptDialog(QDialog):
    def __init__(self, parent, title, label, initial_text=""):
        super().__init__(parent)
        self.setWindowTitle(title)
        self.resize(360, 110)

        layout = QVBoxLayout(self)
        layout.addWidget(QLabel(label))

        self.text_input = QLineEdit()
        self.text_input.setText(initial_text)
        self.text_input.selectAll()
        layout.addWidget(self.text_input)

        buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

    def value(self):
        return self.text_input.text()


class ProfileDialog(QDialog):
    def __init__(self, parent):
        super().__init__(parent)
        self.setWindowTitle("Add Profile")
        self.resize(420, 260)

        layout = QVBoxLayout(self)
        form = QFormLayout()
        form.setLabelAlignment(Qt.AlignRight)

        self.name_input = QLineEdit()
        self.host_input = QLineEdit()

        self.protocol_combo = QComboBox()
        self.protocol_combo.addItems(["Sftp", "Ftp"])
        self.protocol_combo.currentTextChanged.connect(self.sync_default_port)

        self.port_input = QSpinBox()
        self.port_input.setRange(1, 65535)
        self.port_input.setValue(22)

        self.auth_combo = QComboBox()
        self.auth_combo.addItems(["password", "key", "anonymous"])
        self.auth_combo.currentTextChanged.connect(self.update_auth_fields)

        self.username_input = QLineEdit()
        self.password_input = QLineEdit()
        self.password_input.setEchoMode(QLineEdit.Password)
        self.key_path_input = QLineEdit()
        self.passphrase_input = QLineEdit()
        self.passphrase_input.setEchoMode(QLineEdit.Password)

        form.addRow("Name:", self.name_input)
        form.addRow("Protocol:", self.protocol_combo)
        form.addRow("Host:", self.host_input)
        form.addRow("Port:", self.port_input)
        form.addRow("Auth:", self.auth_combo)
        form.addRow("Username:", self.username_input)
        form.addRow("Password:", self.password_input)
        form.addRow("Key path:", self.key_path_input)
        form.addRow("Passphrase:", self.passphrase_input)
        layout.addLayout(form)

        buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

        self.update_auth_fields()

    def sync_default_port(self, *_):
        self.port_input.setValue(22 if self.protocol_combo.currentText() == "Sftp" else 21)

    def update_auth_fields(self, *_):
        auth_type = self.auth_combo.currentText()
        password_mode = auth_type == "password"
        key_mode = auth_type == "key"
        self.username_input.setEnabled(password_mode or key_mode)
        self.password_input.setEnabled(password_mode)
        self.key_path_input.setEnabled(key_mode)
        self.passphrase_input.setEnabled(key_mode)

    def profile_data(self):
        auth_type = self.auth_combo.currentText()
        if auth_type == "anonymous":
            auth = {"$type": "anonymous"}
        elif auth_type == "key":
            auth = {
                "$type": "key",
                "Username": self.username_input.text().strip(),
                "KeyPath": self.key_path_input.text().strip(),
                "Passphrase": self.passphrase_input.text() or None,
            }
        else:
            auth = {
                "$type": "password",
                "Username": self.username_input.text().strip(),
                "Password": self.password_input.text(),
            }

        return {
            "name": self.name_input.text().strip(),
            "host": self.host_input.text().strip(),
            "protocol": self.protocol_combo.currentText(),
            "port": self.port_input.value(),
            "auth": auth,
        }


class PlacesList(QListWidget):
    def __init__(self, parent_window):
        super().__init__()
        self.parent_window = parent_window
        self.setMinimumWidth(128)
        self.setMaximumWidth(140)
        self.itemClicked.connect(self.parent_window.handle_place_clicked)


class SearchWorker(QObject):
    finished = pyqtSignal(list)
    failed = pyqtSignal(str)

    def __init__(self, path, query):
        super().__init__()
        self.path = path
        self.query = query

    def run(self):
        try:
            self.finished.emit(search_files(self.path, self.query))
        except Exception as exc:
            self.failed.emit(str(exc))


class FileTable(QTableWidget):
    def __init__(self, parent_window):
        super().__init__(0, 4)
        self.parent_window = parent_window
        self.setHorizontalHeaderLabels(["Name", "Size", "Type", "Date Modified"])
        self.setSelectionBehavior(QAbstractItemView.SelectRows)
        self.setSelectionMode(QAbstractItemView.ExtendedSelection)
        self.setEditTriggers(QAbstractItemView.NoEditTriggers)
        self.setAlternatingRowColors(False)
        self.verticalHeader().setVisible(False)
        self.verticalHeader().setDefaultSectionSize(21)
        self.setShowGrid(False)
        self.setIconSize(QSize(16, 16))
        self.setAcceptDrops(True)
        self.setDragDropMode(QAbstractItemView.DropOnly)
        self.setContextMenuPolicy(Qt.CustomContextMenu)
        self.customContextMenuRequested.connect(self.parent_window.show_file_context_menu)
        self.itemDoubleClicked.connect(self.parent_window.handle_item_double_click)
        self.itemSelectionChanged.connect(self.parent_window.sync_selection_fields)

        header = self.horizontalHeader()
        header.setDefaultAlignment(Qt.AlignLeft | Qt.AlignVCenter)
        header.setMinimumSectionSize(44)
        header.setSectionResizeMode(0, QHeaderView.Stretch)
        header.setSectionResizeMode(1, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(2, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(3, QHeaderView.ResizeToContents)

    def set_details_mode(self):
        self.setIconSize(QSize(16, 16))
        self.verticalHeader().setDefaultSectionSize(21)
        self.horizontalHeader().show()
        for column in range(self.columnCount()):
            self.setColumnHidden(column, False)

    def set_list_mode(self):
        self.setIconSize(QSize(16, 16))
        self.verticalHeader().setDefaultSectionSize(21)
        self.horizontalHeader().hide()
        self.setColumnHidden(0, False)
        for column in range(1, self.columnCount()):
            self.setColumnHidden(column, True)

    def set_tiles_mode(self):
        self.setIconSize(QSize(28, 28))
        self.verticalHeader().setDefaultSectionSize(36)
        self.horizontalHeader().hide()
        self.setColumnHidden(0, False)
        for column in range(1, self.columnCount()):
            self.setColumnHidden(column, True)

    def dragEnterEvent(self, event):
        if event.mimeData().hasUrls():
            event.acceptProposedAction()
            return
        event.ignore()

    def dragMoveEvent(self, event):
        if event.mimeData().hasUrls():
            event.acceptProposedAction()
            return
        event.ignore()

    def dropEvent(self, event):
        paths = [url.toLocalFile() for url in event.mimeData().urls() if url.isLocalFile()]
        if paths:
            self.parent_window.upload_dropped_paths(paths)
            event.acceptProposedAction()
            return
        event.ignore()

    def keyPressEvent(self, event):
        key = event.key()
        modifiers = event.modifiers()

        if key == Qt.Key_Delete:
            self.parent_window.delete_selected()
            return
        if key == Qt.Key_F2:
            self.parent_window.rename_selected()
            return
        if key in (Qt.Key_Return, Qt.Key_Enter):
            self.parent_window.open_selected_item()
            return
        if key == Qt.Key_Backspace:
            self.parent_window.go_up()
            return
        if key == Qt.Key_F5 or (key == Qt.Key_R and modifiers & Qt.ControlModifier):
            self.parent_window.refresh_current_view()
            return
        if key == Qt.Key_N and modifiers & Qt.ControlModifier:
            self.parent_window.create_folder()
            return
        if key == Qt.Key_F and modifiers & Qt.ControlModifier:
            self.parent_window.search_items()
            return
        if key == Qt.Key_D and modifiers & Qt.ControlModifier:
            self.parent_window.export_selected()
            return

        super().keyPressEvent(event)


class FileManagerWindow(QWidget):
    def __init__(self):
        super().__init__()
        self.current_path = "/"
        self.current_items = []
        self.profiles = []
        self.connected = False
        self.search_thread = None
        self.search_worker = None
        self.current_view_mode = "details"
        self.icon_provider = QFileIconProvider()

        self.setWindowTitle("SFTP/FTP File Manager")
        self.resize(1020, 700)

        self.build_ui()
        self.apply_styles()
        self.safe_initial_load()

        self.refresh_timer = QTimer(self)
        self.refresh_timer.setInterval(5000)
        self.refresh_timer.timeout.connect(self.refresh_current_view)
        self.refresh_timer.start()

    def build_ui(self):
        root_layout = QVBoxLayout(self)
        root_layout.setContentsMargins(8, 8, 8, 8)
        root_layout.setSpacing(0)

        main_group = QGroupBox("SFTP/FTP File Manager")
        group_layout = QVBoxLayout(main_group)
        group_layout.setContentsMargins(8, 7, 8, 8)
        group_layout.setSpacing(7)

        profile_row = QHBoxLayout()
        profile_row.setSpacing(6)

        profile_label = QLabel("Profile:")
        profile_label.setObjectName("toolbarLabel")
        profile_row.addWidget(profile_label)
        self.profile_combo = QComboBox()
        self.profile_combo.setMinimumWidth(340)
        self.profile_combo.setFixedHeight(30)
        self.profile_combo.currentIndexChanged.connect(self.update_connection_controls)
        profile_row.addWidget(self.profile_combo, 1)

        self.connect_button = QPushButton("Connect")
        self.connect_button.setFixedHeight(30)
        self.connect_button.setMinimumWidth(76)
        self.connect_button.clicked.connect(self.connect_selected_profile)
        profile_row.addWidget(self.connect_button)

        self.disconnect_button = QPushButton("Disconnect")
        self.disconnect_button.setFixedHeight(30)
        self.disconnect_button.setMinimumWidth(84)
        self.disconnect_button.clicked.connect(self.disconnect_profile)
        profile_row.addWidget(self.disconnect_button)

        self.add_profile_button = QPushButton("+")
        self.add_profile_button.setToolTip("Add profile")
        self.add_profile_button.clicked.connect(self.add_profile)
        profile_row.addWidget(self.add_profile_button)

        self.delete_profile_button = QPushButton("-")
        self.delete_profile_button.setToolTip("Delete profile")
        self.delete_profile_button.clicked.connect(self.delete_selected_profile)
        profile_row.addWidget(self.delete_profile_button)

        for button in (self.add_profile_button, self.delete_profile_button):
            button.setFixedSize(30, 30)

        group_layout.addLayout(profile_row)

        toolbar_row = QHBoxLayout()
        toolbar_row.setSpacing(6)
        look_in_label = QLabel("Look in:")
        look_in_label.setObjectName("toolbarLabel")
        toolbar_row.addWidget(look_in_label)
        self.path_combo = QComboBox()
        self.path_combo.setEditable(True)
        self.path_combo.setFixedHeight(34)
        self.path_combo.setMinimumWidth(520)
        self.path_combo.lineEdit().returnPressed.connect(self.change_path)
        self.path_combo.lineEdit().setObjectName("pathLineEdit")
        toolbar_row.addWidget(self.path_combo, 1)

        self.back_button = QPushButton()
        self.back_button.setIcon(self.style().standardIcon(QStyle.SP_ArrowBack))
        self.back_button.setToolTip("Back")
        self.back_button.clicked.connect(self.go_up)

        self.up_button = QPushButton()
        self.up_button.setIcon(self.style().standardIcon(QStyle.SP_ArrowUp))
        self.up_button.setToolTip("Up")
        self.up_button.clicked.connect(self.go_up)

        self.refresh_button = QPushButton()
        self.refresh_button.setIcon(self.style().standardIcon(QStyle.SP_BrowserReload))
        self.refresh_button.setToolTip("Refresh")
        self.refresh_button.clicked.connect(self.refresh_current_view)

        self.details_view_button = QPushButton()
        self.details_view_button.setIcon(self.style().standardIcon(QStyle.SP_FileDialogDetailedView))
        self.details_view_button.setToolTip("Details")
        self.details_view_button.setCheckable(True)
        self.details_view_button.clicked.connect(lambda: self.set_view_mode("details"))

        self.list_view_button = QPushButton()
        self.list_view_button.setIcon(self.style().standardIcon(QStyle.SP_FileDialogListView))
        self.list_view_button.setToolTip("List")
        self.list_view_button.setCheckable(True)
        self.list_view_button.clicked.connect(lambda: self.set_view_mode("list"))

        self.tiles_view_button = QPushButton()
        self.tiles_view_button.setIcon(self.style().standardIcon(QStyle.SP_FileIcon))
        self.tiles_view_button.setToolTip("Large icons")
        self.tiles_view_button.setCheckable(True)
        self.tiles_view_button.clicked.connect(lambda: self.set_view_mode("tiles"))

        self.new_folder_button = QPushButton()
        self.new_folder_button.setIcon(self.style().standardIcon(QStyle.SP_FileDialogNewFolder))
        self.new_folder_button.setToolTip("New Folder")
        self.new_folder_button.clicked.connect(self.create_folder)

        toolbar_buttons = (
            self.back_button,
            self.up_button,
            self.refresh_button,
            self.new_folder_button,
            self.details_view_button,
            self.list_view_button,
            self.tiles_view_button,
        )
        for button in toolbar_buttons:
            button.setFixedSize(24, 24)
            button.setIconSize(QSize(15, 15))

        toolbar_row.addWidget(self.back_button)
        toolbar_row.addWidget(self.up_button)
        toolbar_row.addWidget(self.refresh_button)
        toolbar_row.addWidget(self.new_folder_button)
        toolbar_row.addSpacing(4)
        toolbar_row.addWidget(self.details_view_button)
        toolbar_row.addWidget(self.list_view_button)
        toolbar_row.addWidget(self.tiles_view_button)
        group_layout.addLayout(toolbar_row)

        main_row = QHBoxLayout()
        main_row.setSpacing(6)

        self.places_list = PlacesList(self)
        main_row.addWidget(self.places_list)

        self.file_table = FileTable(self)
        main_row.addWidget(self.file_table, 1)

        group_layout.addLayout(main_row, 1)

        self.file_name_input = QLineEdit()
        self.file_name_input.setReadOnly(True)
        self.file_name_input.setFixedHeight(22)
        file_name_row = QHBoxLayout()
        file_name_row.setContentsMargins(0, 0, 0, 0)
        file_name_row.setSpacing(8)
        file_name_row.addWidget(QLabel("File name:"))
        file_name_row.addWidget(self.file_name_input, 1)
        group_layout.addLayout(file_name_row)

        self.file_type_combo = QComboBox()
        self.file_type_combo.addItem("All Files (*)")
        self.file_type_combo.setFixedHeight(22)
        file_type_row = QHBoxLayout()
        file_type_row.setContentsMargins(0, 0, 0, 0)
        file_type_row.setSpacing(8)
        file_type_row.addWidget(QLabel("Files of type:"))
        file_type_row.addWidget(self.file_type_combo, 1)
        group_layout.addLayout(file_type_row)

        self.status_label = QLabel("Current folder.")
        self.status_label.setFixedHeight(17)
        group_layout.addWidget(self.status_label)

        root_layout.addWidget(main_group)
        self.set_view_mode("details")

    def apply_styles(self):
        self.setStyleSheet(
            """
            QWidget {
                background: #0b1118;
                color: #eef3f8;
                font-size: 12px;
            }
            QGroupBox {
                border: 1px solid #24384a;
                border-radius: 9px;
                margin-top: 10px;
                padding-top: 6px;
                font-weight: 600;
            }
            QGroupBox::title {
                subcontrol-origin: margin;
                subcontrol-position: top left;
                left: 12px;
                padding: 0 6px;
                color: #ffffff;
                background: #0b1118;
            }
            QLabel {
                color: #edf2f7;
            }
            QLabel#toolbarLabel {
                font-size: 13px;
                font-weight: 600;
                padding-right: 4px;
            }
            QLineEdit, QComboBox, QListWidget, QTableWidget {
                background: #0f1822;
                color: #eef3f8;
                border: 1px solid #294055;
                border-radius: 6px;
                selection-background-color: #2b6ea5;
                selection-color: #ffffff;
            }
            QLineEdit, QComboBox {
                padding: 3px 8px;
            }
            QComboBox#lineEdit, QLineEdit#pathLineEdit {
                font-size: 14px;
            }
            QComboBox {
                min-height: 30px;
            }
            QPushButton {
                min-height: 30px;
            }
            QComboBox::drop-down {
                width: 20px;
                border: none;
            }
            QComboBox QAbstractItemView {
                background: #0f1822;
                border: 1px solid #294055;
                selection-background-color: #2b6ea5;
            }
            QPushButton {
                background: #13202c;
                color: #eef3f8;
                border: 1px solid #2a3f52;
                border-radius: 6px;
                padding: 3px 10px;
            }
            QPushButton:hover {
                background: #1a2b3b;
                border-color: #54718a;
            }
            QPushButton:pressed {
                background: #101922;
            }
            QPushButton:checked {
                background: #24435d;
                border-color: #6b8aa8;
            }
            QPushButton:disabled {
                color: #7f92a5;
                background: #101821;
                border-color: #1b2a38;
            }
            QListWidget {
                outline: none;
            }
            QListWidget::item {
                min-height: 23px;
                padding: 3px 8px;
                border-radius: 4px;
            }
            QListWidget::item:selected {
                background: #2b6ea5;
            }
            QTableWidget {
                outline: none;
                gridline-color: #1f3344;
                background: #0d1620;
            }
            QTableWidget::item {
                padding: 2px 6px;
                border: none;
            }
            QTableWidget::item:selected {
                background: #2b6ea5;
                color: #ffffff;
            }
            QHeaderView::section {
                background: #172637;
                color: #ffffff;
                border: none;
                border-right: 1px solid #2a3f52;
                border-bottom: 1px solid #2a3f52;
                padding: 4px 7px;
                min-height: 24px;
            }
            QMenu {
                background: #13202c;
                color: #eef3f8;
                border: 1px solid #294055;
                border-radius: 6px;
            }
            QMenu::item {
                padding: 6px 26px 6px 20px;
                border-radius: 4px;
            }
            QMenu::item:selected {
                background: #2b6ea5;
            }
            """
        )

    def set_view_mode(self, mode):
        self.current_view_mode = mode
        self.details_view_button.setChecked(mode == "details")
        self.list_view_button.setChecked(mode == "list")
        self.tiles_view_button.setChecked(mode == "tiles")
        if mode == "details":
            self.file_table.set_details_mode()
        elif mode == "list":
            self.file_table.set_list_mode()
        else:
            self.file_table.set_tiles_mode()

    def populate_places(self):
        self.places_list.clear()

        for root in list_roots():
            icon_type = QFileIconProvider.Drive if root.get("kind") == "drive" else QFileIconProvider.Folder
            root_item = QListWidgetItem(self.icon_provider.icon(icon_type), root["name"])
            root_item.setData(Qt.UserRole, root["path"])
            root_item.setToolTip(root["path"])
            self.places_list.addItem(root_item)

    def set_status(self, message):
        self.status_label.setText(message)

    def require_connection(self, action="This action"):
        if self.connected:
            return True
        self.set_status(f"{action} requires connection.")
        self.show_info("Not connected", "Select a profile and press Connect first.")
        return False

    def safe_initial_load(self):
        self.refresh_profile_list()
        self.path_combo.clear()
        self.path_combo.addItem(self.current_path)
        self.path_combo.setEditText(self.current_path)
        self.update_connection_controls()
        self.set_status("Select a profile.")

    def refresh_profile_list(self, selected_id=None):
        try:
            self.profiles = list_profiles()
        except Exception as exc:
            self.profiles = []
            self.profile_combo.clear()
            self.set_status(f"Unable to load profiles: {exc}")
            return

        previous_id = selected_id
        if previous_id is None and self.profile_combo.count():
            current_data = self.profile_combo.currentData()
            previous_id = current_data.get("id") if current_data else None

        self.profile_combo.blockSignals(True)
        self.profile_combo.clear()
        for profile in self.profiles:
            label = self.format_profile_label(profile)
            self.profile_combo.addItem(label, profile)
            if previous_id and profile["id"] == previous_id:
                self.profile_combo.setCurrentIndex(self.profile_combo.count() - 1)
        self.profile_combo.blockSignals(False)
        self.update_connection_controls()

    def format_profile_label(self, profile):
        host = profile.get("host") or "-"
        protocol = profile.get("protocol") or "?"
        return f"{profile.get('name') or 'Profile'}  [{protocol} {host}]"

    def selected_profile(self):
        return self.profile_combo.currentData()

    def update_connection_controls(self, *_):
        has_profile = self.profile_combo.count() > 0
        self.connect_button.setEnabled(has_profile and not self.connected)
        self.disconnect_button.setEnabled(self.connected)
        profile = self.selected_profile()
        can_delete = bool(profile and not self.connected)
        self.delete_profile_button.setEnabled(can_delete)

    def connect_selected_profile(self):
        profile = self.selected_profile()
        if not profile:
            self.show_info("Connect", "No profile selected.")
            return

        try:
            self.set_status(f"Connecting: {profile.get('name') or profile.get('host')}")
            connect_profile(profile["id"])
            self.current_path = get_home()
            self.connected = True
            self.populate_places()
            self.refresh_current_view()
        except Exception as exc:
            self.connected = False
            self.set_status(f"Connect failed: {exc}")
            self.show_error("Unable to connect", exc)
        finally:
            self.update_connection_controls()

    def disconnect_profile(self):
        try:
            disconnect_current()
        except Exception as exc:
            self.set_status(f"Disconnect failed: {exc}")
            return
        self.connected = False
        self.current_items = []
        self.current_path = "/"
        self.file_table.setRowCount(0)
        self.places_list.clear()
        self.path_combo.clear()
        self.path_combo.addItem(self.current_path)
        self.path_combo.setEditText(self.current_path)
        self.sync_selection_fields()
        self.update_connection_controls()
        self.set_status("Disconnected.")

    def add_profile(self):
        dialog = ProfileDialog(self)
        if dialog.exec_() != QDialog.Accepted:
            self.set_status("Add profile cancelled.")
            return

        data = dialog.profile_data()
        try:
            profile = create_profile(
                data["name"],
                data["host"],
                data["protocol"],
                data["port"],
                data["auth"],
            )
            self.refresh_profile_list(profile["id"])
            self.set_status(f"Profile added: {profile.get('name')}")
        except Exception as exc:
            self.show_error("Unable to add profile", exc)

    def delete_selected_profile(self):
        profile = self.selected_profile()
        if not profile:
            self.show_info("Delete profile", "No profile selected.")
            return

        answer = QMessageBox.question(
            self,
            "Delete profile",
            f"Delete profile '{profile.get('name')}'?",
            QMessageBox.Yes | QMessageBox.No,
            QMessageBox.No,
        )
        if answer != QMessageBox.Yes:
            self.set_status("Delete profile cancelled.")
            return

        try:
            delete_profile(profile["id"])
            self.refresh_profile_list()
            self.set_status(f"Profile deleted: {profile.get('name')}")
        except Exception as exc:
            self.show_error("Unable to delete profile", exc)

    def refresh_current_view(self):
        if not self.connected:
            self.set_status("Select a profile.")
            return
        try:
            response = list_files(self.current_path)
        except Exception as exc:
            self.set_status(f"Unable to load files: {exc}")
            return

        self.current_path = response["path"]
        self.current_items = response["items"]

        self.path_combo.blockSignals(True)
        self.path_combo.clear()
        self.path_combo.addItem(self.current_path)
        self.path_combo.setEditText(self.current_path)
        self.path_combo.blockSignals(False)

        selected_paths = {item["path"] for item in self.selected_items()}

        self.file_table.setRowCount(0)
        for entry in self.current_items:
            row = self.file_table.rowCount()
            self.file_table.insertRow(row)

            icon = self.icon_provider.icon(QFileIconProvider.Folder if entry["is_dir"] else QFileIconProvider.File)
            name_item = QTableWidgetItem(icon, entry["name"])
            name_item.setData(Qt.UserRole, entry)
            self.file_table.setItem(row, 0, name_item)
            self.file_table.setItem(row, 1, QTableWidgetItem("" if entry["is_dir"] else human_size(entry["size"])))
            self.file_table.setItem(row, 2, QTableWidgetItem("File Folder" if entry["is_dir"] else "File"))
            self.file_table.setItem(row, 3, QTableWidgetItem(entry["modified"]))

            if entry["path"] in selected_paths:
                self.file_table.selectRow(row)

        self.sync_selection_fields()
        self.highlight_current_place()
        profile_name = get_active_profile_name()
        if profile_name:
            self.set_status(f"{profile_name} | Current folder: {self.current_path}")
        else:
            self.set_status(f"Current folder: {self.current_path}")

    def highlight_current_place(self):
        for index in range(self.places_list.count()):
            item = self.places_list.item(index)
            target = item.data(Qt.UserRole)
            if target and target.rstrip("/") == self.current_path.rstrip("/"):
                self.places_list.setCurrentItem(item)
                return

    def handle_place_clicked(self, item):
        target = item.data(Qt.UserRole)
        if not target:
            return
        self.current_path = target
        self.refresh_current_view()

    def change_path(self):
        if not self.connected:
            self.set_status("Connect before changing folder.")
            return
        new_path = self.path_combo.currentText().strip()
        if not new_path:
            return
        if not exists_path(new_path):
            self.show_error("Path not found", FileNotFoundError(new_path))
            return
        self.current_path = new_path
        self.refresh_current_view()

    def go_up(self):
        if not self.connected:
            self.set_status("Connect before changing folder.")
            return
        current = PurePosixPath(self.current_path)
        parent = current.parent
        if str(parent) == str(current):
            self.set_status("Already at the top level.")
            return
        self.current_path = str(parent)
        self.refresh_current_view()

    def selected_items(self):
        rows = sorted({index.row() for index in self.file_table.selectionModel().selectedRows()})
        items = []
        for row in rows:
            item = self.file_table.item(row, 0)
            if item:
                data = item.data(Qt.UserRole)
                if data:
                    items.append(data)
        return items

    def sync_selection_fields(self):
        items = self.selected_items()
        if len(items) == 1:
            self.file_name_input.setText(items[0]["name"])
        elif len(items) > 1:
            self.file_name_input.setText(f"{len(items)} items selected")
        else:
            self.file_name_input.setText("")

    def handle_item_double_click(self, item):
        data = self.file_table.item(item.row(), 0).data(Qt.UserRole)
        if data and data["is_dir"]:
            self.current_path = data["path"]
            self.refresh_current_view()

    def open_selected_item(self):
        items = self.selected_items()
        if len(items) != 1:
            return
        if items[0]["is_dir"]:
            self.current_path = items[0]["path"]
            self.refresh_current_view()

    def show_file_context_menu(self, position):
        index = self.file_table.indexAt(position)
        if index.isValid():
            self.file_table.setCurrentCell(index.row(), index.column())
            self.file_table.selectRow(index.row())

        menu = QMenu(self)
        refresh_action = menu.addAction("Refresh")
        new_folder_action = menu.addAction("New Folder")
        rename_action = menu.addAction("Rename")
        delete_action = menu.addAction("Delete")
        copy_action = menu.addAction("Copy")
        move_action = menu.addAction("Move")
        details_action = menu.addAction("File Details")
        search_action = menu.addAction("Search")
        menu.addSeparator()
        download_action = menu.addAction("Download")

        action = menu.exec_(self.file_table.viewport().mapToGlobal(position))
        if action == refresh_action:
            self.refresh_current_view()
        elif action == new_folder_action:
            self.create_folder()
        elif action == rename_action:
            self.rename_selected()
        elif action == delete_action:
            self.delete_selected()
        elif action == copy_action:
            self.copy_selected()
        elif action == move_action:
            self.move_selected()
        elif action == details_action:
            self.get_fileinfo()
        elif action == search_action:
            self.search_items()
        elif action == download_action:
            self.export_selected()

    def create_folder(self):
        if not self.require_connection("Create folder"):
            return
        dialog = TextPromptDialog(self, "New Folder", "Folder name:")
        if dialog.exec_() != QDialog.Accepted:
            self.set_status("Create folder cancelled.")
            return
        folder_name = dialog.value()
        try:
            create_folder(self.current_path, folder_name)
            self.refresh_current_view()
            self.set_status(f"Created folder: {folder_name}")
        except Exception as exc:
            self.show_error("Unable to create folder", exc)

    def rename_selected(self):
        if not self.require_connection("Rename"):
            return
        items = self.selected_items()
        if len(items) != 1:
            self.show_info("Rename", "Select exactly one file or folder to rename.")
            return
        current_name = items[0]["name"]
        dialog = TextPromptDialog(self, "Rename", "New name:", current_name)
        if dialog.exec_() != QDialog.Accepted:
            self.set_status("Rename cancelled.")
            return
        try:
            rename_path(items[0]["path"], dialog.value())
            self.refresh_current_view()
            self.set_status(f"Renamed: {current_name}")
        except Exception as exc:
            self.show_error("Unable to rename item", exc)

    def delete_selected(self):
        if not self.require_connection("Delete"):
            return
        items = self.selected_items()
        if not items:
            self.show_info("Delete", "Select one or more items to delete.")
            return

        answer = QMessageBox.question(
            self,
            "Delete",
            f"Delete {len(items)} selected item(s)?",
            QMessageBox.Yes | QMessageBox.No,
            QMessageBox.No,
        )
        if answer != QMessageBox.Yes:
            self.set_status("Delete cancelled.")
            return

        completed = 0
        failed = 0
        for item in items:
            try:
                delete_path(item["path"])
                completed += 1
            except Exception:
                failed += 1
        self.refresh_current_view()
        self.set_status(f"Delete finished. Completed: {completed}, Failed: {failed}.")

    def copy_selected(self):
        if not self.require_connection("Copy"):
            return
        items = self.selected_items()
        if not items:
            self.show_info("Copy", "Select one or more items to copy.")
            return
        dialog = TextPromptDialog(self, "Copy", "Target remote folder:", self.current_path)
        if dialog.exec_() != QDialog.Accepted:
            self.set_status("Copy cancelled.")
            return
        target_dir = dialog.value().strip()
        if not target_dir:
            self.set_status("Copy cancelled. No destination selected.")
            return

        completed = 0
        failed = 0
        for item in items:
            try:
                copy_path(item["path"], target_dir)
                completed += 1
            except Exception as exc:
                failed += 1
                self.set_status(f"Copy failed for {item['name']}: {self._friendly_error(exc)}")
        self.set_status(f"Copy finished. Completed: {completed}, Failed: {failed}.")

    def move_selected(self):
        if not self.require_connection("Move"):
            return
        items = self.selected_items()
        if not items:
            self.show_info("Move", "Select one or more items to move.")
            return
        dialog = TextPromptDialog(self, "Move", "Target remote folder:", self.current_path)
        if dialog.exec_() != QDialog.Accepted:
            self.set_status("Move cancelled.")
            return
        target_dir = dialog.value().strip()
        if not target_dir:
            self.set_status("Move cancelled. No destination selected.")
            return

        completed = 0
        failed = 0
        for item in items:
            try:
                move_path(item["path"], target_dir)
                completed += 1
            except Exception as exc:
                failed += 1
                self.set_status(f"Move failed for {item['name']}: {self._friendly_error(exc)}")
        self.refresh_current_view()
        self.set_status(f"Move finished. Completed: {completed}, Failed: {failed}.")

    def get_fileinfo(self):
        if not self.require_connection("File details"):
            return
        items = self.selected_items()
        if len(items) != 1:
            self.show_info("File Details", "Select exactly one file or folder.")
            return

        try:
            info = get_file_info(items[0]["path"])
        except Exception as exc:
            self.show_error("Unable to read file details", exc)
            return

        QMessageBox.information(
            self,
            "File Details",
            f"File: {info['path']}\n"
            f"Type: {'Folder' if info['is_dir'] else 'File'}\n"
            f"Size: {human_size(info['size'])}\n"
            f"Last Modified: {info['modified']}\n"
            f"Created: {info['created']}",
        )

    def search_items(self):
        if not self.require_connection("Search"):
            return
        if self.search_thread is not None:
            self.set_status("Search is already running.")
            return
        dialog = TextPromptDialog(self, "Search", "Search current tree for file name:")
        if dialog.exec_() != QDialog.Accepted:
            self.set_status("Search cancelled.")
            return
        query = dialog.value().strip()
        if not query:
            self.set_status("Search cancelled.")
            return

        self.set_status(f"Searching for '{query}'...")
        self.search_thread = QThread(self)
        self.search_worker = SearchWorker(self.current_path, query)
        self.search_worker.moveToThread(self.search_thread)
        self.search_thread.started.connect(self.search_worker.run)
        self.search_worker.finished.connect(lambda results: self.on_search_finished(query, results))
        self.search_worker.failed.connect(self.on_search_failed)
        self.search_worker.finished.connect(self.search_thread.quit)
        self.search_worker.failed.connect(self.search_thread.quit)
        self.search_thread.finished.connect(self.cleanup_search)
        self.search_thread.start()

    def on_search_finished(self, query, results):
        if not results:
            self.show_info("Search", "No matching files found.")
            self.set_status(f"No matches for '{query}'.")
            return

        preview = "\n".join(item["path"] for item in results[:10])
        suffix = "\n..." if len(results) > 10 else ""
        self.show_info("Search Results", f"Found {len(results)} item(s):\n{preview}{suffix}")
        self.set_status(f"Found {len(results)} matching item(s).")

    def on_search_failed(self, message):
        self.show_error("Unable to search files", message)

    def cleanup_search(self):
        if self.search_worker is not None:
            self.search_worker.deleteLater()
        if self.search_thread is not None:
            self.search_thread.deleteLater()
        self.search_worker = None
        self.search_thread = None

    def upload_dropped_paths(self, paths):
        if not self.require_connection("Upload"):
            return
        completed = 0
        failed = 0
        for path in paths:
            path_obj = Path(path)
            if path_obj.is_dir():
                failed += 1
                self.set_status("Folder upload is not implemented by current API.")
                continue
            try:
                upload_file(str(path_obj), self.current_path)
                completed += 1
            except Exception as exc:
                failed += 1
                self.set_status(f"Upload failed for {path_obj.name}: {self._friendly_error(exc)}")
        self.refresh_current_view()
        self.set_status(f"Upload finished. Completed: {completed}, Failed: {failed}.")

    def export_selected(self):
        if not self.require_connection("Download"):
            return
        items = self.selected_items()
        if not items:
            self.show_info("Download", "Select file(s) first.")
            return

        target_dir = QFileDialog.getExistingDirectory(self, "Select download folder")
        if not target_dir:
            self.set_status("Download cancelled. No destination selected.")
            return

        completed = 0
        failed = 0
        for item in items:
            if item["is_dir"]:
                failed += 1
                self.set_status("Folder download is not implemented by current API.")
                continue
            try:
                download_file(item["path"], target_dir)
                completed += 1
            except Exception as exc:
                failed += 1
                self.set_status(f"Download failed for {item['name']}: {self._friendly_error(exc)}")
        self.set_status(f"Download finished. Completed: {completed}, Failed: {failed}.")

    def _friendly_error(self, error):
        message = str(error)
        lowered = message.lower()
        if "not supported by the current api" in lowered:
            return "This operation is not available on the server."
        return message

    def show_info(self, title, message):
        QMessageBox.information(self, title, message)

    def show_error(self, title, error):
        QMessageBox.critical(self, title, str(error))
        self.set_status(f"{title}: {error}")


if __name__ == "__main__":
    app = QApplication(sys.argv)
    window = FileManagerWindow()
    window.show()
    sys.exit(app.exec_())
