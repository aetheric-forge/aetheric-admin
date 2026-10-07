#!/usr/bin/env python3
"""Check automatic startup and University routing without live infrastructure."""
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        return None

published = Path(sys.argv[1]).resolve()
with tempfile.TemporaryDirectory(prefix="admin-auto-setup-") as temporary:
    with socket.socket() as reservation:
        reservation.bind(("127.0.0.1", 0))
        port = reservation.getsockname()[1]
    origin = f"http://127.0.0.1:{port}"
    environment = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development", ASPNETCORE_URLS=origin,
        Bootstrap__Enabled="false", Bootstrap__AutoSetup="true",
        Keycloak__Authority="https://identity.example/realms/forge", Keycloak__ClientId="provisioner",
        Admin__PublicOrigin=origin, BootstrapConnection__StateDirectory=f"{temporary}/state",
        Bootstrap__ProtectionKeyDirectory=f"{temporary}/protection",
        RootCredentials__Directory=f"{temporary}/credentials", RootCredentials__KeyDirectory=f"{temporary}/key")
    snapshot = None
    for attempt in range(2):
        with open(f"{temporary}/host.log", "w+") as output:
            process = subprocess.Popen(["dotnet", str(published / "AethericAdmin.Web.dll")],
                cwd=published, env=environment, stdout=output, stderr=subprocess.STDOUT)
            try:
                deadline = time.monotonic() + 30
                while True:
                    try:
                        with urllib.request.urlopen(origin + "/setup", timeout=2) as response:
                            assert b"A home for your platform" in response.read()
                        break
                    except urllib.error.URLError:
                        if process.poll() is not None or time.monotonic() >= deadline:
                            output.flush(); output.seek(0)
                            raise RuntimeError("Automatic setup did not start:\n" + output.read())
                        time.sleep(.2)
                try:
                    urllib.request.build_opener(NoRedirect()).open(origin + "/university", timeout=5)
                    raise AssertionError("University bypassed automatic setup")
                except urllib.error.HTTPError as error:
                    assert error.code == 302 and error.headers["Location"] == "/setup"
                with urllib.request.urlopen(origin + "/setup/readiness", timeout=5) as response:
                    assert b'"restarting":false' in response.read()
                with urllib.request.urlopen(origin + "/setup/handoff.js", timeout=5) as response:
                    assert b"/university" in response.read()
                current = {p.name: p.read_bytes() for p in Path(temporary, "state").glob("*.json")}
                assert current, "Bootstrap state was not initialized"
                if snapshot is not None: assert current == snapshot, "Restart changed existing setup state"
                snapshot = current
            finally:
                process.terminate()
                try: process.wait(timeout=10)
                except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=5)
    print("Automatic setup redirects University, starts without infrastructure, and preserves state across restarts.")
