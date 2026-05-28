import json
import mimetypes
import os
import uuid
from pathlib import Path, PurePosixPath
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen


API_BASE_URL = os.getenv("LOCALSERVER_API_URL", "http://127.0.0.1:5116").rstrip("/")


_active_connection_id = None
_active_profile = None


def _request(method: str, path: str, *, query=None, data=None, headers=None, timeout=30, json_body=True):
    url = f"{API_BASE_URL}{path}"
    if query:
        url = f"{url}?{urlencode(query)}"

    body = None
    request_headers = headers.copy() if headers else {}
    if data is not None:
        if json_body:
            body = json.dumps(data).encode("utf-8")
            request_headers.setdefault("Content-Type", "application/json")
        elif isinstance(data, bytes):
            body = data
        else:
            body = str(data).encode("utf-8")

    try:
        with urlopen(Request(url, data=body, headers=request_headers, method=method), timeout=timeout) as response:
            content_type = response.headers.get("Content-Type", "")
            payload = response.read()
    except HTTPError as exc:
        message = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(message or f"HTTP {exc.code}") from exc
    except URLError as exc:
        raise RuntimeError("Server is not available.") from exc

    if "application/json" not in content_type:
        return payload
    if not payload:
        return None
    return json.loads(payload.decode("utf-8"))


def _request_text(method: str, path: str, *, query=None, data=None, timeout=30):
    url = f"{API_BASE_URL}{path}"
    if query:
        url = f"{url}?{urlencode(query)}"
    body = None if data is None else json.dumps(data).encode("utf-8")
    headers = {"Content-Type": "application/json"} if data is not None else {}
    try:
        with urlopen(Request(url, data=body, headers=headers, method=method), timeout=timeout) as response:
            return response.read().decode("utf-8")
    except HTTPError as exc:
        message = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(message or f"HTTP {exc.code}") from exc
    except URLError as exc:
        raise RuntimeError("Server is not available.") from exc


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


def _sort_items(items: list[dict]) -> list[dict]:
    return sorted(
        items,
        key=lambda item: (not item["is_dir"], item["name"].lower()),
    )


def _profile_id(profile: dict) -> str:
    return str(profile.get("id") or profile.get("Id") or "")


def _profile_name(profile: dict) -> str:
    return str(profile.get("name") or profile.get("Name") or "")


def _profile_host_profile(profile: dict) -> dict:
    return profile.get("hostProfile") or profile.get("HostProfile") or {}


def _profile_summary(profile: dict, *, source: str) -> dict:
    host_profile = _profile_host_profile(profile)
    auth = host_profile.get("Auth") or host_profile.get("auth") or {}
    return {
        "id": _profile_id(profile),
        "name": _profile_name(profile),
        "host": host_profile.get("Host") or host_profile.get("host") or "",
        "protocol": host_profile.get("Protocol") or host_profile.get("protocol") or "",
        "port": host_profile.get("Port") or host_profile.get("port") or "",
        "auth_type": auth.get("$type") or auth.get("type") or "",
        "username": auth.get("Username") or auth.get("username") or "",
        "source": source,
        "raw": profile,
    }


def _list_profile_ids() -> list[str]:
    result = _request("GET", "/profiles")
    return [str(item) for item in (result or [])]


def _get_profile(profile_id: str) -> dict:
    return _request("GET", f"/profiles/{profile_id}")


def _load_profiles() -> list[dict]:
    profiles = []
    for profile_id in _list_profile_ids():
        try:
            profiles.append(_get_profile(profile_id))
        except Exception:
            continue
    return profiles


def list_profiles() -> list[dict]:
    return [_profile_summary(profile, source="server") for profile in _load_profiles()]


def create_profile(name: str, host: str, protocol: str, port: int, auth: dict) -> dict:
    payload = {
        "Id": str(uuid.uuid4()),
        "Name": name.strip(),
        "HostProfile": {
            "Host": host.strip(),
            "Protocol": protocol,
            "Port": int(port),
            "Auth": auth,
        },
    }
    if not payload["Name"]:
        raise ValueError("Profile name is required.")
    if not payload["HostProfile"]["Host"]:
        raise ValueError("Host is required.")

    result = _request("POST", "/profiles", data=payload)
    profile_id = str(result or payload["Id"])
    try:
        return _profile_summary(_get_profile(profile_id), source="server")
    except Exception:
        return _profile_summary(payload, source="server")


def delete_profile(profile_id: str):
    if not profile_id:
        raise ValueError("Profile id is required.")
    _request("DELETE", f"/profiles/{profile_id}")


def _find_profile_by_id(profile_id: str) -> dict:
    profile_id = str(profile_id).strip()
    for profile in _load_profiles():
        if _profile_id(profile).lower() == profile_id.lower():
            return profile
    raise RuntimeError(f"Profile with Id {profile_id} was not found.")


def _create_connection(profile: dict) -> str:
    result = _request("POST", "/connections", data=profile)
    return str(result)


def _delete_connection(connection_id: str):
    _request("DELETE", f"/connections/{connection_id}")


def _connect_connection(connection_id: str):
    _request("POST", f"/connections/{connection_id}/connect")


def _disconnect_connection(connection_id: str):
    _request("POST", f"/connections/{connection_id}/disconnect")


def disconnect_current():
    global _active_connection_id, _active_profile
    if not _active_connection_id:
        _active_profile = None
        return
    connection_id = _active_connection_id
    try:
        _disconnect_connection(connection_id)
    finally:
        try:
            _delete_connection(connection_id)
        except Exception:
            pass
        _active_connection_id = None
        _active_profile = None


def _get_current_directory(connection_id: str) -> str:
    return _request_text("GET", f"/connections/{connection_id}/filesystem/dir/current")


def _change_directory(connection_id: str, path: str):
    _request("PATCH", f"/connections/{connection_id}/filesystem/dir/current", data=path)


def _get_connection_files(connection_id: str) -> list[dict]:
    return _request("GET", f"/connections/{connection_id}/filesystem") or []


def _get_file_info_raw(connection_id: str, path: str) -> dict:
    return _request("GET", f"/connections/{connection_id}/filesystem/info", data=path)


def _exists_file(connection_id: str, path: str) -> bool:
    return bool(_request("GET", f"/connections/{connection_id}/filesystem/file/exists", data=path))


def _exists_dir(connection_id: str, path: str) -> bool:
    return bool(_request("GET", f"/connections/{connection_id}/filesystem/dir/exists", data=path))


def _require_connection():
    global _active_connection_id
    if _active_connection_id:
        return _active_connection_id
    raise RuntimeError("No active connection.")


def connect_profile(profile_id: str) -> dict:
    global _active_connection_id, _active_profile

    profile = _find_profile_by_id(profile_id)
    disconnect_current()
    connection_id = None
    try:
        connection_id = _create_connection(profile)
        _connect_connection(connection_id)
        _active_connection_id = connection_id
        _active_profile = profile
        return {
            "connectionId": connection_id,
            "profile": profile,
        }
    except Exception as exc:
        if connection_id:
            try:
                _delete_connection(connection_id)
            except Exception:
                pass
        _active_connection_id = None
        _active_profile = None
        raise RuntimeError(f"Failed to connect profile: {exc}") from exc


def get_active_profile_name() -> str:
    if _active_profile:
        return _profile_name(_active_profile)
    return ""


def get_home() -> str:
    connection_id = _require_connection()
    return _get_current_directory(connection_id)


def list_roots() -> list[dict]:
    home = get_home()
    candidates = [
        {"name": "Home", "path": home, "kind": "folder"},
        {"name": "Root", "path": "/", "kind": "folder"},
        {"name": "tmp", "path": "/tmp", "kind": "folder"},
        {"name": "home", "path": "/home", "kind": "folder"},
        {"name": "www", "path": "/var/www", "kind": "folder"},
    ]
    seen = set()
    roots = []
    for item in candidates:
        path = item["path"]
        if path in seen:
            continue
        try:
            if exists_path(path):
                roots.append(item)
                seen.add(path)
        except Exception:
            continue
    return roots


def list_files(path: str, include_hidden: bool = True) -> dict:
    del include_hidden
    connection_id = _require_connection()
    _change_directory(connection_id, path)
    current_path = _get_current_directory(connection_id)
    items = _sort_items([_file_item_to_gui(item) for item in _get_connection_files(connection_id)])
    return {"path": current_path, "items": items}


def get_file_info(path: str) -> dict:
    connection_id = _require_connection()
    data = _get_file_info_raw(connection_id, path)
    return _file_item_to_gui(data)


def exists_path(path: str) -> bool:
    connection_id = _require_connection()
    return _exists_file(connection_id, path) or _exists_dir(connection_id, path)


def search_files(path: str, query: str, limit: int = 100) -> list[dict]:
    matches = []
    visited = set()
    connection_id = _require_connection()
    original_path = _get_current_directory(connection_id)

    def walk(folder: str):
        if folder in visited or len(matches) >= limit:
            return
        visited.add(folder)
        response = list_files(folder)
        for item in response["items"]:
            if query.lower() in item["name"].lower():
                matches.append(item)
                if len(matches) >= limit:
                    return
            if item["is_dir"]:
                walk(item["path"])

    try:
        walk(path)
    finally:
        try:
            _change_directory(connection_id, original_path)
        except Exception:
            pass
    return _sort_items(matches)


def create_folder(parent_path: str, folder_name: str) -> str:
    connection_id = _require_connection()
    target = str(PurePosixPath(parent_path) / folder_name.strip())
    _request("POST", f"/connections/{connection_id}/filesystem/dir", data=target)
    return target


def rename_path(path: str, new_name: str) -> str:
    connection_id = _require_connection()
    info = get_file_info(path)
    target = str(PurePosixPath(path).with_name(new_name.strip()))
    endpoint = "dir" if info["is_dir"] else "file"
    _request("PATCH", f"/connections/{connection_id}/filesystem/{endpoint}", data={"oldPath": path, "newPath": target})
    return target


def delete_path(path: str) -> None:
    connection_id = _require_connection()
    info = get_file_info(path)
    endpoint = "dir" if info["is_dir"] else "file"
    _request("DELETE", f"/connections/{connection_id}/filesystem/{endpoint}", data=path)


def copy_path(path: str, target_dir: str) -> str:
    connection_id = _require_connection()
    info = get_file_info(path)
    if info["is_dir"]:
        raise RuntimeError("Directory copy is not supported by the current API.")
    target = str(PurePosixPath(target_dir) / Path(path).name)
    _request(
        "POST",
        f"/connections/{connection_id}/filesystem/file/copy",
        data={"sourcePath": path, "targetPath": target, "canOverride": False},
    )
    return target


def move_path(path: str, target_dir: str) -> str:
    connection_id = _require_connection()
    info = get_file_info(path)
    if info["is_dir"]:
        raise RuntimeError("Directory move is not supported by the current API.")
    target = str(PurePosixPath(target_dir) / Path(path).name)
    _request(
        "PATCH",
        f"/connections/{connection_id}/filesystem/file/move",
        data={"sourcePath": path, "targetPath": target, "canOverride": False},
    )
    return target


def upload_file(source_path: str, target_dir: str) -> str:
    connection_id = _require_connection()
    source = Path(source_path)
    if not source.exists():
        raise FileNotFoundError(f"Source file not found: {source}")
    if source.is_dir():
        raise RuntimeError("Folder upload is not supported by the current API.")

    remote_path = str(PurePosixPath(target_dir) / source.name)
    boundary = f"----FileManagerBoundary{uuid.uuid4().hex}"
    mime = mimetypes.guess_type(source.name)[0] or "application/octet-stream"
    body = b"".join(
        [
            f"--{boundary}\r\n".encode("utf-8"),
            f'Content-Disposition: form-data; name="file"; filename="{source.name}"\r\n'.encode("utf-8"),
            f"Content-Type: {mime}\r\n\r\n".encode("utf-8"),
            source.read_bytes(),
            b"\r\n",
            f"--{boundary}--\r\n".encode("utf-8"),
        ]
    )
    headers = {"Content-Type": f"multipart/form-data; boundary={boundary}"}
    _request(
        "POST",
        f"/connections/{connection_id}/filesystem/file/upload",
        query={"remotePath": remote_path},
        data=body,
        headers=headers,
        timeout=120,
        json_body=False,
    )
    return remote_path


def download_file(source_path: str, target_dir: str) -> str:
    connection_id = _require_connection()
    payload = _request(
        "GET",
        f"/connections/{connection_id}/filesystem/file/download",
        query={"path": source_path},
        timeout=120,
    )
    destination = Path(target_dir) / Path(source_path).name
    if destination.exists():
        raise FileExistsError(f"Target already exists: {destination.name}")
    destination.write_bytes(payload)
    return str(destination)
