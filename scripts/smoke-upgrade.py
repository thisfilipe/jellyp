#!/usr/bin/env python3
"""Exercise a real baseline-to-current JellyP upgrade ONLY on a marked disposable fixture.

The harness manages only the exact Jellyfin process it started under --root. It
uses http://127.0.0.1:18096, refuses unmarked/symlinked roots, checks the
effective listening socket and exact process ownership before the wizard, then
verifies the authenticated ProgramDataPath after login before media, library or
plugin mutations, and never prints credentials or tokens.

Typical use after extracting the official Jellyfin 12.1 server/web packages into
<root>/host and creating <root>/.jellyp-smoke-root:

  python3 scripts/smoke-upgrade.py --root /tmp/opencode/jellyp-upgrade \\
      --phase all --candidate /tmp/opencode/jellyp-alpha-check/upgrade-candidate/JellyP_0.3.0.1.zip \\
      --expected-dll-sha256 <candidate-dll-sha256>

Phases may also be run separately: bootstrap, seed, upgrade, verify, stop. The
fixture process is left running after bootstrap/seed/upgrade/verify/all so the
native UI smoke tests can use it. Use --phase stop to terminate that exact PID.

Before the wizard, only the exact process command/cwd and the public loopback
socket can be checked; ProgramDataPath is authenticated and is verified only
after login, before media/library creation and plugin mutations. Seed creates a
legacy configuration with one exact item, real interval/daily task triggers and
a real journal/backup produced through the baseline API. Verify re-reads the
upgraded fixture, compares the XML/journal/backup byte-for-byte, checks that
scope/triggers/safe mode did not change, proves the loaded assembly/page belong
to the expected candidate, and restores the old journal through the current API
with byte-exact original artwork. Verification is not reentrant: a consumed
backup or existing result marker refuses a second restore.
"""
import argparse
import base64
import hashlib
import json
import os
import re
import secrets
import shutil
import signal
import socket
import struct
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
import zlib
import xml.etree.ElementTree as ET
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
BASE = "http://127.0.0.1:18096"
PORT = 18096
GUID = "0928e334-8971-4205-a455-f42a76527f9e"
ADMIN = "jellyp-test-admin"
CANARY = "JellyP Canary (2026)"
DLL_NAME = "Jellyfin.Plugin.JellyP.dll"
SCHEMA = 2
AUTH = 'MediaBrowser Client="JellyP upgrade harness", Device="disposable fixture", DeviceId="jellyp-upgrade-harness", Version="0.1"'
NETWORK_XML = """<?xml version="1.0" encoding="utf-8"?>
<NetworkConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <BaseUrl />
  <EnableHttps>false</EnableHttps>
  <RequireHttps>false</RequireHttps>
  <CertificatePath />
  <CertificatePassword />
  <InternalHttpPort>18096</InternalHttpPort>
  <InternalHttpsPort>8920</InternalHttpsPort>
  <PublicHttpPort>18096</PublicHttpPort>
  <PublicHttpsPort>8920</PublicHttpsPort>
  <AutoDiscovery>false</AutoDiscovery>
  <EnableUPnP>false</EnableUPnP>
  <EnableIPv4>true</EnableIPv4>
  <EnableIPv6>false</EnableIPv6>
  <EnableRemoteAccess>false</EnableRemoteAccess>
  <LocalNetworkSubnets />
  <LocalNetworkAddresses>
    <string>127.0.0.1</string>
  </LocalNetworkAddresses>
  <KnownProxies />
  <IgnoreVirtualInterfaces>true</IgnoreVirtualInterfaces>
  <VirtualInterfaceNames>
    <string>veth</string>
  </VirtualInterfaceNames>
  <EnablePublishedServerUriByRequest>false</EnablePublishedServerUriByRequest>
  <PublishedServerUriBySubnet />
  <RemoteIPFilter />
  <IsRemoteIPFilterBlacklist>false</IsRemoteIPFilterBlacklist>
</NetworkConfiguration>
"""


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest().upper()


def normalized(value):
    return str(value).replace("-", "").lower()


def utc_now():
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def is_sha256(value):
    return bool(value and re.fullmatch(r"[0-9A-Fa-f]{64}", value))


def guard_root(root):
    """Refuse anything that is not a real marked disposable fixture root."""
    root = Path(root)
    if root.is_symlink():
        raise SystemExit("Refusing a symlinked fixture root.")
    if not root.is_dir():
        raise SystemExit(f"Fixture root is not a directory: {root}")
    resolved = root.resolve()
    if resolved == Path("/"):
        raise SystemExit("Refusing the filesystem root as a fixture.")
    marker = resolved / ".jellyp-smoke-root"
    if marker.is_symlink() or not marker.is_file():
        raise SystemExit("Refusing unmarked disposable root.")
    return resolved


def safe_path(root, path, base=None, must_exist=False):
    """Return a path under base without symlink components or escapes."""
    root = Path(root).resolve()
    base = root if base is None else Path(base)
    if not base.is_absolute():
        base = root / base
    if base.is_symlink():
        raise SystemExit(f"Refusing symlinked fixture path: {base}")
    base = base.resolve()
    if base != root and root not in base.parents:
        raise SystemExit(f"Fixture path escapes the marked root: {base}")
    candidate = Path(path)
    if not candidate.is_absolute():
        candidate = base / candidate
    candidate = Path(os.path.normpath(str(candidate)))
    try:
        relative = candidate.relative_to(base)
    except ValueError:
        raise SystemExit(f"Fixture path escapes its base: {candidate}") from None
    current = base
    for part in relative.parts:
        current = current / part
        if current.is_symlink():
            raise SystemExit(f"Refusing symlinked fixture path component: {current}")
    resolved = candidate.resolve()
    if resolved != base and base not in resolved.parents:
        raise SystemExit(f"Fixture path resolves outside its base: {candidate}")
    if must_exist and not resolved.exists():
        raise SystemExit(f"Required fixture path does not exist: {candidate}")
    return candidate


def data_path(root, path, must_exist=True):
    return safe_path(root, path, base=Path(root) / "data", must_exist=must_exist)


def write_private(path, text):
    """Create/truncate a private file with mode 0600 at creation, never through a symlink."""
    path = Path(path)
    flags = os.O_WRONLY | os.O_CREAT | os.O_TRUNC | getattr(os, "O_NOFOLLOW", 0)
    try:
        descriptor = os.open(path, flags, 0o600)
    except OSError as error:
        raise SystemExit(f"Refusing to write private file {path}: {error}") from None
    try:
        os.fchmod(descriptor, 0o600)
        with os.fdopen(descriptor, "wb") as handle:
            descriptor = -1
            handle.write(text.encode("utf-8"))
    finally:
        if descriptor >= 0:
            os.close(descriptor)


def validate_network_config(text):
    try:
        root = ET.fromstring(text)
    except ET.ParseError as error:
        raise SystemExit(f"Invalid network.xml fixture: {error}") from None

    def value(tag):
        element = root.find(tag)
        return (element.text or "").strip() if element is not None else ""

    if value("InternalHttpPort") != str(PORT) or value("PublicHttpPort") != str(PORT):
        raise SystemExit("Fixture network.xml must use port 18096.")
    for tag in ("EnableIPv6", "EnableRemoteAccess", "EnableUPnP"):
        if value(tag).lower() != "false":
            raise SystemExit(f"Fixture network.xml must disable {tag}.")
    addresses = [(element.text or "").strip() for element in root.findall("LocalNetworkAddresses/string")]
    if addresses != ["127.0.0.1"]:
        raise SystemExit("Fixture network.xml must bind only 127.0.0.1.")
    if root.findall("LocalNetworkSubnets/string"):
        raise SystemExit("Fixture network.xml must not declare extra local subnets.")
    return True


def request(path, body=None, method=None, token=None, raw=None, content_type=None, timeout=120):
    headers = {"Authorization": AUTH + (', Token="' + token + '"' if token else "")}
    if content_type:
        headers["Content-Type"] = content_type
    elif body is not None:
        headers["Content-Type"] = "application/json"
    data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
    req = urllib.request.Request(BASE + "/" + path.lstrip("/"), data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as response:
            payload = response.read()
            content = response.headers.get("Content-Type", "")
            return response.status, (json.loads(payload) if payload and "json" in content else payload)
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", "replace")[:400]
        raise RuntimeError(f"{path}: HTTP {error.code} {detail}") from None


def wait_for(callback, label, timeout=120, interval=0.2):
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        try:
            value = callback()
            if value:
                return value
        except Exception as error:  # Readiness polling; the final error is reported.
            last = error
        time.sleep(interval)
    raise RuntimeError(f"Timed out waiting for {label}" + (f": {last}" if last else ""))


def port_open():
    try:
        with socket.create_connection(("127.0.0.1", PORT), timeout=1):
            return True
    except OSError:
        return False


def parse_proc_net_tcp(text, port=PORT):
    """Return (address_hex, inode) for LISTEN sockets on port from /proc/net/tcp*."""
    listeners = []
    for line in text.splitlines()[1:]:
        fields = line.split()
        if len(fields) < 10 or fields[3] != "0A":
            continue
        try:
            address, port_hex = fields[1].rsplit(":", 1)
            if int(port_hex, 16) != port:
                continue
        except ValueError:
            continue
        listeners.append((address.upper(), fields[9]))
    return listeners


def listening_bindings(port=PORT, proc_root=Path("/proc")):
    bindings = []
    for name, family in (("tcp", "tcp"), ("tcp6", "tcp6")):
        path = Path(proc_root) / "net" / name
        if path.is_file():
            bindings.extend((family, address, inode) for address, inode in parse_proc_net_tcp(path.read_text(), port))
    return bindings


def socket_inodes(pid, proc_root=Path("/proc")):
    fd_dir = Path(proc_root) / str(pid) / "fd"
    if not fd_dir.is_dir():
        raise SystemExit(f"Cannot inspect sockets for fixture PID {pid}.")
    inodes = set()
    for entry in fd_dir.iterdir():
        try:
            target = os.readlink(entry)
        except OSError:
            continue
        match = re.fullmatch(r"socket:\[(\d+)\]", target)
        if match:
            inodes.add(match.group(1))
    return inodes


def assert_loopback_bind(root, proc_root=Path("/proc")):
    pid = read_host_pid(root)
    bindings = listening_bindings(PORT, proc_root)
    if len(bindings) != 1:
        raise SystemExit(f"Expected exactly one fixture listener on port {PORT}, found {len(bindings)}.")
    family, address, inode = bindings[0]
    if family != "tcp" or address != "0100007F":
        raise SystemExit(f"Fixture listener is not loopback IPv4 only: {family} {address}.")
    if inode not in socket_inodes(pid, proc_root):
        raise SystemExit("Fixture listener belongs to another process.")
    return pid


def process_alive(pid):
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    try:
        return Path(f"/proc/{pid}/stat").read_text().split()[2] != "Z"
    except FileNotFoundError:
        return False


def fixture_argv(root):
    return [
        str(root / "host/usr/lib/jellyfin/bin/jellyfin"),
        "--datadir", str(root / "data"),
        "--configdir", str(root / "config"),
        "--cachedir", str(root / "cache"),
        "--logdir", str(root / "log"),
        "--webdir", str(root / "host/usr/share/jellyfin/web"),
        "--ffmpeg", "/usr/bin/ffmpeg",
    ]


def fixture_process_args(root, args):
    return list(args) == fixture_argv(root)


def fixture_process(root, pid):
    try:
        parts = Path(f"/proc/{pid}/cmdline").read_bytes().split(b"\0")
    except FileNotFoundError:
        return False
    args = [part.decode("utf-8", "replace") for part in parts if part]
    return fixture_process_args(root, args)


def read_host_pid(root):
    pid_file = safe_path(root, "host.pid", base=root, must_exist=True)
    try:
        return int(pid_file.read_text().strip())
    except ValueError:
        raise SystemExit("host.pid is not a PID; refusing to guess the fixture process.") from None


def assert_owned_process(root):
    root = guard_root(root)
    pid = read_host_pid(root)
    if not process_alive(pid):
        raise SystemExit("Fixture PID is not alive.")
    if not fixture_process(root, pid):
        raise SystemExit("Fixture PID does not match the exact command for this root.")
    try:
        cwd = os.readlink(f"/proc/{pid}/cwd")
    except OSError:
        raise SystemExit("Cannot inspect the fixture process working directory.") from None
    if Path(cwd).resolve() != root:
        raise SystemExit("Fixture process working directory does not match the marked root.")
    return pid


def host_running(root):
    root = guard_root(root)
    pid_file = safe_path(root, "host.pid", base=root)
    if not pid_file.is_file():
        return False
    pid = read_host_pid(root)
    if not process_alive(pid):
        return False
    if not fixture_process(root, pid):
        raise SystemExit("host.pid does not identify this fixture process; refusing to stop it.")
    return True


def wait_ready():
    def public():
        _, info = request("System/Info/Public", timeout=5)
        local = info.get("LocalAddress", "")
        return info if info.get("Version") == "12.1.0" and local.startswith("http://127.0.0.1:18096") else None
    return wait_for(public, "Jellyfin 12.1 loopback readiness", timeout=120)


def start_host(root):
    root = guard_root(root)
    if host_running(root):
        assert_owned_process(root)
        assert_loopback_bind(root)
        return
    if port_open():
        raise SystemExit("127.0.0.1:18096 is already in use by a process this harness did not start; refusing to interfere.")
    binary = root / "host/usr/lib/jellyfin/bin/jellyfin"
    if not binary.is_file():
        raise SystemExit(f"Missing extracted host binary: {binary}")
    log_path = safe_path(root, "host.out", base=root)
    log = open(log_path, "ab")
    process = subprocess.Popen(
        fixture_argv(root),
        cwd=str(root),
        stdout=log,
        stderr=subprocess.STDOUT,
        start_new_session=True,
    )
    try:
        pid_file = safe_path(root, "host.pid", base=root)
        pid_file.write_text(str(process.pid))
        wait_ready()
        assert_owned_process(root)
        assert_loopback_bind(root)
        if process.poll() is not None:
            raise SystemExit(f"Jellyfin exited during startup; inspect {log_path}")
    except BaseException:
        # Clean up only the child we just launched, including SystemExit guards
        # and PID-file failures. Do not rely on guards that may be the failure.
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
        pid_path = root / "host.pid"
        if not pid_path.is_symlink() and pid_path.is_file() and pid_path.read_text().strip() == str(process.pid):
            pid_path.unlink()
        raise
    finally:
        log.close()


def stop_host(root):
    root = guard_root(root)
    if not host_running(root):
        if port_open():
            raise SystemExit("Port 18096 is open but no managed fixture PID exists; refusing to stop an unknown process.")
        safe_path(root, "host.pid", base=root).unlink(missing_ok=True)
        return
    pid = assert_owned_process(root)
    os.kill(pid, signal.SIGTERM)
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline and process_alive(pid):
        time.sleep(0.1)
    if process_alive(pid):
        os.kill(pid, signal.SIGKILL)
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline and process_alive(pid):
            time.sleep(0.1)
    if process_alive(pid):
        raise SystemExit("Fixture process did not stop after SIGTERM/SIGKILL.")
    wait_for(lambda: not port_open(), "fixture port shutdown", timeout=20)
    safe_path(root, "host.pid", base=root).unlink(missing_ok=True)


def load_auth(root):
    path = safe_path(root, "fixture-auth.json", base=root, must_exist=True)
    data = json.loads(path.read_text())
    if not data.get("token") or not data.get("userId"):
        raise SystemExit("fixture-auth.json must contain token and userId.")
    return data


def verify_program_data(root, token):
    _, info = request("System/Info", token=token)
    if Path(info["ProgramDataPath"]).resolve() != (root / "data").resolve():
        raise SystemExit("ProgramDataPath does not match the marked disposable root.")
    return info


def plugin(token):
    _, plugins = request("Plugins", token=token)
    found = next((item for item in plugins if normalized(item["Id"]) == normalized(GUID)), None)
    if not found:
        raise SystemExit("JellyP plugin is not installed in the fixture.")
    return found


def wait_plugin(token, version):
    def active():
        item = plugin(token)
        return item if item["Status"] == "Active" and item["Version"] == version else None
    return wait_for(active, f"JellyP {version} active", timeout=120)


def complete_wizard(root):
    root = guard_root(root)
    _, public = request("System/Info/Public")
    password_file = safe_path(root, "test-password", base=root)
    if public.get("StartupWizardCompleted"):
        if not password_file.is_file():
            raise SystemExit("Wizard already completed but test-password is absent; use a fresh root.")
        return
    password = secrets.token_urlsafe(32)
    write_private(password_file, password)
    request("Startup/User")
    request("Startup/Configuration", {"UICulture": "en-US", "MetadataCountryCode": "US", "PreferredMetadataLanguage": "en"})
    request("Startup/User", {"Name": ADMIN, "Password": password})
    request("Startup/RemoteAccess", {"EnableRemoteAccess": False, "EnableAutomaticPortMapping": False})
    request("Startup/Complete", {})
    wait_for(lambda: request("System/Info/Public")[1].get("StartupWizardCompleted"), "startup wizard completion")


def login(root):
    root = guard_root(root)
    password = safe_path(root, "test-password", base=root, must_exist=True).read_text()
    _, result = request("Users/AuthenticateByName", {"Username": ADMIN, "Pw": password})
    token = result["AccessToken"]
    _, users = request("Users", token=token)
    admin = next((user for user in users if user["Name"] == ADMIN), None)
    if not admin:
        raise SystemExit("Disposable administrator was not created.")
    write_private(safe_path(root, "fixture-auth.json", base=root), json.dumps({"token": token, "userId": admin["Id"]}, indent=2) + "\n")
    return token


def generate_media(root):
    root = guard_root(root)
    target = safe_path(root, Path("media") / CANARY / f"{CANARY}.mp4", base=root)
    if target.is_file():
        return
    target.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        [
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc=size=320x240:rate=24",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
            "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac",
            "-movflags", "+faststart", str(target),
        ],
        check=True,
    )


def png(seed):
    state = seed + 1
    rows = bytearray(512 * (512 * 4 + 1))
    for y in range(512):
        for x in range(512):
            index = y * 2049 + 1 + x * 4
            state = (1664525 * state + 1013904223) & 0xFFFFFFFF
            rows[index] = x >> 1
            rows[index + 1] = y >> 1
            rows[index + 2] = (90 + seed * 3 + (state % 5)) & 0xFF
            rows[index + 3] = 255

    def crc(buffer):
        value = 0xFFFFFFFF
        for byte in buffer:
            value ^= byte
            for _ in range(8):
                value = (value >> 1) ^ (0xEDB88320 if value & 1 else 0)
        return (value ^ 0xFFFFFFFF) & 0xFFFFFFFF

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", crc(kind + data))

    header = struct.pack(">IIBBBBB", 512, 512, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(bytes(rows), 9)) + chunk(b"IEND", b"")


def canary(token):
    def find():
        _, result = request("Items?Recursive=true&IncludeItemTypes=Movie&Fields=Path", token=token)
        return next((item for item in result.get("Items", []) if item.get("Name") == CANARY), None)
    return wait_for(find, "generated canary movie", timeout=120)


def ensure_canary_image(root, token, movie):
    root = guard_root(root)
    synthetic = png(0)
    synthetic_hash = hashlib.sha256(synthetic).hexdigest().upper()
    _, images = request("Items/" + movie["Id"] + "/Images", token=token)
    primary = next((image for image in images if image.get("ImageType") == "Primary"), None)
    if primary:
        existing = data_path(root, primary["Path"])
        if sha256(existing) != synthetic_hash:
            raise SystemExit("Existing Primary is not the harness synthetic PNG; refusing to overwrite or claim it.")
        return images, synthetic_hash
    folder = safe_path(root, "native-png", base=root)
    folder.mkdir(parents=True, exist_ok=True)
    slot = safe_path(root, "native-png/slot-0.png", base=root)
    slot.write_bytes(synthetic)
    request(
        "Items/" + movie["Id"] + "/Images/Primary",
        token=token,
        raw=base64.b64encode(synthetic),
        method="POST",
        content_type="image/png",
    )
    _, images = request("Items/" + movie["Id"] + "/Images", token=token)
    primary = next((image for image in images if image.get("ImageType") == "Primary"), None)
    if not primary:
        raise SystemExit("Synthetic Primary image was not registered by Jellyfin.")
    uploaded = data_path(root, primary["Path"])
    if sha256(uploaded) != synthetic_hash:
        raise SystemExit("Jellyfin changed the uploaded synthetic PNG; refusing to claim exact source bytes.")
    return images, synthetic_hash


def configure_library(root, token):
    root = guard_root(root)
    _, folders = request("Library/VirtualFolders", token=token)
    if any(folder.get("Name") == "JellyP fixture" for folder in folders):
        return
    options = {
        "Enabled": True,
        "EnablePhotos": False,
        "EnableRealtimeMonitor": False,
        "EnableInternetProviders": False,
        "EnableLUFSScan": False,
        "EnableChapterImageExtraction": False,
        "ExtractChapterImagesDuringLibraryScan": False,
        "EnableTrickplayImageExtraction": False,
        "ExtractTrickplayImagesDuringLibraryScan": False,
        "SaveLocalMetadata": False,
        "AutomaticRefreshIntervalDays": 0,
        "PreferredMetadataLanguage": "en",
        "MetadataCountryCode": "US",
        "TypeOptions": [{"Type": "Movie", "MetadataFetchers": [], "ImageFetchers": [], "MetadataSavers": [], "ImageOptions": []}],
        "PathInfos": [],
    }
    query = urllib.parse.urlencode(
        {"name": "JellyP fixture", "collectionType": "movies", "paths": str(root / "media"), "refreshLibrary": "false"}
    )
    request("Library/VirtualFolders?" + query, {"LibraryOptions": options}, method="POST", token=token)
    request("Library/Refresh", method="POST", token=token)


def read_plugin_archive(archive, expected_version):
    archive = Path(archive).resolve()
    if not archive.is_file():
        raise SystemExit(f"Missing plugin archive: {archive}")
    with zipfile.ZipFile(archive) as package:
        names = package.namelist()
        if len(names) != len(set(names)):
            raise SystemExit("Plugin archive contains duplicate member names.")
        if any(not name or Path(name).name != name for name in names):
            raise SystemExit("Plugin archive contains directories, traversal or unsafe paths.")
        if not {"meta.json", "LICENSE", DLL_NAME}.issubset(names):
            raise SystemExit("Plugin archive lacks required meta.json, LICENSE or JellyP assembly.")
        meta = json.loads(package.read("meta.json"))
        if normalized(meta.get("guid", "")) != normalized(GUID):
            raise SystemExit("Plugin archive GUID does not match JellyP.")
        if meta.get("version") != expected_version:
            raise SystemExit(f"Plugin archive version {meta.get('version')} != expected {expected_version}.")
        allowed = {"meta.json", "LICENSE", DLL_NAME}
        image = meta.get("imagePath")
        if image:
            if Path(image).name != image:
                raise SystemExit("Plugin archive imagePath is not a flat filename.")
            allowed.add(image)
        if set(names) != allowed:
            raise SystemExit(f"Plugin archive contains unexpected members: {sorted(set(names) - allowed)}")
        dll_hash = hashlib.sha256(package.read(DLL_NAME)).hexdigest().upper()
    return meta, dll_hash


def install_plugin(root, archive, expected_version):
    root = guard_root(root)
    meta, dll_hash = read_plugin_archive(archive, expected_version)
    folder = safe_path(root, "data/plugins/JellyP", base=root)
    if folder.exists() and not folder.is_dir():
        raise SystemExit(f"Plugin target is not a directory: {folder}")
    shutil.rmtree(folder, ignore_errors=True)
    folder.mkdir(parents=True)
    with zipfile.ZipFile(Path(archive).resolve()) as package:
        package.extractall(folder)
    installed = safe_path(root, "data/plugins/JellyP/" + DLL_NAME, base=root, must_exist=True)
    if sha256(installed) != dll_hash:
        raise SystemExit("Installed plugin DLL hash differs from the archive.")
    print(f"installed JellyP {expected_version} from {Path(archive).name} ({dll_hash})")
    return dll_hash


def assert_single_plugin_install(root):
    root = guard_root(root)
    plugins = safe_path(root, "data/plugins", base=root, must_exist=True)
    if plugins.is_symlink():
        raise SystemExit("Plugin directory is a symlink.")
    folders = []
    for child in plugins.iterdir():
        if child.is_symlink():
            raise SystemExit(f"Plugin path is a symlink: {child}")
        meta_path = child / "meta.json"
        if meta_path.is_file():
            meta = json.loads(meta_path.read_text())
            if normalized(meta.get("guid", "")) == normalized(GUID):
                folders.append(child)
    expected_folder = (plugins / "JellyP").resolve()
    if len(folders) != 1 or folders[0].resolve() != expected_folder:
        raise SystemExit(f"Expected exactly one JellyP plugin folder at {expected_folder}.")
    dlls = []
    for directory, dirnames, filenames in os.walk(plugins, followlinks=False):
        dirnames[:] = [name for name in dirnames if not (Path(directory) / name).is_symlink()]
        for name in filenames:
            path = Path(directory) / name
            if name == DLL_NAME:
                if path.is_symlink():
                    raise SystemExit(f"Plugin DLL is a symlink: {path}")
                dlls.append(path)
    expected_dll = (expected_folder / DLL_NAME).resolve()
    if len(dlls) != 1 or dlls[0].resolve() != expected_dll:
        raise SystemExit(f"Expected exactly one JellyP DLL at {expected_dll}.")
    return expected_dll


def assert_loaded_assembly(root, expected_path):
    pid = read_host_pid(root)
    expected = Path(expected_path).resolve()
    loaded = set()
    for line in Path(f"/proc/{pid}/maps").read_text().splitlines():
        fields = line.split()
        if len(fields) >= 6 and fields[-1].endswith("/" + DLL_NAME):
            loaded.add(Path(fields[-1]).resolve())
    if len(loaded) != 1 or next(iter(loaded)) != expected:
        raise SystemExit(f"Loaded JellyP assembly paths do not exclusively match {expected}: {sorted(map(str, loaded))}")
    return expected


def assert_candidate_page(root, token):
    source = (REPO / "Jellyfin.Plugin.JellyP/Web/config.html").read_bytes()
    status, served = request("web/ConfigurationPage?name=jellyp", token=token)
    if status != 200 or served != source:
        raise SystemExit("Installed candidate does not serve the exact current embedded page.")
    if b'id="JpNav"' not in served or b'id="JpMain"' not in served:
        raise SystemExit("Installed page lacks the candidate-only navigation probe.")
    return True


def assert_candidate_identity(archive, expected_version, expected_dll_sha256, baseline_dll_sha256):
    if not is_sha256(expected_dll_sha256):
        raise SystemExit("A 64-hex expected candidate DLL SHA256 is required.")
    meta, dll_hash = read_plugin_archive(archive, expected_version)
    if dll_hash != expected_dll_sha256.upper():
        raise SystemExit(f"Candidate DLL SHA256 {dll_hash} != expected {expected_dll_sha256.upper()}.")
    if baseline_dll_sha256 and dll_hash == baseline_dll_sha256.upper():
        raise SystemExit("Candidate DLL is the baseline; refusing a no-op upgrade.")
    return meta, dll_hash


def load_snapshot(root):
    path = safe_path(root, "upgrade-snapshot.json", base=root, must_exist=True)
    snapshot = json.loads(path.read_text())
    if snapshot.get("schema") != SCHEMA:
        raise SystemExit(f"Unsupported upgrade snapshot schema {snapshot.get('schema')}; re-seed with this harness.")
    if Path(snapshot.get("root", "")).resolve() != root.resolve():
        raise SystemExit("Upgrade snapshot belongs to another root.")
    if not is_sha256(snapshot.get("baselineDllSha256", "")):
        raise SystemExit("Upgrade snapshot lacks the baseline DLL hash; re-seed with this harness.")
    return snapshot


def bootstrap(root, baseline, baseline_version, baseline_sha256):
    root = guard_root(root)
    if not Path(baseline).is_file():
        raise SystemExit(f"Missing baseline archive: {baseline}")
    if baseline_sha256 and sha256(baseline) != baseline_sha256.upper():
        raise SystemExit("Baseline archive SHA256 does not match the verified legacy artifact.")
    if (root / "data/data/jellyfin.db").exists():
        raise SystemExit("Bootstrap requires a fresh root; the Jellyfin database already exists.")
    validate_network_config(NETWORK_XML)
    for name in ("data", "config", "cache", "log", "media"):
        safe_path(root, name, base=root).mkdir(parents=True, exist_ok=True)
    network_path = safe_path(root, "config/network.xml", base=root)
    network_path.write_text(NETWORK_XML)
    if port_open():
        raise SystemExit("127.0.0.1:18096 is already in use; stop the previous fixture first.")
    start_host(root)
    assert_owned_process(root)
    assert_loopback_bind(root)
    complete_wizard(root)
    token = login(root)
    info = verify_program_data(root, token)
    if info.get("Version") != "12.1.0":
        raise SystemExit("Fixture host is not Jellyfin 12.1.")
    generate_media(root)
    configure_library(root, token)
    movie = canary(token)
    ensure_canary_image(root, token, movie)
    print(f"bootstrap: Jellyfin 12.1 wizard, synthetic library and canary ready ({movie['Id']})")
    stop_host(root)
    install_plugin(root, baseline, baseline_version)
    start_host(root)
    assert_owned_process(root)
    assert_loopback_bind(root)
    token = login(root)
    wait_plugin(token, baseline_version)
    baseline_dll = assert_single_plugin_install(root)
    assert_loaded_assembly(root, baseline_dll)
    print(f"bootstrap: baseline JellyP {baseline_version} active; fixture left running")


def legacy_configuration(token, movie_id):
    _, config = request("Plugins/" + GUID + "/Configuration", token=token)
    config.update(
        {
            "Enabled": False,
            "DryRun": True,
            "MaintenanceWindowConfirmed": False,
            "PreserveOriginals": True,
            "Quality": 80,
            "Lossless": False,
            "ResizeImages": False,
            "MaximumDimension": 2400,
            "MinimumSavingsRatio": 0.01,
            "MinimumSavingsBytes": 1024,
            "MinimumSsim": 0.90,
            "BatchSize": 10,
            "MinimumFreeSpaceBytes": 64 * 1024 * 1024,
            "MaximumBackupBytes": 512 * 1024 * 1024,
            "BackupRetentionDays": 0,
            "SyncUnchangedPolicies": False,
            "IncludedItemIds": [movie_id],
            "ScopeMode": "items",
            "IncludedLibraryIds": [],
        }
    )
    for policy in config["ImagePolicies"]:
        policy.update(
            {
                "Enabled": policy["Category"] in ("Primary", "Thumb"),
                "OverrideGlobal": False,
                "Quality": 80,
                "Lossless": False,
                "ResizeImages": False,
                "MaximumDimension": 2400,
                "MinimumSsim": 0.90,
            }
        )
    return config


def configure_legacy(token, movie):
    config = legacy_configuration(token, movie["Id"])
    request("Plugins/" + GUID + "/Configuration", config, method="POST", token=token)
    _, saved = request("Plugins/" + GUID + "/Configuration", token=token)
    if normalized(saved["IncludedItemIds"][0]) != normalized(movie["Id"]) or saved["ScopeMode"] != "items":
        raise SystemExit("Legacy configuration was not saved with the exact item scope.")
    _, tasks = request("ScheduledTasks", token=token)
    jellyp = {task["Key"]: task for task in tasks if task.get("Category") == "JellyP"}
    if set(jellyp) != {"JellyPOptimizeArtwork", "JellyPSynchronizePolicies"}:
        raise SystemExit("Expected both JellyP native tasks.")
    triggers = {
        jellyp["JellyPOptimizeArtwork"]["Id"]: [{"Type": "IntervalTrigger", "IntervalTicks": 864000000000}],
        jellyp["JellyPSynchronizePolicies"]["Id"]: [{"Type": "DailyTrigger", "TimeOfDayTicks": 108000000000}],
    }
    for task_id, values in triggers.items():
        request("ScheduledTasks/" + task_id + "/Triggers", values, method="POST", token=token)
    _, tasks = request("ScheduledTasks", token=token)
    current = {task["Key"]: task for task in tasks if task.get("Category") == "JellyP"}
    if current["JellyPOptimizeArtwork"]["Triggers"] != triggers[jellyp["JellyPOptimizeArtwork"]["Id"]]:
        raise SystemExit("Optimize trigger was not saved.")
    if current["JellyPSynchronizePolicies"]["Triggers"] != triggers[jellyp["JellyPSynchronizePolicies"]["Id"]]:
        raise SystemExit("Synchronize trigger was not saved.")
    return saved, current


def run_baseline_apply(token):
    request("JellyP/Run?dryRun=false", method="POST", token=token)

    def terminal():
        _, status = request("JellyP/Status", token=token)
        report = status.get("LastRun", {})
        if report.get("Running") or report.get("Phase") in ("Queued", "Preparing", "Processing", "WaitingForMetadata"):
            return None
        return status

    status = wait_for(terminal, "baseline apply completion", timeout=180)
    report = status["LastRun"]
    if report.get("Failed") or not report.get("Converted"):
        raise SystemExit(f"Baseline apply did not convert exactly the synthetic image: {report}")
    return status


def snapshot_files(root, movie_id, synthetic_hash):
    journal_dir = safe_path(root, "data/plugins/Jellyfin.Plugin.JellyP/migration/journal", base=root, must_exist=True)
    originals = safe_path(root, "data/plugins/Jellyfin.Plugin.JellyP/migration/originals", base=root, must_exist=True)
    records = []
    for path in sorted(journal_dir.glob("*.json")):
        safe_path(root, path.relative_to(root), base=root, must_exist=True)
        record = json.loads(path.read_text())
        if record.get("State") == "Committed" and normalized(record.get("Source", {}).get("ItemId")) == normalized(movie_id):
            records.append((path, record))
    if len(records) != 1:
        raise SystemExit(f"Seed requires exactly one committed canary journal, found {len(records)}.")
    journal_path, journal = records[0]
    record_id = journal["Id"]
    original = safe_path(root, "data/plugins/Jellyfin.Plugin.JellyP/migration/originals/" + normalized(record_id) + ".original", base=root, must_exist=True)
    output = data_path(root, journal["OutputPath"], must_exist=True)
    if not original.is_file() or not output.is_file():
        raise SystemExit("Seed journal lacks its original backup or output file.")
    if journal.get("SourceHash", "").upper() != synthetic_hash:
        raise SystemExit("Seed journal source hash does not match the harness synthetic PNG.")
    if sha256(original) != synthetic_hash:
        raise SystemExit("Seed original backup hash does not match the harness synthetic PNG.")
    if sha256(output) != journal["OutputHash"].upper():
        raise SystemExit("Seed output hash does not match the journal.")
    return journal_path, journal, original, output


def seed(root, baseline_version):
    root = guard_root(root)
    start_host(root)
    assert_owned_process(root)
    assert_loopback_bind(root)
    auth = load_auth(root)
    token = auth["token"]
    info = verify_program_data(root, token)
    active = wait_plugin(token, baseline_version)
    if active["Status"] != "Active":
        raise SystemExit("Baseline plugin is not active.")
    snapshot_path = safe_path(root, "upgrade-snapshot.json", base=root)
    if snapshot_path.exists():
        raise SystemExit("upgrade-snapshot.json already exists; refusing to overwrite a previous seed.")
    _, status_before = request("JellyP/Status", token=token)
    originals = safe_path(root, "data/plugins/Jellyfin.Plugin.JellyP/migration/originals", base=root)
    if status_before.get("RetainedBackupBytes") or (originals.is_dir() and any(originals.glob("*.original"))):
        raise SystemExit("Fixture already has active backups; use a fresh root.")
    movie = canary(token)
    _, synthetic_hash = ensure_canary_image(root, token, movie)
    config, tasks = configure_legacy(token, movie)
    status = run_baseline_apply(token)
    journal_path, journal, original, output = snapshot_files(root, movie["Id"], synthetic_hash)
    config_path = safe_path(root, "data/plugins/configurations/Jellyfin.Plugin.JellyP.xml", base=root, must_exist=True)
    baseline_dll = safe_path(root, "data/plugins/JellyP/" + DLL_NAME, base=root, must_exist=True)
    snapshot = {
        "schema": SCHEMA,
        "createdUtc": utc_now(),
        "root": str(root),
        "programDataPath": info["ProgramDataPath"],
        "hostVersion": info["Version"],
        "baselineVersion": baseline_version,
        "baselineDllSha256": sha256(baseline_dll),
        "syntheticSourceSha256": synthetic_hash,
        "plugin": active,
        "settings": config,
        "automation": canonical_automation(config, legacy=True),
        "globals": {
            "MinimumFreeSpaceBytes": config["MinimumFreeSpaceBytes"],
            "MaximumBackupBytes": config["MaximumBackupBytes"],
            "BackupRetentionDays": config["BackupRetentionDays"],
            "MinimumSavingsBytes": config["MinimumSavingsBytes"],
            "MinimumSavingsRatio": config["MinimumSavingsRatio"],
        },
        "tasks": sorted(
            [{key: task[key] for key in ("Id", "Key", "Triggers")} for task in tasks.values()],
            key=lambda task: task["Key"],
        ),
        "status": status,
        "movie": movie,
        "files": {
            "config": {"path": str(config_path.relative_to(root)), "sha256": sha256(config_path), "bytes": config_path.stat().st_size},
            "journal": {"path": str(journal_path.relative_to(root)), "sha256": sha256(journal_path), "bytes": journal_path.stat().st_size, "content": journal},
            "original": {"path": str(original.relative_to(root)), "sha256": sha256(original), "bytes": original.stat().st_size},
            "output": {"path": str(output), "sha256": sha256(output), "bytes": output.stat().st_size},
        },
    }
    write_private(snapshot_path, json.dumps(snapshot, indent=2) + "\n")
    print(
        "seed: exact item scope, interval/daily triggers, safe automation and one real journal/backup captured "
        f"(config {snapshot['files']['config']['sha256']}, journal {snapshot['files']['journal']['sha256']})"
    )


def canonical_automation(settings, legacy):
    required = {
        "Enabled", "DryRun", "PreserveOriginals", "Quality", "Lossless", "ResizeImages",
        "MaximumDimension", "MinimumSsim", "BatchSize", "ScopeMode", "SyncUnchangedPolicies",
        "MinimumSavingsRatio", "MinimumSavingsBytes", "MinimumFreeSpaceBytes",
        "MaximumBackupBytes", "BackupRetentionDays",
    }
    missing = sorted(required - set(settings))
    if missing:
        raise SystemExit(f"Automation settings are missing keys: {missing}")
    policies = settings["ImagePolicies"] if legacy else settings["Policies"]
    item_ids = settings["IncludedItemIds"] if legacy else settings["ItemIds"]
    library_ids = settings["IncludedLibraryIds"] if legacy else settings["LibraryIds"]
    return {
        "enabled": bool(settings["Enabled"]),
        "dryRun": bool(settings["DryRun"]),
        "preserveOriginals": bool(settings["PreserveOriginals"]),
        "quality": int(settings["Quality"]),
        "lossless": bool(settings["Lossless"]),
        "resizeImages": bool(settings["ResizeImages"]),
        "maximumDimension": int(settings["MaximumDimension"]),
        "minimumSsim": float(settings["MinimumSsim"]),
        "batchSize": int(settings["BatchSize"]),
        "scopeMode": settings["ScopeMode"],
        "itemIds": sorted(normalized(value) for value in item_ids),
        "libraryIds": sorted(normalized(value) for value in library_ids),
        "syncUnchangedPolicies": bool(settings["SyncUnchangedPolicies"]),
        "minimumSavingsRatio": float(settings["MinimumSavingsRatio"]),
        "minimumSavingsBytes": int(settings["MinimumSavingsBytes"]),
        "minimumFreeSpaceBytes": int(settings["MinimumFreeSpaceBytes"]),
        "maximumBackupBytes": int(settings["MaximumBackupBytes"]),
        "backupRetentionDays": int(settings["BackupRetentionDays"]),
        "policies": sorted(
            [
                {
                    "category": policy["Category"],
                    "enabled": bool(policy["Enabled"]),
                    "overrideGlobal": bool(policy["OverrideGlobal"]),
                    "quality": int(policy["Quality"]),
                    "lossless": bool(policy["Lossless"]),
                    "resizeImages": bool(policy["ResizeImages"]),
                    "maximumDimension": int(policy["MaximumDimension"]),
                    "minimumSsim": float(policy["MinimumSsim"]),
                }
                for policy in policies
            ],
            key=lambda policy: policy["category"],
        ),
    }


def current_automation(settings):
    if "Automation" not in settings or "Globals" not in settings:
        raise SystemExit("JellyP settings response lacks Automation or Globals.")
    automation = {**settings["Automation"], **settings["Globals"]}
    return canonical_automation(
        {
            **automation,
            "IncludedItemIds": automation["ItemIds"],
            "IncludedLibraryIds": automation["LibraryIds"],
            "ImagePolicies": automation["Policies"],
        },
        legacy=True,
    )


def task_snapshot(token):
    _, tasks = request("ScheduledTasks", token=token)
    return sorted(
        [{key: task[key] for key in ("Id", "Key", "Triggers")} for task in tasks if task.get("Category") == "JellyP"],
        key=lambda task: task["Key"],
    )


def operation(token, receipt):
    def terminal():
        _, detail = request("JellyP/Executions/" + receipt["OperationId"], token=token)
        return detail if detail["Operation"]["State"] not in ("Queued", "Running") else None
    return wait_for(terminal, "persisted restore operation", timeout=180)


def restore_paths(root, snapshot, require_original=True):
    journal = safe_path(root, snapshot["files"]["journal"]["path"], base=root, must_exist=True)
    original = safe_path(root, snapshot["files"]["original"]["path"], base=root, must_exist=require_original)
    record_id = snapshot["files"]["journal"]["content"]["Id"]
    backup = safe_path(root, "data/plugins/Jellyfin.Plugin.JellyP/migration/originals/" + normalized(record_id) + ".original", base=root)
    return journal, original, backup


def assert_restore_allowed(root, snapshot):
    evidence = safe_path(root, "upgrade-verify.json", base=root)
    if evidence.exists():
        raise SystemExit("Verification evidence already exists; refusing a second restore. Use a fresh fixture and seed.")
    journal = safe_path(root, snapshot["files"]["journal"]["path"], base=root, must_exist=True)
    record = json.loads(journal.read_text())
    backup = safe_path(
        root,
        "data/plugins/Jellyfin.Plugin.JellyP/migration/originals/" + normalized(snapshot["files"]["journal"]["content"]["Id"]) + ".original",
        base=root,
    )
    if record.get("State") == "RolledBack" or record.get("BackupDeleted") is True or not backup.is_file():
        raise SystemExit("Legacy backup already consumed; verification is not reentrant. Use a fresh fixture and seed.")
    if record.get("SourceHash", "").upper() != snapshot["files"]["original"]["sha256"]:
        raise SystemExit("Legacy journal source hash does not match the snapshot original.")
    original = safe_path(root, snapshot["files"]["original"]["path"], base=root)
    if not original.is_file():
        raise SystemExit("Legacy original backup is missing; verification is not reentrant. Use a fresh fixture and seed.")
    if sha256(original) != snapshot["files"]["original"]["sha256"]:
        raise SystemExit("Legacy original backup changed before restore.")


def assert_restored_journal(root, snapshot):
    journal, original, backup = restore_paths(root, snapshot, require_original=False)
    record = json.loads(journal.read_text())
    if normalized(record.get("Id")) != normalized(snapshot["files"]["journal"]["content"]["Id"]):
        raise SystemExit("Restored journal ID changed.")
    if record.get("State") != "RolledBack":
        raise SystemExit(f"Restored journal state is {record.get('State')}, expected RolledBack.")
    if record.get("BackupDeleted") is not True:
        raise SystemExit("Restored journal does not mark BackupDeleted.")
    if record.get("SourceHash", "").upper() != snapshot["files"]["original"]["sha256"]:
        raise SystemExit("Restored journal source hash changed.")
    if backup.exists():
        raise SystemExit("Restored journal still has a backup file.")


def verify(root, expected_version, expected_dll_sha256):
    root = guard_root(root)
    if not is_sha256(expected_dll_sha256):
        raise SystemExit("A 64-hex expected candidate DLL SHA256 is required for verify.")
    snapshot = load_snapshot(root)
    assert_restore_allowed(root, snapshot)
    start_host(root)
    assert_owned_process(root)
    assert_loopback_bind(root)
    auth = load_auth(root)
    token = auth["token"]
    info = verify_program_data(root, token)
    active = wait_plugin(token, expected_version)
    installed = safe_path(root, "data/plugins/JellyP/" + DLL_NAME, base=root, must_exist=True)
    installed_hash = sha256(installed)
    baseline_hash = snapshot["baselineDllSha256"]
    if installed_hash != expected_dll_sha256.upper():
        raise SystemExit(f"Installed candidate DLL SHA256 {installed_hash} != expected {expected_dll_sha256.upper()}.")
    if installed_hash == baseline_hash:
        raise SystemExit("Installed DLL is the baseline; no upgrade was performed.")
    assert_single_plugin_install(root)
    assert_loaded_assembly(root, installed)
    assert_candidate_page(root, token)
    checks = []

    def check(truth, label):
        if not truth:
            raise SystemExit("FAIL: " + label)
        checks.append(label)
        print("PASS " + label)

    check(info["Version"] == "12.1.0", "official Jellyfin 12.1 host")
    check(installed_hash == expected_dll_sha256.upper(), "installed DLL hash equals the expected candidate")
    check(installed_hash != baseline_hash, "installed DLL differs from the baseline")
    check(True, "exactly one JellyP plugin folder and DLL exist")
    check(True, "loaded assembly path is exclusively the installed candidate")
    check(True, "installed candidate serves the exact current embedded page")
    files = snapshot["files"]
    config_path = safe_path(root, files["config"]["path"], base=root, must_exist=True)
    journal_path = safe_path(root, files["journal"]["path"], base=root, must_exist=True)
    original_path = safe_path(root, files["original"]["path"], base=root, must_exist=True)
    output_path = safe_path(root, files["output"]["path"], base=root, must_exist=True)
    check(sha256(config_path) == files["config"]["sha256"], "legacy XML is byte-identical after upgrade")
    check(sha256(journal_path) == files["journal"]["sha256"], "legacy journal is byte-identical after upgrade")
    check(sha256(original_path) == files["original"]["sha256"], "legacy original backup is byte-identical after upgrade")
    check(sha256(output_path) == files["output"]["sha256"], "converted output is byte-identical after upgrade")

    _, settings = request("JellyP/Settings", token=token)
    check(settings.get("DefaultsSource") == "legacy", "legacy defaults are projected for reading")
    check(settings.get("LegacyDefaultsWarning") is True, "legacy-defaults warning remains visible")
    check(current_automation(settings) == snapshot["automation"], "automation, exact item scope, policies and limits are unchanged")
    check(settings["Automation"]["Enabled"] is False and settings["Automation"]["DryRun"] is True, "upgrade did not enable mutations or disable dry run")
    check(settings["Automation"]["PreserveOriginals"] is True, "upgrade preserved original-retention mode")
    check(settings["Automation"]["ScopeMode"] == "items", "scope remains the exact item mode, not all/legacy expansion")
    check(
        [normalized(value) for value in settings["Automation"]["ItemIds"]]
        == [normalized(value) for value in snapshot["settings"]["IncludedItemIds"]],
        "exact selected item ID is unchanged",
    )
    check(settings["Automation"]["LibraryIds"] == [], "no library was added to the legacy item scope")
    check(settings["Globals"] == snapshot["globals"], "global storage and savings limits are unchanged")
    check(task_snapshot(token) == snapshot["tasks"], "task IDs, keys and triggers are unchanged; no new schedule was created")
    check(sha256(config_path) == files["config"]["sha256"], "reading legacy configuration did not rewrite the XML")
    _, status = request("JellyP/Status", token=token)
    check(status.get("PendingRecovery") == 0, "upgrade leaves no pending recovery")
    assert_restore_allowed(root, snapshot)
    _, backups = request("JellyP/Backups?filter=restorable&limit=200", token=token)
    record = next(
        (item for item in backups.get("Items", []) if normalized(item["RecordId"]) == normalized(files["journal"]["content"]["Id"])),
        None,
    )
    check(record is not None and record.get("Restorable") is True, "legacy journal is exposed as restorable by the current API")
    check(record["SourceHash"].upper() == files["original"]["sha256"], "restorable record still names the exact legacy source hash")
    _, receipt = request("JellyP/Backups/Restore", {"RecordIds": [record["RecordId"]]}, method="POST", token=token)
    detail = operation(token, receipt)
    operation_state = detail["Operation"]
    check(operation_state["State"] == "Completed" and operation_state["Changed"] == 1, "current API restores the legacy journal")
    check(operation_state["Failed"] == 0 and operation_state["Invalidated"] == 0 and operation_state["NotStarted"] == 0, "restore reports no failure, invalidation or skipped item")
    check(detail["Items"][0]["Status"] == "Restored", "restore result identifies the item as restored")
    _, images = request("Items/" + snapshot["movie"]["Id"] + "/Images", token=token)
    primary = next((image for image in images if image.get("ImageType") == "Primary"), None)
    check(primary is not None, "restored Primary is registered by Jellyfin")
    restored = data_path(root, primary["Path"])
    check(sha256(restored) == files["original"]["sha256"], "restored artwork is byte-exact legacy original")
    check(restored.stat().st_size == files["original"]["bytes"], "restored artwork has the exact original size")
    assert_restored_journal(root, snapshot)
    check(True, "post-restore journal is RolledBack, BackupDeleted and its backup is absent")
    check(task_snapshot(token) == snapshot["tasks"], "restore did not add or remove task triggers")
    check(sha256(config_path) == files["config"]["sha256"], "restore did not rewrite legacy configuration")

    evidence = {
        "schema": SCHEMA,
        "verifiedUtc": utc_now(),
        "root": str(root),
        "programDataPath": info["ProgramDataPath"],
        "hostVersion": info["Version"],
        "candidate": {"version": active["Version"], "dllSha256": installed_hash, "loadedAssemblyPath": str(installed)},
        "baselineDllSha256": baseline_hash,
        "checks": checks,
        "restore": {
            "operationId": operation_state["OperationId"],
            "state": operation_state["State"],
            "changed": operation_state["Changed"],
            "failed": operation_state["Failed"],
            "invalidated": operation_state["Invalidated"],
            "activePath": str(restored),
            "activeSha256": sha256(restored),
            "expectedSha256": files["original"]["sha256"],
            "journalState": "RolledBack",
            "backupDeleted": True,
        },
        "files": {
            "configSha256": files["config"]["sha256"],
            "journalSha256": files["journal"]["sha256"],
            "originalSha256": files["original"]["sha256"],
            "outputSha256": files["output"]["sha256"],
        },
    }
    write_private(safe_path(root, "upgrade-verify.json", base=root), json.dumps(evidence, indent=2) + "\n")
    print(f"verify: {len(checks)} checks passed; restored {restored.name} with exact hash {evidence['restore']['activeSha256']}")


def upgrade_host(root, candidate, version, expected_dll_sha256):
    snapshot = load_snapshot(root)
    assert_candidate_identity(candidate, version, expected_dll_sha256, snapshot["baselineDllSha256"])
    start_host(root)
    token = login(root)
    verify_program_data(root, token)
    stop_host(root)
    install_plugin(root, candidate, version)
    installed = assert_single_plugin_install(root)
    # Compare against the requested identity, not just whatever ZIP was read
    # during installation; the archive may have changed since pre-validation.
    installed = safe_path(root, "data/plugins/JellyP/" + DLL_NAME, base=root, must_exist=True)
    if sha256(installed) != expected_dll_sha256.upper():
        raise SystemExit("Installed candidate DLL hash does not match the expected build.")
    start_host(root)
    token = login(root)
    verify_program_data(root, token)
    wait_plugin(token, version)
    assert_loaded_assembly(root, installed)
    assert_candidate_page(root, token)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--phase", choices=("bootstrap", "seed", "upgrade", "verify", "all", "stop"), default="all")
    parser.add_argument("--baseline", type=Path, default=REPO / "artifacts/JellyP_0.3.0.1.zip")
    parser.add_argument("--candidate", type=Path, help="Candidate plugin ZIP for upgrade/all.")
    parser.add_argument("--expected-baseline-version", default="0.3.0.1")
    parser.add_argument("--expected-candidate-version", default="0.3.0.1")
    parser.add_argument(
        "--expected-baseline-sha256",
        default="9EB58165657F30D9C633848D4ED707A33FBCD079688B4C966BE36030086657CA",
        help="Verified legacy baseline archive SHA256 (empty string disables the check).",
    )
    parser.add_argument("--expected-dll-sha256", help="Expected candidate DLL SHA256 (required for upgrade/all/verify).")
    args = parser.parse_args(argv)
    root = guard_root(args.root)
    if args.expected_baseline_sha256 and not is_sha256(args.expected_baseline_sha256):
        parser.error("--expected-baseline-sha256 must be 64 hex characters")
    if args.phase in ("upgrade", "all", "verify") and not is_sha256(args.expected_dll_sha256):
        parser.error("--expected-dll-sha256 is required (64 hex characters) for upgrade/all/verify")
    if args.phase in ("upgrade", "all") and not args.candidate:
        parser.error("--candidate is required for upgrade/all")
    if args.phase in ("upgrade", "all") and not args.candidate.is_file():
        parser.error(f"candidate archive not found: {args.candidate}")
    if args.phase == "bootstrap":
        bootstrap(root, args.baseline, args.expected_baseline_version, args.expected_baseline_sha256)
    elif args.phase == "seed":
        seed(root, args.expected_baseline_version)
    elif args.phase == "upgrade":
        upgrade_host(root, args.candidate, args.expected_candidate_version, args.expected_dll_sha256)
        print(f"upgrade: candidate {args.expected_candidate_version} active; fixture left running")
    elif args.phase == "verify":
        verify(root, args.expected_candidate_version, args.expected_dll_sha256)
    elif args.phase == "stop":
        stop_host(root)
        print("stop: exact fixture process stopped")
    else:
        bootstrap(root, args.baseline, args.expected_baseline_version, args.expected_baseline_sha256)
        seed(root, args.expected_baseline_version)
        upgrade_host(root, args.candidate, args.expected_candidate_version, args.expected_dll_sha256)
        verify(root, args.expected_candidate_version, args.expected_dll_sha256)
        print(f"all: fixture left running (PID {(safe_path(root, 'host.pid', base=root).read_text().strip())}); use --phase stop to end it")


if __name__ == "__main__":
    main()
