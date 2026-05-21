import json
import mimetypes
import uuid
from pathlib import Path, PurePosixPath
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen


API_BASE_URL = "http://127.0.0.1:5000/api"


def _request(method: str, path: str, *, query=None, data=None, headers=None, timeout=30):
    url = f"{API_BASE_URL}{path}"
    if query:
        url = f"{url}?{urlencode(query)}"

    body = None
    request_headers = headers.copy() if headers else {}
    if data is not None:
        body = json.dumps(data).encode("utf-8")
        request_headers["Content-Type"] = "application/json"

    try:
        with urlopen(Request(url, data=body, headers=request_headers, method=method), timeout=timeout) as response:
            content_type = response.headers.get("Content-Type", "")
            payload = response.read()
    except HTTPError as exc:
        message = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(message or f"HTTP {exc.code}") from exc
    except URLError as exc:
        raise RuntimeError("LocalServer is not running.") from exc

    if "application/json" not in content_type:
        return payload
    return json.loads(payload.decode("utf-8"))


def _status_ok(status: dict) -> bool:
    return bool(status and status.get("isSuccess"))


def _ensure_status(result):
    status = result.get("status") if isinstance(result, dict) else result
    if isinstance(status, dict) and not _status_ok(status):
        raise RuntimeError(status.get("message", "Operation failed."))
    return result


def _file_item_to_gui(item: dict) -> dict:
    is_dir = bool(item.get("isDirectory"))
    full_path = item.get("fullPath") or item.get("path") or item.get("name", "")
    return {
        "name": item.get("name", ""),
        "path": full_path,
        "type": "dir" if is_dir else "file",
        "is_dir": is_dir,
        "size": int(item.get("size") or 0),
        "modified": item.get("lastModified") or "",
        "created": "",
        "permissions": item.get("permissions") or "",
    }


def _multipart_upload(remote_path: str, local_path: str):
    source = Path(local_path)
    if not source.exists():
        raise FileNotFoundError(f"Source file not found: {source}")
    if source.is_dir():
        raise IsADirectoryError("Folder upload is not supported.")

    boundary = f"----FileManagerBoundary{uuid.uuid4().hex}"
    mime = mimetypes.guess_type(source.name)[0] or "application/octet-stream"
    file_bytes = source.read_bytes()

    chunks = [
        f"--{boundary}\r\n".encode("utf-8"),
        b'Content-Disposition: form-data; name="remotePath"\r\n\r\n',
        remote_path.encode("utf-8"),
        b"\r\n",
        f"--{boundary}\r\n".encode("utf-8"),
        f'Content-Disposition: form-data; name="file"; filename="{source.name}"\r\n'.encode("utf-8"),
        f"Content-Type: {mime}\r\n\r\n".encode("utf-8"),
        file_bytes,
        b"\r\n",
        f"--{boundary}--\r\n".encode("utf-8"),
    ]
    body = b"".join(chunks)
    headers = {"Content-Type": f"multipart/form-data; boundary={boundary}"}

    with urlopen(Request(f"{API_BASE_URL}/files/upload", data=body, headers=headers, method="POST"), timeout=120) as response:
        return json.loads(response.read().decode("utf-8"))


def get_home() -> str:
    roots = list_roots()
    return roots[0]["path"] if roots else "/"


def list_roots() -> list[dict]:
    return _request("GET", "/files/roots")


def list_files(path: str, include_hidden: bool = True) -> dict:
    result = _ensure_status(_request("GET", "/files", query={"path": path, "includeHidden": str(include_hidden).lower()}))
    return {
        "path": path,
        "items": [_file_item_to_gui(item) for item in result.get("data") or []],
    }


def get_file_info(path: str) -> dict:
    result = _ensure_status(_request("GET", "/files/info", query={"path": path}))
    data = result.get("data")
    if not data:
        raise FileNotFoundError(path)
    return _file_item_to_gui(data)


def exists_path(path: str) -> bool:
    result = _request("GET", "/files/exists", query={"path": path})
    return bool(result.get("exists"))


def search_files(path: str, query: str, limit: int = 100) -> list[dict]:
    result = _ensure_status(_request("GET", "/files/search", query={"path": path, "query": query, "limit": limit}))
    return [_file_item_to_gui(item) for item in result.get("data") or []]


def create_folder(parent_path: str, folder_name: str) -> str:
    target = f"{parent_path.rstrip('/')}/{folder_name.strip()}"
    status = _request("POST", "/files/create-dir", data={"path": target})
    if not _status_ok(status):
        raise RuntimeError(status.get("message", "Create folder failed."))
    return target


def rename_path(path: str, new_name: str) -> str:
    target = str(PurePosixPath(path).with_name(new_name.strip()))
    status = _request("POST", "/files/rename", data={"oldPath": path, "newPath": target})
    if not _status_ok(status):
        raise RuntimeError(status.get("message", "Rename failed."))
    return target


def delete_path(path: str) -> None:
    info = get_file_info(path)
    endpoint = "/files/delete-dir" if info["is_dir"] else "/files/delete-file"
    status = _request("POST", endpoint, data={"path": path})
    if not _status_ok(status):
        raise RuntimeError(status.get("message", "Delete failed."))


def copy_path(path: str, target_dir: str) -> str:
    result = _request("POST", "/files/copy", data={"sourcePaths": [path], "targetPath": target_dir, "overwrite": False})
    if not result.get("isSuccess"):
        raise RuntimeError("Copy failed.")
    return f"{target_dir.rstrip('/')}/{Path(path).name}"


def move_path(path: str, target_dir: str) -> str:
    result = _request("POST", "/files/move", data={"sourcePaths": [path], "targetPath": target_dir, "overwrite": False})
    if not result.get("isSuccess"):
        raise RuntimeError("Move failed.")
    return f"{target_dir.rstrip('/')}/{Path(path).name}"


def upload_file(source_path: str, target_dir: str) -> str:
    remote_path = f"{target_dir.rstrip('/')}/{Path(source_path).name}"
    status = _multipart_upload(remote_path, source_path)
    if not _status_ok(status):
        raise RuntimeError(status.get("message", "Upload failed."))
    return remote_path


def download_file(source_path: str, target_dir: str) -> str:
    payload = _request("POST", "/files/download", data={"remotePath": source_path}, timeout=120)
    destination = Path(target_dir) / Path(source_path).name
    if destination.exists():
        raise FileExistsError(f"Target already exists: {destination.name}")
    destination.write_bytes(payload)
    return str(destination)
