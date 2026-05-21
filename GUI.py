import os
import string
import sys
from pathlib import Path, PurePosixPath

from PyQt5.QtCore import QSize, QTimer, Qt
from PyQt5.QtWidgets import (
    QApplication,
    QAbstractItemView,
    QComboBox,
    QDialog,
    QDialogButtonBox,
    QFileDialog,
    QFileIconProvider,
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
    QStyle,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
    QWidget,
)

from local_api import (
    copy_path,
    create_folder,
    delete_path,
    download_file,
    exists_path,
    get_file_info,
    get_home,
    list_files,
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


class PlacesList(QListWidget):
    def __init__(self, parent_window):
        super().__init__()
        self.parent_window = parent_window
        self.setMinimumWidth(128)
        self.setMaximumWidth(140)
        self.itemClicked.connect(self.parent_window.handle_place_clicked)


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
        self.current_path = get_home()
        self.current_items = []
        self.current_view_mode = "details"
        self.icon_provider = QFileIconProvider()

        self.setWindowTitle("SFTP/FTP File Manager")
        self.resize(1020, 700)

        self.build_ui()
        self.apply_styles()
        self.populate_places()
        self.refresh_current_view()

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

        toolbar_row = QHBoxLayout()
        toolbar_row.setSpacing(5)

        toolbar_row.addWidget(QLabel("Look in:"))
        self.path_combo = QComboBox()
        self.path_combo.setEditable(True)
        self.path_combo.setFixedHeight(24)
        self.path_combo.lineEdit().returnPressed.connect(self.change_path)
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
                background: #0a1017;
                color: #f2f5f8;
                font-size: 12px;
            }
            QGroupBox {
                border: 1px solid #2e4052;
                border-radius: 5px;
                margin-top: 9px;
                padding-top: 3px;
                font-weight: 600;
            }
            QGroupBox::title {
                subcontrol-origin: margin;
                subcontrol-position: top left;
                left: 10px;
                padding: 0 4px;
                color: #ffffff;
                background: #0a1017;
            }
            QLabel {
                color: #f0f3f7;
            }
            QLineEdit, QComboBox, QListWidget, QTableWidget {
                background: #0d1620;
                color: #f2f5f8;
                border: 1px solid #2e4052;
                selection-background-color: #246aa8;
                selection-color: #ffffff;
            }
            QLineEdit, QComboBox {
                padding: 1px 4px;
            }
            QComboBox::drop-down {
                width: 20px;
                border: none;
            }
            QComboBox QAbstractItemView {
                background: #0d1620;
                border: 1px solid #2e4052;
                selection-background-color: #246aa8;
            }
            QPushButton {
                background: #101a25;
                color: #f2f5f8;
                border: 1px solid transparent;
                border-radius: 2px;
                padding: 1px;
            }
            QPushButton:hover {
                background: #1a2a3b;
                border-color: #49657e;
            }
            QPushButton:pressed {
                background: #0c151f;
            }
            QPushButton:checked {
                background: #243f5a;
                border-color: #5c7fa0;
            }
            QListWidget {
                outline: none;
            }
            QListWidget::item {
                min-height: 21px;
                padding: 2px 6px;
            }
            QListWidget::item:selected {
                background: #246aa8;
            }
            QTableWidget {
                outline: none;
                gridline-color: #223343;
                background: #0b141d;
            }
            QTableWidget::item {
                padding: 1px 4px;
                border: none;
            }
            QTableWidget::item:selected {
                background: #246aa8;
                color: #ffffff;
            }
            QHeaderView::section {
                background: #172536;
                color: #ffffff;
                border: none;
                border-right: 1px solid #2e4052;
                border-bottom: 1px solid #2e4052;
                padding: 3px 5px;
                min-height: 22px;
            }
            QMenu {
                background: #111b26;
                color: #f2f5f8;
                border: 1px solid #2e4052;
            }
            QMenu::item {
                padding: 5px 24px 5px 20px;
            }
            QMenu::item:selected {
                background: #246aa8;
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
            self.places_list.addItem(root_item)

    def set_status(self, message):
        self.status_label.setText(message)

    def refresh_current_view(self):
        try:
            response = list_files(self.current_path)
        except Exception as exc:
            self.show_error("Unable to load files", exc)
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
        new_path = self.path_combo.currentText().strip()
        if not new_path:
            return
        if not exists_path(new_path):
            self.show_error("Path not found", FileNotFoundError(new_path))
            return
        self.current_path = new_path
        self.refresh_current_view()

    def go_up(self):
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
                self.set_status(f"Copy failed for {item['name']}: {exc}")
        self.set_status(f"Copy finished. Completed: {completed}, Failed: {failed}.")

    def move_selected(self):
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
                self.set_status(f"Move failed for {item['name']}: {exc}")
        self.refresh_current_view()
        self.set_status(f"Move finished. Completed: {completed}, Failed: {failed}.")

    def get_fileinfo(self):
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
        dialog = TextPromptDialog(self, "Search", "Search current tree for file name:")
        if dialog.exec_() != QDialog.Accepted:
            self.set_status("Search cancelled.")
            return
        query = dialog.value().strip()
        if not query:
            self.set_status("Search cancelled.")
            return

        try:
            results = search_files(self.current_path, query)
        except Exception as exc:
            self.show_error("Unable to search files", exc)
            return

        if not results:
            self.show_info("Search", "No matching files found.")
            self.set_status(f"No matches for '{query}'.")
            return

        preview = "\n".join(item["path"] for item in results[:10])
        suffix = "\n..." if len(results) > 10 else ""
        self.show_info("Search Results", f"Found {len(results)} item(s):\n{preview}{suffix}")
        self.set_status(f"Found {len(results)} matching item(s).")

    def upload_dropped_paths(self, paths):
        completed = 0
        failed = 0
        for path in paths:
            path_obj = Path(path)
            if path_obj.is_dir():
                failed += 1
                continue
            try:
                upload_file(str(path_obj), self.current_path)
                completed += 1
            except Exception:
                failed += 1
        self.refresh_current_view()
        self.set_status(f"Upload finished. Completed: {completed}, Failed: {failed}.")

    def export_selected(self):
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
                continue
            try:
                download_file(item["path"], target_dir)
                completed += 1
            except Exception as exc:
                failed += 1
                self.set_status(f"Download failed for {item['name']}: {exc}")
        self.set_status(f"Download finished. Completed: {completed}, Failed: {failed}.")

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
