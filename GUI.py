import os
import sys
from datetime import datetime
from pathlib import Path

from PyQt5.QtCore import QEvent, QPoint, QItemSelectionModel, Qt
from PyQt5.QtWidgets import (
    QApplication,
    QDialog,
    QDialogButtonBox,
    QFileDialog,
    QGroupBox,
    QLabel,
    QLineEdit,
    QListView,
    QMenu,
    QMessageBox,
    QAbstractItemView,
    QTreeView,
    QVBoxLayout,
)

from local_api import (
    copy_path,
    create_folder,
    delete_path,
    download_file,
    move_path,
    upload_file,
)


class FileSearcher:
    def __init__(self, root_path):
        self.root_path = root_path

    def search_file(self, name):
        matches = []
        for dirpath, _, filenames in os.walk(self.root_path):
            for filename in filenames:
                if name.lower() in filename.lower():
                    matches.append(os.path.join(dirpath, filename))
        return matches


class TextPromptDialog(QDialog):
    def __init__(self, parent, title, label, initial_text=""):
        super().__init__(parent)
        self.setWindowTitle(title)
        self.setModal(True)
        self.resize(360, 110)

        layout = QVBoxLayout(self)
        prompt_label = QLabel(label)
        self.text_input = QLineEdit()
        self.text_input.setText(initial_text)
        self.text_input.selectAll()

        buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)

        layout.addWidget(prompt_label)
        layout.addWidget(self.text_input)
        layout.addWidget(buttons)

    def value(self):
        return self.text_input.text()


class FileDialog(QFileDialog):
    def __init__(self):
        super().__init__()
        self.setOption(QFileDialog.DontUseNativeDialog, True)
        self.setFileMode(QFileDialog.ExistingFiles)
        self.setOption(QFileDialog.ReadOnly, False)
        self.searcher = FileSearcher(str(Path.home()))
        self.file_views = []
        self.drop_targets = set()
        self.viewport_to_view = {}
        self.setup_ui()

    def setup_ui(self):
        self.setWindowTitle("SFTP/FTP File Manager")
        self.resize(1020, 700)

        qr = self.frameGeometry()
        cp = QApplication.desktop().availableGeometry().center()
        qr.moveCenter(cp)
        self.move(qr.topLeft())

        root_layout = QVBoxLayout()

        group_box_file = QGroupBox("SFTP/FTP File Manager")
        main_layout = self.layout()
        self.status_label = QLabel("Current folder.")
        self.status_label.setWordWrap(True)
        main_layout.addWidget(self.status_label)
        group_box_file.setLayout(main_layout)

        root_layout.addWidget(group_box_file, 1)
        self.setLayout(root_layout)
        self.install_file_view_context_menu()

        self.setStyleSheet(
            """
            QFileDialog, QWidget { background: #f3f5f8; color: #1f2f3f; }
            QGroupBox {
                border: 1px solid #3a4755;
                border-radius: 6px;
                margin-top: 8px;
                font-weight: 600;
                background: #161b22;
            }
            QGroupBox::title {
                subcontrol-origin: margin;
                left: 10px;
                padding: 0 4px;
            }
            QPushButton {
                background: #2d5a88;
                color: #edf3fb;
                border: none;
                border-radius: 4px;
                padding: 8px 10px;
            }
            QPushButton:hover { background: #36699f; }
            QLabel { background: transparent; color: #c7d1db; }
            QFileDialog, QWidget {
                background: #0f141a;
                color: #d7dee7;
            }
            QLineEdit, QListView, QTreeView, QTableWidget {
                background: #111821;
                color: #d7dee7;
                border: 1px solid #364250;
                selection-background-color: #22415f;
                selection-color: #eef4fb;
            }
            QHeaderView::section {
                background: #18212b;
                color: #d7dee7;
                border: 1px solid #364250;
                padding: 5px;
            }
            """
        )

    def install_file_view_context_menu(self):
        views = self.findChildren(QListView) + self.findChildren(QTreeView)
        self.file_views = views
        for view in self.file_views:
            view.setEditTriggers(
                QAbstractItemView.EditKeyPressed | QAbstractItemView.SelectedClicked
            )
            if hasattr(view, "clicked"):
                view.clicked.connect(lambda index, current_view=view: self.show_file_action_menu(current_view, index))
            view.setAcceptDrops(True)
            view.setDragEnabled(True)
            view.setDropIndicatorShown(True)
            view.viewport().setAcceptDrops(True)
            view.viewport().installEventFilter(self)
            self.drop_targets.add(view.viewport())
            self.viewport_to_view[view.viewport()] = view

    def eventFilter(self, watched, event):
        if watched in self.drop_targets:
            view = self.viewport_to_view.get(watched)
            if event.type() == QEvent.MouseButtonPress and event.button() == Qt.RightButton and view is not None:
                index = view.indexAt(event.pos())
                if index.isValid():
                    view.setCurrentIndex(index)
                    if view.selectionModel():
                        view.selectionModel().select(index, QItemSelectionModel.ClearAndSelect)
                global_pos = watched.mapToGlobal(event.pos())
                self.show_file_action_menu(view, index if index.isValid() else None, global_pos)
                return True
            if event.type() == QEvent.DragEnter and event.mimeData().hasUrls():
                event.acceptProposedAction()
                return True
            if event.type() == QEvent.DragMove and event.mimeData().hasUrls():
                event.acceptProposedAction()
                return True
            if event.type() == QEvent.Drop and event.mimeData().hasUrls():
                paths = [url.toLocalFile() for url in event.mimeData().urls() if url.isLocalFile()]
                if paths:
                    self.upload_dropped_paths(paths)
                    event.acceptProposedAction()
                    return True
        return super().eventFilter(watched, event)

    def show_file_action_menu(self, view, index=None, global_pos=None):
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
        export_action = menu.addAction("Download / Export")

        if index is not None and not index.isValid():
            return

        if global_pos is None:
            rect = view.visualRect(index)
            anchor = rect.bottomLeft() if rect.isValid() else QPoint(0, 0)
            global_pos = view.viewport().mapToGlobal(anchor)

        action = menu.exec_(global_pos)
        if action == refresh_action:
            self.refresh_current_view()
        elif action == new_folder_action:
            self.create_folder()
        elif action == rename_action:
            self.rename_selected(view)
        elif action == delete_action:
            self.delete_selected()
        elif action == copy_action:
            self.copy_selected()
        elif action == move_action:
            self.move_selected()
        elif action == details_action:
            self.get_fileinfo()
        elif action == search_action:
            self.search_files()
        elif action == export_action:
            self.export_selected()

    def current_path(self):
        return self.directory().absolutePath()

    def set_status(self, message):
        self.status_label.setText(message)

    def refresh_current_view(self):
        current_path = self.current_path()
        self.setDirectory(current_path)
        self.set_status(f"Refreshed: {current_path}")

    def selected_paths(self):
        return [path for path in self.selectedFiles() if path]

    def create_folder(self):
        dialog = TextPromptDialog(self, "New Folder", "Folder name:")
        if dialog.exec_() != QDialog.Accepted:
            self.set_status("Create folder cancelled.")
            return
        folder_name = dialog.value()
        try:
            create_folder(self.current_path(), folder_name)
            self.refresh_current_view()
            self.set_status(f"Created folder: {folder_name}")
        except Exception as exc:
            self.show_error("Unable to create folder", exc)

    def rename_selected(self, preferred_view=None):
        paths = self.selected_paths()
        if len(paths) != 1:
            self.show_info("Rename", "Select exactly one file or folder to rename.")
            return
        view = preferred_view or self.active_file_view()
        if view is None:
            self.show_info("Rename", "Unable to locate file view for inline rename.")
            return
        index = view.currentIndex()
        if not index.isValid():
            self.show_info("Rename", "Select exactly one file or folder to rename.")
            return
        view.edit(index)
        self.set_status("Inline rename started.")

    def active_file_view(self):
        for view in self.file_views:
            if view.hasFocus():
                return view
        for view in self.file_views:
            if view.selectionModel() and view.selectionModel().hasSelection():
                return view
        return self.file_views[0] if self.file_views else None

    def delete_selected(self):
        paths = self.selected_paths()
        if not paths:
            self.show_info("Delete", "Select one or more items to delete.")
            return
        answer = QMessageBox.question(
            self,
            "Delete",
            f"Delete {len(paths)} selected item(s)?",
            QMessageBox.Yes | QMessageBox.No,
            QMessageBox.No,
        )
        if answer != QMessageBox.Yes:
            self.set_status("Delete cancelled.")
            return
        completed = 0
        failed = 0
        for path in paths:
            try:
                delete_path(path)
                completed += 1
            except Exception:
                failed += 1
        self.refresh_current_view()
        self.set_status(f"Delete finished. Completed: {completed}, Failed: {failed}.")

    def copy_selected(self):
        paths = self.selected_paths()
        if not paths:
            self.show_info("Copy", "Select one or more items to copy.")
            return
        target_dir = QFileDialog.getExistingDirectory(self, "Copy selected to...")
        if not target_dir:
            self.set_status("Copy cancelled. No destination selected.")
            return
        completed = 0
        failed = 0
        for path in paths:
            try:
                copy_path(path, target_dir)
                completed += 1
            except Exception:
                failed += 1
        self.set_status(f"Copy finished. Completed: {completed}, Failed: {failed}.")

    def move_selected(self):
        paths = self.selected_paths()
        if not paths:
            self.show_info("Move", "Select one or more items to move.")
            return
        target_dir = QFileDialog.getExistingDirectory(self, "Move selected to...")
        if not target_dir:
            self.set_status("Move cancelled. No destination selected.")
            return
        completed = 0
        failed = 0
        for path in paths:
            try:
                move_path(path, target_dir)
                completed += 1
            except Exception:
                failed += 1
        self.refresh_current_view()
        self.set_status(f"Move finished. Completed: {completed}, Failed: {failed}.")

    def get_fileinfo(self):
        paths = self.selected_paths()
        if len(paths) != 1:
            self.show_info("File Details", "Select exactly one file or folder.")
            return

        file_path = paths[0]
        filesize = os.path.getsize(file_path)
        last_modified = datetime.fromtimestamp(os.path.getmtime(file_path)).strftime("%Y-%m-%d %H:%M:%S")
        creation_date = datetime.fromtimestamp(os.path.getctime(file_path)).strftime("%Y-%m-%d %H:%M:%S")

        QMessageBox.information(
            self,
            "File Details",
            f"File: {file_path}\n"
            f"Size: {filesize} bytes\n"
            f"Last Modified: {last_modified}\n"
            f"Created: {creation_date}",
        )

    def search_files(self):
        dialog = TextPromptDialog(self, "Search", "Search current tree for file name:")
        if dialog.exec_() != QDialog.Accepted:
            self.set_status("Search cancelled.")
            return
        name = dialog.value()
        if not name.strip():
            self.set_status("Search cancelled.")
            return

        searcher = FileSearcher(self.current_path())
        results = searcher.search_file(name.strip())
        if not results:
            self.show_info("Search", "No matching files found.")
            self.set_status(f"No matches for '{name}'.")
            return

        preview = "\n".join(results[:10])
        suffix = "\n..." if len(results) > 10 else ""
        self.show_info("Search Results", f"Found {len(results)} item(s):\n{preview}{suffix}")
        self.set_status(f"Found {len(results)} matching file(s).")

    def upload_dropped_paths(self, paths):
        completed = 0
        failed = 0
        current_target = self.current_path()
        for path in paths:
            path_obj = Path(path)
            if path_obj.is_dir():
                failed += 1
                continue
            try:
                upload_file(str(path_obj), current_target)
                completed += 1
            except Exception:
                failed += 1
        self.refresh_current_view()
        self.set_status(
            f"Upload finished for dropped items. Completed: {completed}, Failed: {failed}."
        )

    def export_selected(self):
        paths = self.selected_paths()
        if not paths:
            self.show_info("Download / Export", "Select file(s) in the main file dialog first.")
            return
        target_dir = QFileDialog.getExistingDirectory(self, "Select export folder")
        if not target_dir:
            self.set_status("Download/export cancelled. No destination selected.")
            return

        completed = 0
        failed = 0
        for path in paths:
            path_obj = Path(path)
            if path_obj.is_dir():
                failed += 1
                continue
            try:
                download_file(str(path_obj), target_dir)
                completed += 1
            except Exception:
                failed += 1
        self.set_status(
            f"Download/export finished. Completed: {completed}, Failed: {failed}."
        )

    def show_info(self, title, message):
        QMessageBox.information(self, title, message)

    def show_error(self, title, error):
        QMessageBox.critical(self, title, str(error))
        self.set_status(f"{title}: {error}")


if __name__ == "__main__":
    app = QApplication(sys.argv)
    dialog = FileDialog()
    dialog.show()
    sys.exit(app.exec_())
