#!/usr/bin/env python3
"""Run the published admin bootstrap without live infrastructure or real credentials."""
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

published = Path(sys.argv[1]).resolve()
with tempfile.TemporaryDirectory(prefix="admin-bootstrap-smoke-") as temporary:
    with socket.socket() as reservation:
        reservation.bind(("127.0.0.1", 0))
        port = reservation.getsockname()[1]
    origin = f"http://127.0.0.1:{port}"
    environment = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development", ASPNETCORE_URLS=origin,
                       Bootstrap__Enabled="true", BootstrapConnection__Authority="https://identity.example",
                       BootstrapConnection__Realm="root", BootstrapConnection__ClientId="provisioner",
                       BootstrapConnection__PublicOrigin=origin,
                       BootstrapConnection__StateDirectory=f"{temporary}/state",
                       Bootstrap__ProtectionKeyDirectory=f"{temporary}/protection",
                       RootCredentials__Directory=f"{temporary}/credentials",
                       RootCredentials__KeyDirectory=f"{temporary}/root-key")
    command = ["dotnet", str(published / "AethericAdmin.Web.dll")]
    subprocess.run(command + ["--initialize-bootstrap"], cwd=published, env=environment, check=True,
                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
    with open(f"{temporary}/host.log", "w+") as output:
        process = subprocess.Popen(command, cwd=published, env=environment, stdout=output, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 30
            while True:
                try:
                    with urllib.request.urlopen(origin + "/setup", timeout=2) as response:
                        assert response.status == 200
                        assert b"A home for your platform" in response.read()
                    break
                except urllib.error.URLError:
                    if process.poll() is not None or time.monotonic() >= deadline:
                        output.flush(); output.seek(0)
                        raise RuntimeError("Bootstrap did not start:\n" + output.read())
                    time.sleep(0.2)
            for asset in ["app.css", "setup-credentials.js", "infrastructure-setup.js"]:
                with urllib.request.urlopen(origin + "/_content/Aetheric.Provisioning.Components/" + asset, timeout=5) as response:
                    assert response.status == 200 and response.read(), asset
            with urllib.request.urlopen(origin + "/", timeout=5) as response:
                assert response.url.endswith("/setup")
            with urllib.request.urlopen(origin + "/setup/infrastructure", timeout=5) as response:
                assert "/setup?" in response.url, "Anonymous request reached credential setup"
            try:
                urllib.request.urlopen(origin + "/maintenance", timeout=5)
                raise AssertionError("Operational route exposed in bootstrap mode")
            except urllib.error.HTTPError as error:
                assert error.code == 404
            print("Published bootstrap starts without services; setup, protected routes and all library assets verified.")
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill(); process.wait(timeout=5)
