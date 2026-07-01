from __future__ import annotations

import json
import os
import threading
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from . import fetch_cdn, extract, official_writer, translate
from .install import install_official, write_pack_status
from .pack_status import build_status
from .paths import CACHE_DIR, ROOT

SERVER_VERSION = "0.1.0"
_lock = threading.Lock()
_busy = False


def _json_response(handler: BaseHTTPRequestHandler, code: int, payload: dict) -> None:
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    handler.send_response(code)
    handler.send_header("Content-Type", "application/json; charset=utf-8")
    handler.send_header("Content-Length", str(len(body)))
    handler.end_headers()
    handler.wfile.write(body)


class Handler(BaseHTTPRequestHandler):
    server_version = f"PgTranslateServer/{SERVER_VERSION}"

    def log_message(self, fmt: str, *args) -> None:
        print(f"[serve] {self.address_string()} {fmt % args}")

    def do_GET(self) -> None:
        path = urllib.parse.urlparse(self.path).path
        if path == "/health":
            _json_response(
                self,
                200,
                {
                    "ok": True,
                    "version": SERVER_VERSION,
                    "busy": _busy,
                    "pipelineRoot": str(ROOT),
                },
            )
            return
        if path == "/status":
            _json_response(self, 200, build_status())
            return
        _json_response(self, 404, {"ok": False, "error": "not found"})

    def do_POST(self) -> None:
        global _busy
        path = urllib.parse.urlparse(self.path).path
        query = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)

        if path == "/sync":
            if not _lock.acquire(blocking=False):
                _json_response(self, 409, {"ok": False, "error": "busy"})
                return
            try:
                _busy = True
                official_writer.write_official()
                installed = install_official()
                status = build_status()
                _json_response(
                    self,
                    200,
                    {"ok": True, "action": "sync", "installed": installed, "status": status},
                )
            except Exception as exc:
                _json_response(self, 500, {"ok": False, "error": str(exc)})
            finally:
                _busy = False
                _lock.release()
            return

        if path == "/translate":
            batch = int(query.get("batch", ["200"])[0])
            workers = int(query.get("workers", ["4"])[0])
            delay = float(query.get("delay", ["0.05"])[0])
            categories = query.get("categories", [None])[0]
            cats = [c.strip() for c in categories.split(",")] if categories else None

            if not _lock.acquire(blocking=False):
                _json_response(self, 409, {"ok": False, "error": "busy"})
                return
            try:
                _busy = True
                translate.translate_strings(
                    categories=cats,
                    limit=batch,
                    workers=workers,
                    delay=delay,
                )
                status = build_status()
                _json_response(
                    self,
                    200,
                    {"ok": True, "action": "translate", "batch": batch, "status": status},
                )
            except Exception as exc:
                _json_response(self, 500, {"ok": False, "error": str(exc)})
            finally:
                _busy = False
                _lock.release()
            return

        if path == "/fetch":
            force = query.get("force", ["0"])[0] in ("1", "true", "yes")
            if not _lock.acquire(blocking=False):
                _json_response(self, 409, {"ok": False, "error": "busy"})
                return
            try:
                _busy = True
                version = fetch_cdn.fetch_cdn(force=force)
                extract.extract(version=version)
                status = build_status()
                _json_response(
                    self,
                    200,
                    {"ok": True, "action": "fetch", "version": version, "status": status},
                )
            except Exception as exc:
                _json_response(self, 500, {"ok": False, "error": str(exc)})
            finally:
                _busy = False
                _lock.release()
            return

        _json_response(self, 404, {"ok": False, "error": "not found"})


def write_pid(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(str(os.getpid()), encoding="utf-8")


def run_server(host: str = "127.0.0.1", port: int = 8765) -> None:
    pid_path = CACHE_DIR / "serve.pid"
    write_pid(pid_path)
    from .install import default_plugin_dir

    write_pack_status(default_plugin_dir())

    httpd = ThreadingHTTPServer((host, port), Handler)
    print(f"Pg Translate Server {SERVER_VERSION} em http://{host}:{port}")
    print(f"  GET  /health /status")
    print(f"  POST /sync /translate?batch=200 /fetch")
    try:
        httpd.serve_forever()
    finally:
        pid_path.unlink(missing_ok=True)
