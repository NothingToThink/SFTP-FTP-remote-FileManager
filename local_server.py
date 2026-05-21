import atexit
import cgi
import base64
import json
import re
import shutil
import subprocess
import tempfile
import threading
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path, PureWindowsPath
from urllib.parse import parse_qs, urlparse


HOST = "127.0.0.1"
PORT = 5000


WSL_SCRIPT = r"""
import base64
import json
import os
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path


def iso_time(timestamp):
    try:
        return datetime.fromtimestamp(timestamp, timezone.utc).isoformat()
    except (OSError, OverflowError, ValueError):
        return ""


def ensure_parent(path):
    parent = Path(path).expanduser().parent
    parent.mkdir(parents=True, exist_ok=True)


def ensure_directory(path):
    target = Path(path).expanduser()
    if not target.exists():
        raise FileNotFoundError(f"Path not found: {target}")
    if not target.is_dir():
        raise NotADirectoryError(f"Not a directory: {target}")
    return target


def permissions(path):
    try:
        mode = path.stat().st_mode
        bits = ""
        for shift in (6, 3, 0):
            bits += "r" if mode & (4 << shift) else "-"
            bits += "w" if mode & (2 << shift) else "-"
            bits += "x" if mode & (1 << shift) else "-"
        return bits
    except OSError:
        return ""


def file_item(path):
    path = Path(path).expanduser()
    stat = path.stat()
    return {
        "name": path.name or str(path),
        "size": 0 if path.is_dir() else stat.st_size,
        "lastModified": iso_time(stat.st_mtime),
        "isDirectory": path.is_dir(),
        "fullPath": str(path),
        "permissions": permissions(path),
    }


def safe_file_item(path):
    try:
        return file_item(path)
    except (OSError, PermissionError):
        return {
            "name": path.name or str(path),
            "size": 0,
            "lastModified": "",
            "isDirectory": False,
            "fullPath": str(path),
            "permissions": "",
        }


def copy_path(source_path, target_dir, overwrite=False):
    source = Path(source_path).expanduser()
    destination_dir = ensure_directory(target_dir)
    if not source.exists():
        raise FileNotFoundError(f"Path not found: {source}")
    destination = destination_dir / source.name
    if destination.exists():
        if not overwrite:
            raise FileExistsError(f"Target already exists: {destination}")
        if destination.is_dir():
            shutil.rmtree(destination)
        else:
            destination.unlink()
    if source.is_dir():
        shutil.copytree(source, destination)
    else:
        shutil.copy2(source, destination)
    return str(destination)


def move_path(source_path, target_dir, overwrite=False):
    source = Path(source_path).expanduser()
    destination_dir = ensure_directory(target_dir)
    if not source.exists():
        raise FileNotFoundError(f"Path not found: {source}")
    destination = destination_dir / source.name
    if destination.exists():
        if not overwrite:
            raise FileExistsError(f"Target already exists: {destination}")
        if destination.is_dir():
            shutil.rmtree(destination)
        else:
            destination.unlink()
    return shutil.move(str(source), str(destination))


def handle(payload):
    action = payload["action"]

    if action == "roots":
        return [
            {"name": "Home", "path": os.path.expanduser("~"), "kind": "home"},
            {"name": "Root", "path": "/", "kind": "root"},
            {"name": "tmp", "path": "/tmp", "kind": "folder"},
        ]

    if action == "list":
        directory = ensure_directory(payload["path"])
        include_hidden = payload.get("includeHidden", True)

        def sort_key(item):
            try:
                is_file = not item.is_dir()
            except OSError:
                is_file = True
            return is_file, item.name.lower()

        items = []
        for entry in sorted(directory.iterdir(), key=sort_key):
            if not include_hidden and entry.name.startswith("."):
                continue
            items.append(safe_file_item(entry))
        return items

    if action == "info":
        target = Path(payload["path"]).expanduser()
        if not target.exists():
            raise FileNotFoundError(f"Path not found: {target}")
        return file_item(target)

    if action == "exists":
        return Path(payload["path"]).expanduser().exists()

    if action == "search":
        root = ensure_directory(payload["path"])
        query = payload["query"].lower()
        limit = int(payload.get("limit", 100))
        results = []
        for dirpath, dirnames, filenames in os.walk(root):
            for name in dirnames + filenames:
                if query in name.lower():
                    results.append(safe_file_item(Path(dirpath) / name))
                    if len(results) >= limit:
                        return results
        return results

    if action == "create_file":
        target = Path(payload["path"]).expanduser()
        ensure_parent(target)
        if target.exists():
            raise FileExistsError(f"Target already exists: {target}")
        target.touch()
        return str(target)

    if action == "create_dir":
        target = Path(payload["path"]).expanduser()
        target.mkdir(parents=True, exist_ok=False)
        return str(target)

    if action == "delete_file":
        target = Path(payload["path"]).expanduser()
        if not target.exists():
            raise FileNotFoundError(f"Path not found: {target}")
        if target.is_dir():
            raise IsADirectoryError(f"Not a file: {target}")
        target.unlink()
        return str(target)

    if action == "delete_dir":
        target = Path(payload["path"]).expanduser()
        if not target.exists():
            raise FileNotFoundError(f"Path not found: {target}")
        if not target.is_dir():
            raise NotADirectoryError(f"Not a directory: {target}")
        shutil.rmtree(target)
        return str(target)

    if action == "rename":
        old_path = Path(payload["oldPath"]).expanduser()
        new_path = Path(payload["newPath"]).expanduser()
        if not old_path.exists():
            raise FileNotFoundError(f"Path not found: {old_path}")
        if new_path.exists():
            raise FileExistsError(f"Target already exists: {new_path}")
        old_path.rename(new_path)
        return str(new_path)

    if action == "copy":
        return [
            copy_path(path, payload["targetPath"], payload.get("overwrite", False))
            for path in payload["sourcePaths"]
        ]

    if action == "move":
        return [
            move_path(path, payload["targetPath"], payload.get("overwrite", False))
            for path in payload["sourcePaths"]
        ]

    if action == "save_uploaded_file":
        source = Path(payload["localPath"]).expanduser()
        remote_path = Path(payload["remotePath"]).expanduser()
        if not source.exists():
            raise FileNotFoundError(f"Upload temp file not found: {source}")
        ensure_parent(remote_path)
        if remote_path.exists() and not payload.get("overwrite", False):
            raise FileExistsError(f"Target already exists: {remote_path}")
        shutil.copy2(source, remote_path)
        return str(remote_path)

    if action == "read_file":
        source = Path(payload["remotePath"]).expanduser()
        if not source.exists():
            raise FileNotFoundError(f"Path not found: {source}")
        if source.is_dir():
            raise IsADirectoryError(f"Not a file: {source}")
        return {"name": source.name, "content": base64.b64encode(source.read_bytes()).decode("ascii")}

    raise ValueError(f"Unknown action: {action}")


def run_payload(payload):
    try:
        return {"ok": True, "result": handle(payload)}
    except Exception as exc:
        return {"ok": False, "error": f"{type(exc).__name__}: {exc}"}


for line in sys.stdin:
    line = line.strip()
    if not line:
        continue
    print(json.dumps(run_payload(json.loads(line)), ensure_ascii=False), flush=True)
"""


class WslClient:
    def __init__(self):
        self.process = None
        self.lock = threading.Lock()

    def close(self):
        if self.process is None:
            return
        try:
            self.process.terminate()
        except Exception:
            pass
        self.process = None

    def call(self, action, **payload):
        payload["action"] = action
        for key in ("path", "oldPath", "newPath", "targetPath", "remotePath", "localPath"):
            if key in payload and payload[key] is not None:
                payload[key] = to_wsl_path(payload[key])
        if "sourcePaths" in payload:
            payload["sourcePaths"] = [to_wsl_path(path) for path in payload["sourcePaths"]]

        with self.lock:
            process = self._process()
            try:
                process.stdin.write(json.dumps(payload, ensure_ascii=False) + "\n")
                process.stdin.flush()
                response = json.loads(process.stdout.readline())
            except Exception:
                self.close()
                raise

        if not response.get("ok"):
            raise RuntimeError(response.get("error", "WSL error"))
        return response.get("result")

    def _process(self):
        if self.process is None or self.process.poll() is not None:
            self.process = subprocess.Popen(
                ["wsl.exe", "python3", "-u", "-c", WSL_SCRIPT],
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                stderr=subprocess.DEVNULL,
                text=True,
                encoding="utf-8",
                bufsize=1,
                **hide_window_kwargs(),
            )
        return self.process


def hide_window_kwargs():
    if not hasattr(subprocess, "STARTUPINFO"):
        return {}
    startupinfo = subprocess.STARTUPINFO()
    startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    return {"startupinfo": startupinfo, "creationflags": getattr(subprocess, "CREATE_NO_WINDOW", 0)}


def to_wsl_path(path):
    path = str(path).strip()
    if not path:
        return path
    unc_match = re.match(r"^\\\\wsl(?:\.localhost)?\\[^\\]+\\(.+)$", path, re.IGNORECASE)
    if unc_match:
        return "/" + unc_match.group(1).replace("\\", "/")
    drive_match = re.match(r"^([a-zA-Z]):[\\/]*(.*)$", path)
    if drive_match:
        drive = drive_match.group(1).lower()
        rest = drive_match.group(2).replace("\\", "/")
        return f"/mnt/{drive}/{rest}".rstrip("/")
    if "\\" in path:
        windows_path = PureWindowsPath(path)
        if windows_path.drive:
            drive = windows_path.drive.rstrip(":").lower()
            rest = "/".join(windows_path.parts[1:])
            return f"/mnt/{drive}/{rest}".rstrip("/")
    return path


wsl = WslClient()
connection_is_open = False
atexit.register(wsl.close)


def ok(message="ok"):
    return {"code": 0, "message": message, "isSuccess": True}


def fail(message, code=1):
    return {"code": code, "message": str(message), "isSuccess": False}


def query_result(data, message="files received"):
    return {"data": data, "status": ok(message), "isSuccess": True}


def bulk_result(source_paths, target_path, action):
    items = []
    completed = 0
    failed = 0
    for source in source_paths:
        try:
            result = wsl.call(action, sourcePaths=[source], targetPath=target_path)
            target = result[0] if result else None
            completed += 1
            items.append({"sourcePath": source, "targetPath": target, "status": ok("done")})
        except Exception as exc:
            failed += 1
            items.append({"sourcePath": source, "targetPath": target_path, "status": fail(exc)})
    return {"isSuccess": failed == 0, "completed": completed, "failed": failed, "items": items}


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_request_handled(self, status, content_type, size):
        now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        print(
            f"[{now}] {self.command} {self.path} -> {status} "
            f"{content_type} {size} bytes",
            flush=True,
        )

    def do_GET(self):
        parsed = urlparse(self.path)
        params = {key: values[-1] for key, values in parse_qs(parsed.query).items()}
        try:
            if parsed.path == "/api/files/roots":
                self.send_json(wsl.call("roots"))
            elif parsed.path == "/api/files":
                include_hidden = params.get("includeHidden", "true").lower() == "true"
                self.send_json(query_result(wsl.call("list", path=params.get("path", "/"), includeHidden=include_hidden)))
            elif parsed.path == "/api/files/info":
                self.send_json(query_result(wsl.call("info", path=params["path"]), "file received"))
            elif parsed.path == "/api/files/exists":
                self.send_json({"path": params.get("path", ""), "exists": bool(wsl.call("exists", path=params.get("path", "")))})
            elif parsed.path == "/api/files/search":
                self.send_json(query_result(wsl.call("search", path=params["path"], query=params["query"], limit=int(params.get("limit", 100)))))
            elif parsed.path == "/api/connection/state":
                self.send_json({"isConnected": connection_is_open})
            elif parsed.path in ("/docs/api.json", "/api.json"):
                self.send_file(Path(__file__).with_name("api.json"), "application/json")
            elif parsed.path in ("/docs/", "/docs"):
                self.send_file(Path(__file__).with_name("docs.html"), "text/html; charset=utf-8")
            else:
                self.send_error_json(404, "Not found")
        except Exception as exc:
            self.send_error_json(500, exc)

    def do_POST(self):
        global connection_is_open
        parsed = urlparse(self.path)
        try:
            if parsed.path == "/api/connection/connect":
                self.read_json()
                connection_is_open = True
                self.send_json(ok("connected"))
            elif parsed.path == "/api/connection/test":
                self.read_json()
                self.send_json(ok("connection ok"))
            elif parsed.path == "/api/connection/disconnect":
                connection_is_open = False
                self.send_json(ok("disconnected"))
            elif parsed.path == "/api/files/create-file":
                self.send_json(self.status_call("create_file", **self.read_json()))
            elif parsed.path == "/api/files/create-dir":
                self.send_json(self.status_call("create_dir", **self.read_json()))
            elif parsed.path == "/api/files/delete-file":
                self.send_json(self.status_call("delete_file", **self.read_json()))
            elif parsed.path == "/api/files/delete-dir":
                self.send_json(self.status_call("delete_dir", **self.read_json()))
            elif parsed.path == "/api/files/delete":
                self.send_json(self.delete_many(self.read_json().get("paths", [])))
            elif parsed.path == "/api/files/rename":
                self.send_json(self.status_call("rename", **self.read_json()))
            elif parsed.path == "/api/files/copy":
                data = self.read_json()
                self.send_json(bulk_result(data.get("sourcePaths", []), data.get("targetPath", "/"), "copy"))
            elif parsed.path == "/api/files/move":
                data = self.read_json()
                self.send_json(bulk_result(data.get("sourcePaths", []), data.get("targetPath", "/"), "move"))
            elif parsed.path == "/api/files/upload":
                self.handle_upload()
            elif parsed.path == "/api/files/download":
                data = self.read_json()
                self.handle_download(data["remotePath"])
            else:
                self.send_error_json(404, "Not found")
        except Exception as exc:
            self.send_error_json(500, exc)

    def read_json(self):
        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0:
            return {}
        return json.loads(self.rfile.read(length).decode("utf-8"))

    def status_call(self, action, **payload):
        try:
            wsl.call(action, **payload)
            return ok("done")
        except Exception as exc:
            return fail(exc)

    def delete_many(self, paths):
        completed = 0
        failed = 0
        items = []
        for path in paths:
            try:
                info = wsl.call("info", path=path)
                wsl.call("delete_dir" if info.get("isDirectory") else "delete_file", path=path)
                completed += 1
                items.append({"sourcePath": path, "targetPath": None, "status": ok("deleted")})
            except Exception as exc:
                failed += 1
                items.append({"sourcePath": path, "targetPath": None, "status": fail(exc)})
        return {"isSuccess": failed == 0, "completed": completed, "failed": failed, "items": items}

    def handle_upload(self):
        form = cgi.FieldStorage(fp=self.rfile, headers=self.headers, environ={"REQUEST_METHOD": "POST"})
        remote_path = form.getfirst("remotePath")
        file_item = form["file"] if "file" in form else None
        if not remote_path or file_item is None:
            self.send_json(fail("remotePath and file are required"), 400)
            return
        with tempfile.NamedTemporaryFile(delete=False) as tmp:
            shutil.copyfileobj(file_item.file, tmp)
            tmp_path = tmp.name
        try:
            wsl.call("save_uploaded_file", localPath=tmp_path, remotePath=remote_path)
            self.send_json(ok("uploaded"))
        finally:
            Path(tmp_path).unlink(missing_ok=True)

    def handle_download(self, remote_path):
        result = wsl.call("read_file", remotePath=remote_path)
        data = base64.b64decode(result["content"])
        name = result.get("name") or Path(remote_path).name
        headers = {"Content-Disposition": f'attachment; filename="{name}"'}
        self.send_bytes(data, "application/octet-stream", headers)

    def send_json(self, payload, status=200):
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_bytes(data, "application/json; charset=utf-8", status=status)

    def send_error_json(self, status, message):
        self.send_json(fail(message), status)

    def send_file(self, path, content_type):
        self.send_bytes(path.read_bytes(), content_type)

    def send_bytes(self, data, content_type, headers=None, status=200):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Connection", "close")
        for key, value in (headers or {}).items():
            self.send_header(key, value)
        self.end_headers()
        self.wfile.write(data)
        self.log_request_handled(status, content_type, len(data))

    def log_message(self, fmt, *args):
        return


def main():
    server = ThreadingHTTPServer((HOST, PORT), Handler)
    print(f"LocalServer listening on http://{HOST}:{PORT}/api")
    try:
        server.serve_forever()
    finally:
        wsl.close()


if __name__ == "__main__":
    main()
