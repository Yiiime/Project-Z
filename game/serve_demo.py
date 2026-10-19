#!/usr/bin/env python3
"""Serve the exported Godot game locally; no uploads or external dependencies."""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import urllib.request
import webbrowser

root = Path(__file__).resolve().parent / 'build'
if not (root / 'index.html').exists():
    raise SystemExit('缺少 Web 导出。请先在 Godot 中执行 Web 导出。')
url = 'http://127.0.0.1:61327/'
try:
    server = ThreadingHTTPServer(('127.0.0.1', 61327), partial(SimpleHTTPRequestHandler, directory=str(root)))
except OSError:
    try:
        with urllib.request.urlopen(url, timeout=2) as response:
            existing = response.read(20000).decode('utf-8', errors='replace')
        if '来自月亮的 Z' in existing:
            print('试玩服务已在运行：' + url)
            webbrowser.open(url)
            raise SystemExit(0)
    except (OSError, ValueError):
        pass
    server = ThreadingHTTPServer(('127.0.0.1', 0), partial(SimpleHTTPRequestHandler, directory=str(root)))
    url = f'http://127.0.0.1:{server.server_port}/'
print('来自月亮的 Z · Chapter 1\n' + url + '\n保留这个窗口；按 Ctrl+C 停止本地服务。', flush=True)
webbrowser.open(url)
try:
    server.serve_forever()
except KeyboardInterrupt:
    pass
finally:
    server.server_close()
