#!/usr/bin/env python3
"""Static file server for the browser build's dev loop.

Exists instead of a plain `python3 -m http.server` for one reason: that server sends no
cache-control headers at all, so the browser is free to cache everything -- and it does. The
painful case is mana_interop.js: JSHost.ImportAsync pulls it in as a dynamically-imported ES
module, and those can survive even a hard refresh, so you end up running a fresh .wasm against a
stale JS module. That shows up as bewildering errors like "installInput must be a Function but was
undefined" -- new C# calling a function the cached JS copy doesn't have yet.

Sending no-store on everything costs nothing locally and makes "did I actually reload my changes?"
a question you never have to ask.

Usage:  python3 serve.py [port]          (defaults to 8000, serves the published AppBundle)
"""

import functools
import http.server
import os
import sys

DEFAULT_PORT = 8000
APP_BUNDLE = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Artifacts", "Bin", "ManaEditor.Web", "Release", "net10.0", "browser-wasm", "AppBundle",
)


class NoCacheHandler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
        self.send_header("Pragma", "no-cache")
        self.send_header("Expires", "0")
        super().end_headers()

    def log_message(self, format, *args):
        # The default logs every request; the wasm build pulls hundreds per load, which buries
        # anything worth seeing. Errors still surface through log_error.
        pass


def main():
    port = int(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_PORT

    if not os.path.isdir(APP_BUNDLE):
        print(f"AppBundle not found at {APP_BUNDLE}")
        print("Publish first:  dotnet publish ManaEditor.Web/ManaEditor.Web.csproj -c Release")
        return 1

    handler = functools.partial(NoCacheHandler, directory=APP_BUNDLE)
    # Threaded: the engine download is dozens of requests at once, and a one-at-a-time server refuses some of them
    # once its queue fills -- the boot then fails with "Failed to fetch" on a framework file.
    http.server.ThreadingHTTPServer.allow_reuse_address = True
    http.server.ThreadingHTTPServer.daemon_threads = True
    with http.server.ThreadingHTTPServer(("", port), handler) as httpd:
        print(f"Serving {APP_BUNDLE}")
        print(f"  http://localhost:{port}/   (no-store: every reload gets fresh files)")
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nstopped")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
