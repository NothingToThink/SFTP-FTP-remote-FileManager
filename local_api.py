import shutil
from pathlib import Path


def _ensure_directory(path: str) -> Path:
    target = Path(path)
    if not target.exists():
        raise FileNotFoundError(f"Path not found: {target}")
    if not target.is_dir():
        raise NotADirectoryError(f"Not a directory: {target}")
    return target


def create_folder(parent_path: str, folder_name: str) -> str:
    parent = _ensure_directory(parent_path)
    if not folder_name.strip():
        raise ValueError("Folder name is required.")
    destination = parent / folder_name.strip()
    destination.mkdir(exist_ok=False)
    return str(destination)


def rename_path(path: str, new_name: str) -> str:
    source = Path(path)
    if not source.exists():
        raise FileNotFoundError(f"Path not found: {source}")
    if not new_name.strip():
        raise ValueError("New name is required.")
    destination = source.with_name(new_name.strip())
    if destination.exists():
        raise FileExistsError(f"Target already exists: {destination.name}")
    source.rename(destination)
    return str(destination)


def delete_path(path: str) -> None:
    target = Path(path)
    if not target.exists():
        raise FileNotFoundError(f"Path not found: {target}")
    if target.is_dir():
        shutil.rmtree(target)
    else:
        target.unlink()


def copy_path(path: str, target_dir: str) -> str:
    source = Path(path)
    destination_dir = _ensure_directory(target_dir)
    if not source.exists():
        raise FileNotFoundError(f"Path not found: {source}")
    destination = destination_dir / source.name
    if destination.exists():
        raise FileExistsError(f"Target already exists: {destination.name}")
    if source.is_dir():
        shutil.copytree(source, destination)
    else:
        shutil.copy2(source, destination)
    return str(destination)


def move_path(path: str, target_dir: str) -> str:
    source = Path(path)
    destination_dir = _ensure_directory(target_dir)
    if not source.exists():
        raise FileNotFoundError(f"Path not found: {source}")
    destination = destination_dir / source.name
    if destination.exists():
        raise FileExistsError(f"Target already exists: {destination.name}")
    return str(shutil.move(str(source), str(destination)))


def upload_file(source_path: str, target_dir: str) -> str:
    source = Path(source_path)
    if not source.exists():
        raise FileNotFoundError(f"Source file not found: {source}")
    if source.is_dir():
        raise IsADirectoryError("Folder upload is not supported.")
    return copy_path(str(source), target_dir)


def download_file(source_path: str, target_dir: str) -> str:
    source = Path(source_path)
    if not source.exists():
        raise FileNotFoundError(f"Source file not found: {source}")
    if source.is_dir():
        raise IsADirectoryError("Folder download is not supported.")
    return copy_path(str(source), target_dir)
