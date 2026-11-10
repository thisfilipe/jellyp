#!/usr/bin/env python3
"""Exercise V08/V10/V15 against one marked disposable Jellyfin fixture.

This host-level harness reuses the guards in scripts/smoke-upgrade.py. It does
not create a release identity, publish anything, or touch production. It installs
a test-only slow metadata provider under the fixture's plugin directory so a real
Jellyfin library refresh can be held open deterministically; no product fault
endpoint and no fake HTTP response is used.

The fixture must already be bootstrapped by scripts/smoke-upgrade.py (Jellyfin
12.1, wizard completed, synthetic media and credentials under the marked root).
The script installs the current candidate DLL from an explicitly supplied ZIP,
verifies its SHA256, creates three synthetic libraries, and runs:

* V08: cancellation by operationId before the first mutation (plan Ready) and
  between transactions after a real mutation (plan Consumed, no next write).
* V10: a real interval trigger runs the native Optimize task while a related
  library refresh waits/resumes, and while an unrelated refresh does not block.
* V15: a real SIGKILL of the verified fixture PID after the test-only fixture
  plugin has held the real image save immediately after its WebP bytes were
  written and before the journal is committed. The harness verifies the on-disk
  output hash against the Prepared journal, restarts the same DLL, checks
  Interrupted/no-replay/PendingRecovery and performs explicit recovery with
  exact bytes/hashes and proven removal of the pre-kill output.

All paths are under the marked root. Credentials and tokens are never printed.
The `all` phase stops the fixture at the end by default and requires the public
loopback port to be free; `setup` and `run` leave it running for inspection.
"""
import argparse
import base64
import datetime
import hashlib
import importlib.util
import json
import os
import select
import shutil
import signal
import subprocess
import sys
import time
import urllib.parse
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
CANDIDATE_VERSION = "0.4.0.1"
HARNESS_PATH = REPO / "scripts/smoke-upgrade.py"
_spec = importlib.util.spec_from_file_location("jellyp_smoke_upgrade", HARNESS_PATH)
h = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(h)

TEST_PLUGIN_GUID = "7f1f8f4e-6f0e-4a0e-9c1e-6b7d8e9f0a1b"
TEST_PLUGIN_VERSION = "1.0.0.0"
TEST_PLUGIN_DLL = "JellyP.TestSlowMetadata.dll"
TEST_PLUGIN_FOLDER = "JellyPTestSlowMetadata"
TEST_PLUGIN_NAME = "JellyP Test Slow Metadata"
DEFAULT_TEST_PLUGIN = REPO / "tests/host-fixture/JellyP.TestSlowMetadata/bin/Release/net10.0" / TEST_PLUGIN_DLL
STATE_NAME = "host-gates-state.json"
EVIDENCE_NAME = "host-gates-evidence.json"
SLOW_CONTROL_NAME = "test-provider-delay"
SLOW_RELEASE_NAME = "test-provider-release"
IMAGE_BARRIER_NAME = "test-image-barrier"
TARGET_LIBRARY = "JellyP target"
BARRIER_LIBRARY = "JellyP barrier"
V15_LIBRARY = "JellyP v15"


class GateFailure(RuntimeError):
    """A host gate did not produce the required observation."""


def now_iso():
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def utc_stamp():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def parse_utc(value):
    if not value:
        return None
    text = str(value).replace("Z", "+00:00")
    try:
        parsed = datetime.datetime.fromisoformat(text)
    except ValueError:
        return None
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=datetime.timezone.utc)
    return parsed


def split_inotify_chunk(buffer, chunk):
    """Split binary inotify output without losing a line that shares a read."""
    data = buffer + chunk
    parts = data.split(b"\n")
    rest = parts.pop()
    return rest, [part.decode("utf-8", "replace").strip() for part in parts if part]


def inotify_event_names(buffer, chunk):
    """Return the unterminated remainder and non-temp event names in order."""
    rest, lines = split_inotify_chunk(buffer, chunk)
    return rest, [line for line in lines if line and ".tmp" not in line]


def write_private_bytes(path, data):
    """Write fixture bytes with mode 0600 at creation and O_NOFOLLOW."""
    path = Path(path)
    flags = os.O_WRONLY | os.O_CREAT | os.O_TRUNC | getattr(os, "O_NOFOLLOW", 0)
    try:
        descriptor = os.open(path, flags, 0o600)
    except OSError as error:
        raise GateFailure(f"Refusing to write private fixture file {path}: {error}") from None
    try:
        os.fchmod(descriptor, 0o600)
        with os.fdopen(descriptor, "wb") as handle:
            descriptor = -1
            handle.write(data)
    finally:
        if descriptor >= 0:
            os.close(descriptor)


def file_probe(path):
    """Read-only evidence for an existing/absent fixture file; never follows a symlink."""
    if path is None:
        return {"path": None, "exists": False, "hash": None, "bytes": 0, "isSymlink": False}
    candidate = Path(path)
    if candidate.is_symlink():
        return {"path": str(candidate), "exists": False, "hash": None, "bytes": 0, "isSymlink": True}
    if not candidate.is_file():
        return {"path": str(candidate), "exists": False, "hash": None, "bytes": 0, "isSymlink": False}
    return {
        "path": str(candidate),
        "exists": True,
        "hash": hashlib.sha256(candidate.read_bytes()).hexdigest().upper(),
        "bytes": candidate.stat().st_size,
        "isSymlink": False,
    }


def preflight():
    missing = [name for name in ("ffmpeg", "inotifywait") if shutil.which(name) is None]
    if missing:
        raise GateFailure(
            "Missing fixture dependencies: " + ", ".join(missing)
            + ". Install them explicitly on the disposable host; this harness never runs a package manager."
        )


def arm_image_barrier(root, source_paths):
    """Arm the test-only host barrier on exact item metadata folders.

    The fixture plugin blocks the real image save after its bytes are written
    and records the completed path atomically in <barrier>.reached. This removes
    the inotify/SIGSTOP race between the output write and the journal commit.
    """
    barrier = h.safe_path(root, IMAGE_BARRIER_NAME, base=root, must_exist=False)
    reached = Path(str(barrier) + ".reached")
    release = Path(str(barrier) + ".release")
    for stale in (reached, release):
        if stale.is_file() and not stale.is_symlink():
            stale.unlink()
    folders = sorted({str(Path(path).resolve().parent) for path in source_paths if path})
    if not folders:
        raise GateFailure("V15 has no source image folder to arm the test barrier with.")
    write_private_bytes(barrier, ("\n".join(folders) + "\n").encode("utf-8"))
    return barrier, reached, release, folders


def release_image_barrier(release):
    """Let a blocked fixture thread continue during failure cleanup."""
    if release is None:
        return
    try:
        if not release.exists():
            write_private_bytes(release, b"release\n")
    except (OSError, GateFailure):
        pass


def disarm_image_barrier(root):
    """Remove only the armed barrier; keep reached evidence for inspection."""
    barrier = h.safe_path(root, IMAGE_BARRIER_NAME, base=root, must_exist=False)
    if barrier.is_file() and not barrier.is_symlink():
        barrier.unlink()


def parse_stat_starttime(text):
    """Return field 22 (starttime) from /proc/<pid>/stat after the comm field."""
    after_comm = text.rsplit(")", 1)[1].split()
    return after_comm[19]


def proc_starttime(pid):
    return parse_stat_starttime(Path(f"/proc/{pid}/stat").read_text())


def proc_uid(pid):
    for line in Path(f"/proc/{pid}/status").read_text().splitlines():
        if line.startswith("Uid:"):
            return int(line.split()[1])
    raise GateFailure(f"Cannot read the owner of fixture PID {pid}.")


def assert_process_guard(root, expected_starttime=None):
    """Require the exact marked-root process, same user, starttime and loopback socket."""
    root = h.guard_root(root)
    pid = h.assert_owned_process(root)  # exact argv and cwd
    if proc_uid(pid) != os.getuid():
        raise GateFailure(f"Fixture PID {pid} is owned by another user.")
    starttime = proc_starttime(pid)
    if expected_starttime is not None and starttime != expected_starttime:
        raise GateFailure(f"Fixture PID {pid} starttime changed; refusing process control.")
    h.assert_loopback_bind(root)  # exactly one 127.0.0.1:18096 socket owned by PID
    return pid, starttime


class PidfdControl:
    """Linux pidfd-scoped SIGSTOP/SIGCONT/SIGKILL for one verified fixture identity.

    A pidfd pins the process object, so a reused PID cannot receive a signal from
    this handle. Every signal is still preceded by the argv/cwd/uid/starttime/
    loopback guard; a failed guard sends nothing.
    """

    def __init__(self, root, pid, starttime, pidfd):
        self.root = root
        self.pid = pid
        self.starttime = starttime
        self.pidfd = pidfd
        self.stop_sent = False
        self.kill_sent = False
        self.closed = False

    @classmethod
    def open(cls, root, pid, starttime):
        if not hasattr(os, "pidfd_open") or not hasattr(signal, "pidfd_send_signal"):
            raise GateFailure("Linux pidfd_open/pidfd_send_signal are required for V15 process control.")
        return cls(root, pid, starttime, os.pidfd_open(pid, 0))

    def _guard(self):
        assert_process_guard(self.root, self.starttime)

    def _send(self, sig):
        signal.pidfd_send_signal(self.pidfd, sig)

    def stop(self):
        self._guard()
        self._send(signal.SIGSTOP)
        self.stop_sent = True

    def kill(self):
        self._guard()
        self._send(signal.SIGKILL)
        self.kill_sent = True
        self.stop_sent = False

    def cont(self):
        if not self.stop_sent or self.kill_sent:
            return
        self._guard()
        self._send(signal.SIGCONT)
        self.stop_sent = False

    def close(self):
        if self.closed:
            return
        if self.stop_sent and not self.kill_sent:
            try:
                self.cont()
            except BaseException:
                # Identity is unknown or the process is gone. Never signal an
                # unverified PID merely to clear a stopped state.
                pass
        try:
            os.close(self.pidfd)
        finally:
            self.closed = True


def kill_verified(root, expected_starttime, pidfd=None):
    """SIGKILL only the verified fixture PID and wait for its socket to close."""
    pid, starttime = assert_process_guard(root, expected_starttime)
    if pidfd is None:
        control = PidfdControl.open(root, pid, starttime)
        try:
            control.kill()
        finally:
            control.close()
    else:
        assert_process_guard(root, expected_starttime)
        signal.pidfd_send_signal(pidfd, signal.SIGKILL)
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline and (h.process_alive(pid) or h.port_open()):
        time.sleep(0.05)
    if h.process_alive(pid) or h.port_open():
        raise GateFailure("Verified fixture process or its loopback socket survived SIGKILL.")
    pid_file = h.safe_path(root, "host.pid", base=root)
    if pid_file.is_file() and pid_file.read_text().strip() == str(pid):
        pid_file.unlink()
    return {"pid": pid, "starttime": starttime, "signal": "SIGKILL", "portFree": True, "pidfd": pidfd is not None}


def login_verified(root):
    auth = h.load_auth(root)
    token = auth["token"]
    info = h.verify_program_data(root, token)
    if info.get("Version") != "12.1.0":
        raise GateFailure("Fixture host is not Jellyfin 12.1.")
    return token, info


def restart_same_candidate(root, expected_dll_sha256, previous_identity=None, require_test_plugin=False):
    """Stop the verified fixture if present, start it again, and prove a new identity."""
    h.stop_host(root)
    if h.port_open():
        raise GateFailure("Fixture port remained open before restart; refusing to start a second process.")
    h.start_host(root)
    pid, starttime = assert_process_guard(root)
    if previous_identity is not None and (pid, starttime) == tuple(previous_identity):
        raise GateFailure("Restart returned the same PID/starttime identity; refusing to claim a restart.")
    token, info = login_verified(root)
    active = h.wait_plugin(token, CANDIDATE_VERSION)
    installed = h.assert_single_plugin_install(root)
    if h.sha256(installed) != expected_dll_sha256.upper():
        raise GateFailure("Restarted host loaded a DLL different from the expected candidate.")
    h.assert_loaded_assembly(root, installed)
    h.assert_candidate_page(root, token)
    test_plugin = test_plugin_status(token)
    if require_test_plugin and (test_plugin is None or test_plugin.get("Status") != "Active"):
        raise GateFailure("Test-only slow metadata plugin is not Active after restart.")
    return {
        "pid": pid,
        "starttime": starttime,
        "token": token,
        "info": info,
        "active": active,
        "installed": installed,
        "installedSha256": h.sha256(installed),
        "testPlugin": test_plugin,
    }


def install_test_plugin(root, dll):
    dll = Path(dll)
    if not dll.is_file() or dll.is_symlink():
        raise GateFailure(f"Missing test-only slow metadata DLL: {dll}")
    folder = h.safe_path(root, "data/plugins/" + TEST_PLUGIN_FOLDER, base=root)
    folder.mkdir(parents=True, exist_ok=True)
    target_dll = h.safe_path(root, f"data/plugins/{TEST_PLUGIN_FOLDER}/{TEST_PLUGIN_DLL}", base=root)
    shutil.copy2(dll, target_dll)
    meta = {
        "category": "General",
        "name": TEST_PLUGIN_NAME,
        "guid": TEST_PLUGIN_GUID,
        "version": TEST_PLUGIN_VERSION,
        "targetAbi": "12.1.0.0",
        "owner": "fixture",
        "status": "Active",
        "autoUpdate": False,
        "assemblies": [TEST_PLUGIN_DLL],
        "description": "Test-only slow local metadata provider for a disposable fixture.",
    }
    h.write_private(folder / "meta.json", json.dumps(meta, indent=2) + "\n")
    return {"dll": str(target_dll), "sha256": h.sha256(target_dll)}


def test_plugin_status(token):
    _, plugins = h.request("Plugins", token=token)
    return next((p for p in plugins if p.get("Name") == TEST_PLUGIN_NAME), None)


def ensure_test_plugin_active(root, token):
    item = test_plugin_status(token)
    if item is None:
        raise GateFailure("Test-only slow metadata plugin was not discovered by Jellyfin.")
    if item["Status"] == "Active":
        return item
    if item["Status"] == "Restart":
        restarted = restart_same_candidate(root, h.sha256(h.safe_path(root, "data/plugins/JellyP/" + h.DLL_NAME, base=root)), require_test_plugin=True)
        item = test_plugin_status(restarted["token"])
        if item is not None and item["Status"] == "Active":
            return item
    try:
        h.request(f"Plugins/{TEST_PLUGIN_GUID}/{TEST_PLUGIN_VERSION}/Enable", method="POST", token=token)
    except RuntimeError:
        pass
    restarted = restart_same_candidate(root, h.sha256(h.safe_path(root, "data/plugins/JellyP/" + h.DLL_NAME, base=root)), require_test_plugin=True)
    item = test_plugin_status(restarted["token"])
    if item is None or item["Status"] != "Active":
        raise GateFailure("Test-only slow metadata plugin did not become Active; inspect the fixture log.")
    return item


def set_slow_delay(root, seconds):
    if not isinstance(seconds, int) or not 0 <= seconds <= 600:
        raise GateFailure("Slow metadata delay must be between 0 and 600 seconds.")
    h.write_private(h.safe_path(root, SLOW_CONTROL_NAME, base=root), f"{seconds}\n")


def release_slow_provider(root):
    h.write_private(h.safe_path(root, SLOW_RELEASE_NAME, base=root), "release\n")


def clear_slow_release(root):
    path = h.safe_path(root, SLOW_RELEASE_NAME, base=root, must_exist=False)
    if path.is_file() and not path.is_symlink():
        path.unlink()


def task_info(token, key):
    _, tasks = h.request("ScheduledTasks", token=token)
    found = next((task for task in tasks if task.get("Key") == key), None)
    if not found:
        raise GateFailure(f"Jellyfin scheduled task {key} is missing.")
    return found


def wait_task_idle(token, key, timeout=120):
    return h.wait_for(lambda: task_info(token, key) if task_info(token, key)["State"] == "Idle" else None,
                      f"native task {key} idle", timeout=timeout, interval=0.1)


def set_task_triggers(token, key, triggers):
    task = task_info(token, key)
    h.request("ScheduledTasks/" + task["Id"] + "/Triggers", triggers, method="POST", token=token)
    current = task_info(token, key)
    if current["Triggers"] != triggers:
        raise GateFailure(f"Native task {key} triggers were not persisted exactly.")


def wait_scheduler_running(token, key, previous_start, timeout=150):
    def running():
        task = task_info(token, key)
        start = (task.get("LastExecutionResult") or {}).get("StartTimeUtc")
        return task if task["State"] == "Running" or (start and start != previous_start) else None
    return h.wait_for(running, f"real scheduler start for {key}", timeout=timeout, interval=0.05)


def wait_report_phase(token, phase, timeout=120):
    def match():
        _, status = h.request("JellyP/Status", token=token)
        report = status.get("LastRun", {})
        return status if report.get("Phase") == phase and report.get("Running") else None
    return h.wait_for(match, f"JellyP report phase {phase}", timeout=timeout, interval=0.05)


def wait_report_terminal(token, timeout=180, exclude_execution=None):
    def terminal():
        _, status = h.request("JellyP/Status", token=token)
        report = status.get("LastRun", {})
        if exclude_execution and report.get("ExecutionId") == exclude_execution:
            return None
        if report.get("Running") or report.get("Phase") in ("Queued", "Preparing", "Processing", "WaitingForMetadata", "Restoring", "RetiringOriginals"):
            return None
        return status
    return h.wait_for(terminal, "JellyP report terminal state", timeout=timeout, interval=0.05)


def operation_detail(token, opid):
    _, detail = h.request("JellyP/Executions/" + opid, token=token)
    return detail


def wait_operation(token, opid, predicate, label, timeout=120, interval=0.02):
    def match():
        detail = operation_detail(token, opid)
        return detail if predicate(detail) else None
    return h.wait_for(match, label, timeout=timeout, interval=interval)


def wait_operation_terminal(token, opid, timeout=120):
    return wait_operation(
        token, opid,
        lambda detail: detail["Operation"]["State"] in ("Completed", "CompletedWithErrors", "Cancelled", "Interrupted", "Failed"),
        "operation terminal state", timeout=timeout,
    )


def plan_status(token, plan_id):
    _, plan = h.request("JellyP/Plan/" + plan_id, token=token)
    return plan


def analyze_plan(token, item_ids):
    _, plan = h.request("JellyP/Plan/Analyze", {"Scope": "items", "ItemIds": item_ids}, method="POST", token=token)
    return plan


def plan_keys(token, plan_id):
    _, keys = h.request("JellyP/Plan/" + plan_id + "/Keys", token=token)
    return keys["Keys"]


def apply_plan(token, plan_id, keys):
    _, receipt = h.request("JellyP/Plan/" + plan_id + "/Apply", {"Keys": keys}, method="POST", token=token)
    return receipt


def operation_file(root, opid):
    path = h.safe_path(
        root, f"data/plugins/Jellyfin.Plugin.JellyP/migration/operations/{opid.replace('-', '').lower()}.json",
        base=root, must_exist=False,
    )
    if not path.is_file():
        return None
    return json.loads(path.read_text())


def journal_records(root):
    directory = h.safe_path(root, "data/plugins/Jellyfin.Plugin.JellyP/migration/journal", base=root, must_exist=False)
    if not directory.is_dir():
        return []
    records = []
    for path in sorted(directory.glob("*.json")):
        h.safe_path(root, path.relative_to(root), base=root, must_exist=True)
        records.append({"path": path, "record": json.loads(path.read_text())})
    return records


def journal_for_operation(root, opid):
    wanted = opid.replace("-", "").lower()
    return [entry for entry in journal_records(root)
            if str(entry["record"].get("ExecutionId", "")).replace("-", "").lower() == wanted]


def journal_for_item(root, item_id):
    wanted = h.normalized(item_id)
    return [entry for entry in journal_records(root)
            if h.normalized(entry["record"].get("Source", {}).get("ItemId", "")) == wanted]


def committed_output(root, item_id, source_hash, active_hash):
    """Require exactly one committed WebP journal whose hashes match the active file."""
    records = journal_for_item(root, item_id)
    committed = [entry["record"] for entry in records if entry["record"].get("State") == "Committed"]
    if len(committed) != 1:
        raise GateFailure(f"Expected exactly one committed journal for item {item_id}, found {len(committed)}.")
    record = committed[0]
    output_path = record.get("OutputPath") or ""
    if Path(output_path).suffix.lower() != ".webp":
        raise GateFailure(f"Committed output for item {item_id} is not WebP: {output_path}")
    if str(record.get("SourceHash", "")).upper() != source_hash.upper():
        raise GateFailure(f"Committed source hash for item {item_id} does not match the synthetic source.")
    if str(record.get("OutputHash", "")).upper() != active_hash.upper():
        raise GateFailure(f"Committed output hash for item {item_id} does not match the active image.")
    if record.get("BackupDeleted") is True:
        raise GateFailure(f"Committed output for item {item_id} unexpectedly deleted its retained original.")
    return record


def state_hash(state, item_id):
    wanted = h.normalized(item_id)
    for key, value in state["hashes"].items():
        if h.normalized(key) == wanted:
            return value
    raise GateFailure(f"No synthetic source hash recorded for item {item_id}.")


def first_fresh_item(root, token, state, group, exclude=()):
    excluded = {h.normalized(item_id) for item_id in exclude}
    for item in state["items"][group]:
        if h.normalized(item["Id"]) in excluded:
            continue
        info = primary_info(root, token, item["Id"])
        if info["hash"] == state_hash(state, item["Id"]):
            return item
    raise GateFailure(f"No unused synthetic {group} item remains for this scenario.")


def primary_info(root, token, item_id):
    _, images = h.request("Items/" + item_id + "/Images", token=token)
    primary = next((image for image in images if image.get("ImageType") == "Primary"), None)
    if primary is None:
        return {"path": None, "hash": None, "bytes": 0, "exists": False}
    path = h.data_path(root, primary["Path"], must_exist=False)
    return {
        "path": str(path),
        "hash": h.sha256(path) if path.is_file() else None,
        "bytes": path.stat().st_size if path.is_file() else 0,
        "exists": path.is_file(),
    }


def set_primary_png(root, token, item, data, label):
    expected = hashlib.sha256(data).hexdigest().upper()
    _, images = h.request("Items/" + item["Id"] + "/Images", token=token)
    primary = next((image for image in images if image.get("ImageType") == "Primary"), None)
    if primary:
        path = h.data_path(root, primary["Path"], must_exist=False)
        if path.is_file() and h.sha256(path) == expected:
            return {"path": str(path), "hash": expected, "bytes": path.stat().st_size}
        raise GateFailure(f"{label} already has non-synthetic artwork; use a fresh fixture.")
    h.request(
        "Items/" + item["Id"] + "/Images/Primary",
        token=token,
        raw=base64.b64encode(data),
        method="POST",
        content_type="image/png",
    )
    _, images = h.request("Items/" + item["Id"] + "/Images", token=token)
    primary = next((image for image in images if image.get("ImageType") == "Primary"), None)
    if primary is None:
        raise GateFailure(f"{label} did not register the synthetic Primary image.")
    path = h.data_path(root, primary["Path"], must_exist=True)
    if h.sha256(path) != expected:
        raise GateFailure(f"{label} synthetic Primary bytes changed in Jellyfin.")
    return {"path": str(path), "hash": expected, "bytes": path.stat().st_size}


def library_status(token, name):
    _, folders = h.request("Library/VirtualFolders", token=token)
    return next((folder for folder in folders if folder.get("Name") == name), None)


def wait_library_status(token, name, status, timeout=120):
    def match():
        folder = library_status(token, name)
        return folder if folder is not None and folder.get("RefreshStatus") == status else None
    return h.wait_for(match, f"library {name} refresh {status}", timeout=timeout, interval=0.1)


def trigger_library_refresh(token, library_id):
    h.request("Items/" + library_id + "/Refresh?metadataRefreshMode=FullRefresh", method="POST", token=token)


def movies(token):
    _, result = h.request("Items?Recursive=true&IncludeItemTypes=Movie&Fields=Path&Limit=500", token=token)
    return result.get("Items", [])


def wait_movie(token, name, timeout=120):
    return h.wait_for(lambda: next((item for item in movies(token) if item.get("Name") == name), None),
                      f"movie {name}", timeout=timeout, interval=0.1)


def generate_movie(root, relative_path):
    root = h.guard_root(root)
    path = h.safe_path(root, relative_path, base=root, must_exist=False)
    path.parent.mkdir(parents=True, exist_ok=True)
    path = h.safe_path(root, relative_path, base=root, must_exist=False)
    if path.is_file():
        return path
    subprocess.run(
        [
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "color=c=black:s=64x64:d=1",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", str(path),
        ],
        check=True,
    )
    return path


def ensure_library(token, name, media_path):
    folder = library_status(token, name)
    if folder is not None:
        locations = folder.get("Locations") or []
        if locations and str(media_path) not in locations:
            raise GateFailure(f"Library {name} already exists with a different path.")
        return folder["ItemId"]
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
        {"name": name, "collectionType": "movies", "paths": str(media_path), "refreshLibrary": "false"}
    )
    h.request("Library/VirtualFolders?" + query, {"LibraryOptions": options}, method="POST", token=token)
    folder = library_status(token, name)
    if folder is None:
        raise GateFailure(f"Jellyfin did not create library {name}.")
    return folder["ItemId"]


def wait_scan_cycle(token, previous_start, timeout=180):
    def started():
        current = task_info(token, "RefreshLibrary")
        start = (current.get("LastExecutionResult") or {}).get("StartTimeUtc")
        return current if current["State"] == "Running" or (start and start != previous_start) else None
    h.wait_for(started, "library scan start", timeout=timeout, interval=0.2)

    def idle():
        current = task_info(token, "RefreshLibrary")
        start = (current.get("LastExecutionResult") or {}).get("StartTimeUtc")
        return current if current["State"] == "Idle" and start and start != previous_start else None
    return h.wait_for(idle, "library scan idle after refresh", timeout=timeout, interval=0.2)


def configure_defaults(token):
    _, settings = h.request("JellyP/Settings", token=token)
    defaults = settings["Defaults"]
    defaults.update({
        "PreserveOriginals": True,
        "Quality": 80,
        "Lossless": False,
        "ResizeImages": False,
        "MaximumDimension": 2400,
        "MinimumSsim": 0.90,
        "BatchSize": 50,
    })
    for policy in defaults["Policies"]:
        policy.update({
            "Enabled": policy["Category"] == "Primary",
            "OverrideGlobal": False,
            "Quality": 80,
            "Lossless": False,
            "ResizeImages": False,
            "MaximumDimension": 2400,
            "MinimumSsim": 0.90,
        })
    globals_ = {
        "MinimumFreeSpaceBytes": 64 * 1024 * 1024,
        "MaximumBackupBytes": 512 * 1024 * 1024,
        "BackupRetentionDays": 0,
        "MinimumSavingsBytes": 0,
        "MinimumSavingsRatio": 0.0,
    }
    h.request("JellyP/Settings/Defaults", {"Defaults": defaults, "Globals": globals_}, method="PUT", token=token)
    _, saved = h.request("JellyP/Settings", token=token)
    if saved.get("DefaultsSource") != "manual":
        raise GateFailure("Manual defaults were not persisted before the host gates.")


def configure_automation(token, item_ids):
    _, settings = h.request("JellyP/Settings", token=token)
    policies = [dict(policy) for policy in settings["Defaults"]["Policies"]]
    for policy in policies:
        policy.update({
            "Enabled": policy["Category"] == "Primary",
            "OverrideGlobal": False,
            "Quality": 80,
            "Lossless": False,
            "ResizeImages": False,
            "MaximumDimension": 2400,
            "MinimumSsim": 0.90,
        })
    automation = {
        "Enabled": True,
        "DryRun": False,
        "PreserveOriginals": True,
        "Quality": 80,
        "Lossless": False,
        "ResizeImages": False,
        "MaximumDimension": 2400,
        "MinimumSsim": 0.90,
        "BatchSize": 50,
        "ScopeMode": "items",
        "ItemIds": item_ids,
        "LibraryIds": [],
        "Policies": policies,
        "SyncUnchangedPolicies": False,
    }
    h.request("JellyP/Settings/Automation", {"Automation": automation, "AuthorizePermanentRemoval": False}, method="PUT", token=token)
    _, saved = h.request("JellyP/Settings", token=token)
    if not saved["Automation"]["Enabled"] or saved["Automation"]["DryRun"]:
        raise GateFailure("Native automation was not saved in applying mode.")


def ensure_fixture_state(root, test_plugin_dll):
    """Create only synthetic fixture media and verify the candidate/test provider."""
    h.stop_host(root)
    installed = h.safe_path(root, "data/plugins/JellyP/" + h.DLL_NAME, base=root, must_exist=True)
    installed_hash = h.sha256(installed)
    test_plugin = install_test_plugin(root, test_plugin_dll)
    # The first library scan must not be held open by the test provider.
    set_slow_delay(root, 0)
    clear_slow_release(root)
    os.environ["JELLYP_TEST_SLOW_FILE"] = str(root / SLOW_CONTROL_NAME)
    os.environ["JELLYP_TEST_SLOW_RELEASE"] = str(root / SLOW_RELEASE_NAME)
    os.environ["JELLYP_TEST_IMAGE_BARRIER"] = str(root / IMAGE_BARRIER_NAME)

    media = h.safe_path(root, "host-gates-media", base=root, must_exist=False)
    specs = {
        "target": [f"Target Artwork {i:02d} (2026)" for i in range(1, 5)],
        "barrier": [f"Slow Refresh Barrier {i:02d} (2026)" for i in range(1, 5)],
        "v15": [f"V15 Artwork {i:02d} (2026)" for i in range(1, 7)],
    }
    for group, names in specs.items():
        for name in names:
            generate_movie(root, f"host-gates-media/{group}/{name}/{name}.mp4")

    h.start_host(root)
    pid, starttime = assert_process_guard(root)
    token, info = login_verified(root)
    h.wait_plugin(token, CANDIDATE_VERSION)
    installed = h.assert_single_plugin_install(root)
    if h.sha256(installed) != installed_hash:
        raise GateFailure("Installed candidate DLL changed during fixture setup.")
    h.assert_loaded_assembly(root, installed)
    h.assert_candidate_page(root, token)
    ensure_test_plugin_active(root, token)
    token, info = login_verified(root)

    libraries = {}
    for name, media_path in (
        (TARGET_LIBRARY, media / "target"),
        (BARRIER_LIBRARY, media / "barrier"),
        (V15_LIBRARY, media / "v15"),
    ):
        libraries[name] = ensure_library(token, name, media_path)
    previous_scan = (task_info(token, "RefreshLibrary").get("LastExecutionResult") or {}).get("StartTimeUtc")
    h.request("Library/Refresh", method="POST", token=token)
    wait_scan_cycle(token, previous_scan)
    set_slow_delay(root, 60)

    groups = {}
    for group, names in specs.items():
        groups[group] = [wait_movie(token, name) for name in names]

    # Target/barrier images are small; V15 images are 4096x4096 to widen the
    # journal Prepared-to-Committed window without a product fault seam. The
    # generated PNG is fixture-private, never a shared /tmp cache.
    small = h.png(21)
    big_path = h.safe_path(root, "host-gates-4096.png", base=root, must_exist=False)
    if not big_path.is_file():
        subprocess.run(
            [
                "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
                "-f", "lavfi", "-i", "testsrc=size=4096x4096:rate=1",
                "-frames:v", "1", str(big_path),
            ],
            check=True,
        )
    os.chmod(big_path, 0o600)
    big = big_path.read_bytes()

    hashes = {}
    for group, items in groups.items():
        payload = big if group == "v15" else small
        for index, item in enumerate(items):
            hashes[item["Id"]] = set_primary_png(root, token, item, payload, f"{group} {index}")["hash"]

    configure_defaults(token)
    state = {
        "schema": 1,
        "createdUtc": now_iso(),
        "root": str(root),
        "hostVersion": info["Version"],
        "programDataPath": info["ProgramDataPath"],
        "pid": pid,
        "starttime": starttime,
        "installedDllSha256": h.sha256(installed),
        "candidateDllSha256": installed_hash,
        "testPlugin": test_plugin,
        "libraries": libraries,
        "items": {group: [{"Id": item["Id"], "Name": item["Name"]} for item in items] for group, items in groups.items()},
        "hashes": hashes,
        "settings": "manual defaults Primary-only, preserve originals, 64MiB reserve",
    }
    h.write_private(h.safe_path(root, STATE_NAME, base=root), json.dumps(state, indent=2) + "\n")
    print(f"setup: {len(groups['target'])} target, {len(groups['barrier'])} barrier, {len(groups['v15'])} V15 items; candidate {installed_hash}")
    return state


def load_state(root):
    path = h.safe_path(root, STATE_NAME, base=root, must_exist=True)
    state = json.loads(path.read_text())
    if Path(state.get("root", "")).resolve() != root.resolve():
        raise GateFailure("Host gate state belongs to another root.")
    return state


def select_ordered_pair(target_items, barrier_items):
    """Pick a target-library item whose key sorts before a barrier-library item."""
    targets = sorted(target_items, key=lambda item: h.normalized(item["Id"]))
    barriers = sorted(barrier_items, key=lambda item: h.normalized(item["Id"]))
    for target in targets:
        for barrier in barriers:
            if h.normalized(target["Id"]) < h.normalized(barrier["Id"]):
                return target, barrier
    raise GateFailure("No target/barrier item pair has the required transaction order.")


def check(evidence, truth, label, detail=None):
    if not truth:
        evidence["checks"].append("FAIL " + label)
        raise GateFailure(label)
    evidence["checks"].append(label)
    print("PASS " + label)


def run_v08_pre_mutation(root, token, state, evidence):
    item = state["items"]["barrier"][0]
    library_id = state["libraries"][BARRIER_LIBRARY]
    before = primary_info(root, token, item["Id"])
    check(evidence, before["hash"] == state_hash(state, item["Id"]), "V08a target starts as the exact synthetic PNG")
    set_slow_delay(root, 20)
    clear_slow_release(root)
    trigger_library_refresh(token, library_id)
    wait_library_status(token, BARRIER_LIBRARY, "Active", timeout=30)
    plan = analyze_plan(token, [item["Id"]])
    check(evidence, plan["Proposed"] >= 1, "V08a analysis proposes a real mutation")
    keys = plan_keys(token, plan["PlanId"])
    receipt = apply_plan(token, plan["PlanId"], keys)
    waiting = wait_operation(
        token, receipt["OperationId"],
        lambda detail: detail["Operation"]["Phase"] == "WaitingForMetadata" and not detail["Operation"]["MutationStarted"],
        "V08a WaitingForMetadata before mutation", timeout=30,
    )
    check(evidence, plan_status(token, plan["PlanId"])["Status"] == "Reserved", "V08a plan is Reserved while waiting")
    h.request("JellyP/Executions/" + receipt["OperationId"] + "/Cancel", method="POST", token=token)
    terminal = wait_operation_terminal(token, receipt["OperationId"], timeout=60)
    op = terminal["Operation"]
    after = primary_info(root, token, item["Id"])
    records = journal_for_operation(root, receipt["OperationId"])
    check(evidence, op["State"] == "Cancelled" and op["Phase"] == "Cancelled", "V08a endpoint cancel reaches terminal Cancelled")
    check(evidence, op["MutationStarted"] is False and op["Changed"] == 0, "V08a no real mutation started")
    check(evidence, op["NotStarted"] >= 1, "V08a no next transaction was started")
    check(evidence, plan_status(token, plan["PlanId"])["Status"] == "Ready", "V08a plan returns to Ready before first mutation")
    check(evidence, after["hash"] == before["hash"], "V08a synthetic artwork bytes are unchanged")
    check(evidence, records == [], "V08a no journal was written")
    release_slow_provider(root)
    wait_library_status(token, BARRIER_LIBRARY, "Idle", timeout=60)
    evidence["v08"]["preMutation"] = {
        "operationId": receipt["OperationId"],
        "planId": plan["PlanId"],
        "observedPhase": waiting["Operation"]["Phase"],
        "terminalState": op["State"],
        "mutationStarted": op["MutationStarted"],
        "changed": op["Changed"],
        "notStarted": op["NotStarted"],
        "planAfter": plan_status(token, plan["PlanId"])["Status"],
        "artworkHashBefore": before["hash"],
        "artworkHashAfter": after["hash"],
        "journalCount": len(records),
    }


def run_v08_post_mutation(root, token, state, evidence):
    first, second = select_ordered_pair(state["items"]["target"], state["items"]["barrier"])
    first_before = primary_info(root, token, first["Id"])
    second_before = primary_info(root, token, second["Id"])
    check(evidence, first_before["hash"] == state_hash(state, first["Id"]), "V08b first item starts as exact synthetic PNG")
    check(evidence, second_before["hash"] == state_hash(state, second["Id"]), "V08b second item starts as exact synthetic PNG")
    set_slow_delay(root, 35)
    clear_slow_release(root)
    trigger_library_refresh(token, state["libraries"][BARRIER_LIBRARY])
    wait_library_status(token, BARRIER_LIBRARY, "Active", timeout=30)
    plan = analyze_plan(token, [first["Id"], second["Id"]])
    check(evidence, plan["Proposed"] >= 2, "V08b analysis proposes both transactions")
    keys = plan_keys(token, plan["PlanId"])
    receipt = apply_plan(token, plan["PlanId"], keys)
    observed = wait_operation(
        token, receipt["OperationId"],
        lambda detail: detail["Operation"]["Changed"] >= 1 and detail["Operation"]["Phase"] == "WaitingForMetadata",
        "V08b mutation then WaitingForMetadata", timeout=60,
    )
    op = observed["Operation"]
    first_mid = primary_info(root, token, first["Id"])
    second_mid = primary_info(root, token, second["Id"])
    plan_mid = plan_status(token, plan["PlanId"])["Status"]
    records_mid = journal_for_operation(root, receipt["OperationId"])
    first_journal = committed_output(root, first["Id"], first_before["hash"], first_mid["hash"])
    check(evidence, op["MutationStarted"] is True and op["Changed"] >= 1, "V08b first transaction reached a real mutation")
    check(evidence, plan_mid == "Consumed", "V08b plan is Consumed after the first mutation")
    check(evidence, first_mid["hash"] != first_before["hash"], "V08b first artwork changed to verified output")
    check(evidence, second_mid["hash"] == second_before["hash"], "V08b second artwork is still unchanged while waiting")
    check(evidence, len(records_mid) >= 1, "V08b first journal exists before cancellation")
    h.request("JellyP/Executions/" + receipt["OperationId"] + "/Cancel", method="POST", token=token)
    terminal = wait_operation_terminal(token, receipt["OperationId"], timeout=60)
    op = terminal["Operation"]
    second_after = primary_info(root, token, second["Id"])
    records_after = journal_for_operation(root, receipt["OperationId"])
    second_journal = journal_for_item(root, second["Id"])
    check(evidence, op["State"] == "Cancelled", "V08b endpoint cancel reaches terminal Cancelled")
    check(evidence, op["Changed"] >= 1 and op["NotStarted"] >= 1, "V08b no next transaction was started")
    check(evidence, plan_status(token, plan["PlanId"])["Status"] == "Consumed", "V08b plan stays Consumed after cancellation")
    check(evidence, second_after["hash"] == second_before["hash"], "V08b second artwork remains byte-identical")
    check(evidence, len(records_after) == len(records_mid), "V08b cancellation wrote no second journal")
    check(evidence, second_journal == [], "V08b second item has no journal")
    release_slow_provider(root)
    wait_library_status(token, BARRIER_LIBRARY, "Idle", timeout=90)
    evidence["v08"]["postMutation"] = {
        "operationId": receipt["OperationId"],
        "planId": plan["PlanId"],
        "firstItem": first["Name"],
        "secondItem": second["Name"],
        "observedPhase": observed["Operation"]["Phase"],
        "terminalState": op["State"],
        "mutationStarted": op["MutationStarted"],
        "changed": op["Changed"],
        "notStarted": op["NotStarted"],
        "planMid": plan_mid,
        "planAfter": plan_status(token, plan["PlanId"])["Status"],
        "firstHashBefore": first_before["hash"],
        "firstHashAfter": first_mid["hash"],
        "secondHashBefore": second_before["hash"],
        "secondHashAfter": second_after["hash"],
        "journalCount": len(records_after),
        "journalIds": [entry["record"]["Id"] for entry in records_after],
        "firstJournalId": first_journal.get("Id"),
        "firstJournalSourceHash": first_journal.get("SourceHash"),
        "firstJournalOutputHash": first_journal.get("OutputHash"),
        "firstJournalState": first_journal.get("State"),
    }


def reset_safe_automation(root, token):
    """Persist preview mode and verify it; re-authenticate once after a restart."""
    def apply(current_token):
        _, settings = h.request("JellyP/Settings", token=current_token)
        automation = dict(settings["Automation"])
        automation.update({
            "Enabled": False,
            "DryRun": True,
            "PreserveOriginals": True,
            "ScopeMode": "legacy",
            "ItemIds": [],
            "LibraryIds": [],
        })
        h.request("JellyP/Settings/Automation", {"Automation": automation, "AuthorizePermanentRemoval": False},
                  method="PUT", token=current_token)
        _, saved = h.request("JellyP/Settings", token=current_token)
        saved_automation = saved.get("Automation", {})
        if (saved_automation.get("Enabled") is not False
                or saved_automation.get("DryRun") is not True
                or saved_automation.get("PreserveOriginals") is not True
                or saved_automation.get("ScopeMode") != "legacy"
                or saved_automation.get("ItemIds") not in ([], None)
                or saved_automation.get("LibraryIds") not in ([], None)):
            raise GateFailure("Safe automation readback did not match preview mode.")
        set_task_triggers(current_token, "JellyPOptimizeArtwork", [])
        task = task_info(current_token, "JellyPOptimizeArtwork")
        if task.get("Triggers") != []:
            raise GateFailure("Safe automation readback still has an Optimize trigger.")
        _, status = h.request("JellyP/Status", token=current_token)
        if status.get("PendingRecovery") != 0:
            raise GateFailure(f"Safe automation readback reports PendingRecovery={status.get('PendingRecovery')}.")
        return {
            "enabled": saved_automation.get("Enabled"),
            "dryRun": saved_automation.get("DryRun"),
            "preserveOriginals": saved_automation.get("PreserveOriginals"),
            "scopeMode": saved_automation.get("ScopeMode"),
            "triggers": task.get("Triggers"),
            "pendingRecovery": status.get("PendingRecovery"),
        }

    try:
        return apply(token)
    except RuntimeError as error:
        message = str(error)
        if "HTTP 401" not in message and "HTTP 403" not in message:
            raise
        fresh_token, _ = login_verified(root)
        return apply(fresh_token)


def ensure_task_has_last_result(token):
    if task_info(token, "JellyPOptimizeArtwork").get("LastExecutionResult"):
        return
    h.request("JellyP/Run?dryRun=true", method="POST", token=token)
    wait_report_terminal(token, timeout=180)
    if not task_info(token, "JellyPOptimizeArtwork").get("LastExecutionResult"):
        raise GateFailure("Native Optimize task has no LastExecutionResult; interval scheduling would wait an hour.")


def run_v10_related(root, token, state, evidence):
    item = first_fresh_item(root, token, state, "barrier")
    before = primary_info(root, token, item["Id"])
    ensure_task_has_last_result(token)
    wait_task_idle(token, "JellyPOptimizeArtwork", timeout=60)
    configure_automation(token, [item["Id"]])
    set_slow_delay(root, 90)
    clear_slow_release(root)
    previous = (task_info(token, "JellyPOptimizeArtwork").get("LastExecutionResult") or {}).get("StartTimeUtc")
    previous_execution = (h.request("JellyP/Status", token=token)[1].get("LastRun", {}) or {}).get("ExecutionId")
    trigger_library_refresh(token, state["libraries"][BARRIER_LIBRARY])
    wait_library_status(token, BARRIER_LIBRARY, "Active", timeout=30)
    active_at = utc_stamp()
    active_mono = time.monotonic()
    set_task_triggers(token, "JellyPOptimizeArtwork", [{"Type": "IntervalTrigger", "IntervalTicks": 10000000}])
    trigger_at = utc_stamp()
    trigger_mono = time.monotonic()
    started = wait_scheduler_running(token, "JellyPOptimizeArtwork", previous, timeout=150)
    started_at = utc_stamp()
    started_mono = time.monotonic()
    waiting = wait_report_phase(token, "WaitingForMetadata", timeout=30)
    waiting_at = utc_stamp()
    waiting_report = waiting["LastRun"]
    waiting_execution = waiting_report.get("ExecutionId")
    check(evidence, waiting_execution is not None, "V10 related waiting report has an executionId")
    during = primary_info(root, token, item["Id"])
    records_during = journal_for_item(root, item["Id"])
    check(evidence, active_mono < trigger_mono < started_mono,
          "V10 related refresh was Active before trigger install and scheduler start")
    check(evidence, waiting_report.get("Running") is True and waiting_report.get("Phase") == "WaitingForMetadata",
          "V10 related scheduler is waiting while refresh is Active")
    check(evidence, during["hash"] == before["hash"], "V10 related refresh blocks before any mutation")
    check(evidence, not records_during, "V10 related wait wrote no journal")
    check(evidence, not waiting_report.get("FinishedUtc"), "V10 related run had not finished while Active")

    # Explicitly release the controlled provider while the report still belongs
    # to the same running execution. The refresh ending is observed by status,
    # not by an arbitrary sleep.
    release_slow_provider(root)
    release_at = utc_stamp()
    release_mono = time.monotonic()
    phases_after_release = []
    while True:
        folder = library_status(token, BARRIER_LIBRARY)
        _, current = h.request("JellyP/Status", token=token)
        report = current.get("LastRun", {})
        if report.get("ExecutionId") != waiting_execution:
            raise GateFailure("V10 related execution identity changed while releasing the refresh.")
        if report.get("Running") is True:
            phases_after_release.append(report.get("Phase"))
        if report.get("Running") is not True or report.get("Phase") in ("Completed", "CompletedWithErrors", "Failed", "Cancelled", "Interrupted"):
            raise GateFailure("V10 related task finished while the refresh was still Active.")
        if folder is not None and folder.get("RefreshStatus") == "Idle":
            idle_at = utc_stamp()
            idle_mono = time.monotonic()
            break
        time.sleep(0.05)
    check(evidence, release_mono < idle_mono, "V10 related provider release preceded observed refresh Idle")
    check(evidence, bool(phases_after_release) and all(
        phase in ("WaitingForMetadata", "Processing") for phase in phases_after_release),
        "V10 related execution stayed on the same run through Idle")
    check(evidence, report.get("ExecutionId") == waiting_execution,
          "V10 related executionId was stable until refresh Idle")
    terminal = wait_report_terminal(token, timeout=150, exclude_execution=previous_execution)
    terminal_at = utc_stamp()
    terminal_mono = time.monotonic()
    report = terminal["LastRun"]
    after = primary_info(root, token, item["Id"])
    journal = committed_output(root, item["Id"], before["hash"], after["hash"])
    finished = parse_utc(report.get("FinishedUtc"))
    idle_time = parse_utc(idle_at)
    check(evidence, report.get("ExecutionId") == waiting_execution, "V10 related resumed with the same executionId")
    check(evidence, finished is not None and idle_time is not None and finished >= idle_time,
          "V10 related finished after the refresh became Idle")
    check(evidence, terminal_mono > idle_mono, "V10 related terminal observation followed Idle")
    check(evidence, report["Phase"] == "Completed" and report.get("Converted", 0) >= 1,
          "V10 related task resumed and converted after refresh")
    check(evidence, after["hash"] != before["hash"], "V10 related artwork changed after resume")
    check(evidence, journal.get("State") == "Committed", "V10 related run has a committed WebP journal")
    set_task_triggers(token, "JellyPOptimizeArtwork", [])
    wait_task_idle(token, "JellyPOptimizeArtwork", timeout=30)
    evidence["v10"]["related"] = {
        "activeAt": active_at,
        "triggerInstalledAt": trigger_at,
        "schedulerObservedAt": started_at,
        "waitingObservedAt": waiting_at,
        "releasedAt": release_at,
        "idleObservedAt": idle_at,
        "terminalObservedAt": terminal_at,
        "schedulerStartUtc": report.get("StartedUtc"),
        "executionId": report.get("ExecutionId"),
        "sameExecutionId": report.get("ExecutionId") == waiting_execution,
        "refreshWasActiveBeforeStart": active_mono < trigger_mono < started_mono,
        "phasesAfterRelease": phases_after_release,
        "observedPhase": waiting_report.get("Phase"),
        "converted": report.get("Converted"),
        "finishedUtc": report.get("FinishedUtc"),
        "journalCompletedUtc": journal.get("CompletedUtc"),
        "artworkHashBefore": before["hash"],
        "artworkHashAfter": after["hash"],
        "journalState": journal.get("State"),
        "journalId": journal.get("Id"),
        "journalSourceHash": journal.get("SourceHash"),
        "journalOutputHash": journal.get("OutputHash"),
    }


def run_v10_unrelated(root, token, state, evidence):
    item = first_fresh_item(root, token, state, "target")
    before = primary_info(root, token, item["Id"])
    witness = first_fresh_item(root, token, state, "barrier")
    witness_before = primary_info(root, token, witness["Id"])
    wait_task_idle(token, "JellyPOptimizeArtwork", timeout=60)
    configure_automation(token, [item["Id"]])
    set_slow_delay(root, 90)
    clear_slow_release(root)
    previous = (task_info(token, "JellyPOptimizeArtwork").get("LastExecutionResult") or {}).get("StartTimeUtc")
    previous_execution = (h.request("JellyP/Status", token=token)[1].get("LastRun", {}) or {}).get("ExecutionId")
    trigger_library_refresh(token, state["libraries"][BARRIER_LIBRARY])
    wait_library_status(token, BARRIER_LIBRARY, "Active", timeout=30)
    active_at = utc_stamp()
    active_mono = time.monotonic()
    set_task_triggers(token, "JellyPOptimizeArtwork", [{"Type": "IntervalTrigger", "IntervalTicks": 10000000}])
    trigger_at = utc_stamp()
    trigger_mono = time.monotonic()
    started = wait_scheduler_running(token, "JellyPOptimizeArtwork", previous, timeout=150)
    started_at = utc_stamp()
    started_mono = time.monotonic()
    saw_waiting = False
    commit_at = None
    commit_mono = None
    barrier_status_at_commit = None
    journal_at_commit = None
    deadline = time.monotonic() + 150
    while time.monotonic() < deadline:
        folder = library_status(token, BARRIER_LIBRARY)
        records = journal_for_item(root, item["Id"])
        committed = [entry["record"] for entry in records if entry["record"].get("State") == "Committed"]
        _, status = h.request("JellyP/Status", token=token)
        report = status.get("LastRun", {})
        if previous_execution and report.get("ExecutionId") == previous_execution:
            time.sleep(0.01)
            continue
        if report.get("Phase") == "WaitingForMetadata":
            saw_waiting = True
        if committed:
            commit_at = utc_stamp()
            commit_mono = time.monotonic()
            barrier_status_at_commit = folder.get("RefreshStatus") if folder is not None else None
            journal_at_commit = committed[0]
            break
        if report.get("Phase") in ("Completed", "CompletedWithErrors", "Failed", "Cancelled", "Interrupted"):
            # The report can be finalized a few milliseconds before the durable
            # journal rename is visible. Re-read the journal while the controlled
            # barrier is still Active instead of treating that as a lost event.
            commit_deadline = time.monotonic() + 3
            while time.monotonic() < commit_deadline:
                folder = library_status(token, BARRIER_LIBRARY)
                records = journal_for_item(root, item["Id"])
                committed = [entry["record"] for entry in records if entry["record"].get("State") == "Committed"]
                if committed:
                    commit_at = utc_stamp()
                    commit_mono = time.monotonic()
                    barrier_status_at_commit = folder.get("RefreshStatus") if folder is not None else None
                    journal_at_commit = committed[0]
                    break
                if folder is None or folder.get("RefreshStatus") != "Active":
                    break
                time.sleep(0.01)
            if journal_at_commit is not None:
                break
            raise GateFailure("V10 unrelated run finished without an observed committed journal.")
        time.sleep(0.01)
    if journal_at_commit is None:
        raise GateFailure("V10 unrelated did not observe a committed output while the barrier was Active.")
    check(evidence, active_mono < trigger_mono < started_mono,
          "V10 unrelated refresh was Active before trigger install and scheduler start")
    check(evidence, barrier_status_at_commit == "Active",
          "V10 unrelated output was Committed while the unrelated refresh was Active")
    release_slow_provider(root)
    release_at = utc_stamp()
    release_mono = time.monotonic()
    wait_library_status(token, BARRIER_LIBRARY, "Idle", timeout=150)
    idle_at = utc_stamp()
    idle_mono = time.monotonic()
    terminal = wait_report_terminal(token, timeout=60, exclude_execution=previous_execution)
    terminal_at = utc_stamp()
    report = terminal["LastRun"]
    after = primary_info(root, token, item["Id"])
    witness_after = primary_info(root, token, witness["Id"])
    journal = committed_output(root, item["Id"], before["hash"], after["hash"])
    finished = parse_utc(report.get("FinishedUtc"))
    journal_completed = parse_utc(journal.get("CompletedUtc"))
    check(evidence, commit_mono < release_mono < idle_mono,
          "V10 unrelated commit preceded explicit provider release and observed Idle")
    check(evidence, finished is not None and journal_completed is not None and finished >= journal_completed,
          "V10 unrelated completion is not earlier than its committed journal")
    check(evidence, report.get("ExecutionId") != previous_execution, "V10 unrelated produced a new execution")
    check(evidence, saw_waiting is False, "V10 unrelated refresh never produced WaitingForMetadata")
    check(evidence, report["Phase"] == "Completed" and report.get("Converted", 0) >= 1,
          "V10 unrelated task completed and converted")
    check(evidence, after["hash"] != before["hash"], "V10 unrelated target artwork changed")
    check(evidence, journal.get("OutputHash") == after["hash"], "V10 unrelated committed hash matches active WebP")
    check(evidence, witness_after["hash"] == witness_before["hash"],
          "V10 unrelated library artwork remained untouched")
    set_task_triggers(token, "JellyPOptimizeArtwork", [])
    wait_task_idle(token, "JellyPOptimizeArtwork", timeout=30)
    evidence["v10"]["unrelated"] = {
        "activeAt": active_at,
        "triggerInstalledAt": trigger_at,
        "schedulerObservedAt": started_at,
        "commitObservedAt": commit_at,
        "releasedAt": release_at,
        "idleObservedAt": idle_at,
        "terminalObservedAt": terminal_at,
        "schedulerStartUtc": report.get("StartedUtc"),
        "executionId": report.get("ExecutionId"),
        "previousExecutionId": previous_execution,
        "refreshWasActiveBeforeStart": active_mono < trigger_mono < started_mono,
        "sawWaitingForMetadata": saw_waiting,
        "barrierRefreshActiveAtCommit": barrier_status_at_commit == "Active",
        "barrierStatusAtCommit": barrier_status_at_commit,
        "converted": report.get("Converted"),
        "finishedUtc": report.get("FinishedUtc"),
        "journalCompletedUtc": journal.get("CompletedUtc"),
        "artworkHashBefore": before["hash"],
        "artworkHashAfter": after["hash"],
        "journalState": journal.get("State"),
        "journalId": journal.get("Id"),
        "journalSourceHash": journal.get("SourceHash"),
        "journalOutputHash": journal.get("OutputHash"),
    }


def v15_prekill_proof(record, output, source, backup, expected_source_hash):
    """Return ordered proof conditions for the pre-SIGKILL state.

    The caller raises on any false condition. Keeping the conditions in one
    helper lets the release suite reject an output that never existed, a
    Committed journal, or a hash mismatch instead of accepting a weaker
    Prepared-only observation.
    """
    output_hash = str(record.get("OutputHash", "")).upper()
    source_hash = str(record.get("SourceHash", "")).upper()
    output_path = record.get("OutputPath")
    return [
        ("V15 output exists before SIGKILL", output["exists"]),
        ("V15 output hash matches the Prepared journal OutputHash before SIGKILL",
         output["exists"] and output["hash"] == output_hash),
        ("V15 barrier reached the journal OutputPath",
         output["exists"] and output_path is not None and output.get("path") is not None
         and str(Path(output_path).resolve()) == str(Path(output["path"]).resolve())),
        ("V15 journal was Prepared before SIGKILL", record.get("State") == "Prepared"),
        ("V15 original backup still exists at interruption", record.get("BackupDeleted") is False),
        ("V15 retained original exists before kill", backup["exists"]),
        ("V15 retained original has the exact synthetic source hash before kill",
         backup["exists"] and backup["hash"] == expected_source_hash.upper()),
        ("V15 original source still has its exact bytes before kill",
         source["exists"] and source["hash"] == source_hash),
    ]


def run_v15(root, token, state, evidence, expected_dll_sha256):
    items = state["items"]["v15"]
    before = {item["Id"]: primary_info(root, token, item["Id"]) for item in items}
    for item in items:
        check(evidence, before[item["Id"]]["hash"] == state_hash(state, item["Id"]),
              f"V15 {item['Name']} starts as exact synthetic PNG")
    plan = analyze_plan(token, [item["Id"] for item in items])
    check(evidence, plan["Proposed"] >= len(items), "V15 analysis proposes every synthetic item")
    keys = plan_keys(token, plan["PlanId"])
    journal_dir = h.safe_path(root, "data/plugins/Jellyfin.Plugin.JellyP/migration/journal", base=root, must_exist=True)
    barrier, reached, release, armed_folders = arm_image_barrier(
        root, [entry["path"] for entry in before.values()],
    )
    watcher = subprocess.Popen(
        ["inotifywait", "-m", "-q", "-e", "moved_to", "--format", "%f", str(journal_dir)],
        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, bufsize=0,
    )
    control = None
    kill_evidence = None
    try:
        pid, starttime = assert_process_guard(root)
        control = PidfdControl.open(root, pid, starttime)
        receipt = apply_plan(token, plan["PlanId"], keys)
        opid = receipt["OperationId"]
        op_count_before = len(h.request("JellyP/Executions?limit=100", token=token)[1].get("Items", []))
        reached_path = h.wait_for(
            lambda: reached.read_text().strip() if reached.is_file() else None,
            "V15 test image barrier after real output write", timeout=60, interval=0.02,
        )
        # The fixture plugin is blocked in the real ImageSaver completion callback.
        # Freeze the whole verified process before reading the on-disk state.
        control.stop()
        output_before = file_probe(reached_path)
        prepared = [
            entry for entry in journal_for_operation(root, opid)
            if entry["record"].get("State") == "Prepared"
        ]
        check(evidence, len(prepared) == 1, "V15 exactly one Prepared journal exists at the barrier")
        record_path = prepared[0]["path"]
        record_before = prepared[0]["record"]
        source_before = file_probe(record_before.get("Source", {}).get("Path"))
        backup_path = h.safe_path(
            root,
            "data/plugins/Jellyfin.Plugin.JellyP/migration/originals/"
            + h.normalized(record_before.get("Id", "")) + ".original",
            base=root, must_exist=False,
        )
        backup_before = file_probe(backup_path)
        observed_events = []
        buffer = b""
        drain_deadline = time.monotonic() + 1
        while time.monotonic() < drain_deadline:
            ready, _, _ = select.select([watcher.stdout], [], [], 0.05)
            if not ready:
                break
            chunk = os.read(watcher.stdout.fileno(), 4096)
            if not chunk:
                break
            buffer, lines = inotify_event_names(buffer, chunk)
            observed_events.extend(lines)
        output_hash = str(record_before.get("OutputHash", "")).upper()
        for label, truth in v15_prekill_proof(
                record_before, output_before, source_before, backup_before,
                state_hash(state, record_before["Source"]["ItemId"])):
            check(evidence, truth, label)
        inotify_prepared_observed = record_path.name in observed_events
        op_before = operation_file(root, opid) or {}
        check(evidence, op_before.get("MutationStarted") is True,
              "V15 MutationStarted was durable before SIGKILL")
        control.kill()
        kill_evidence = {
            "pid": pid,
            "starttime": starttime,
            "signal": "SIGKILL",
            "pidfd": True,
            "barrierBlocked": True,
            "stopSent": True,
            "killSent": True,
        }
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline and (h.process_alive(pid) or h.port_open()):
            time.sleep(0.05)
        if h.process_alive(pid) or h.port_open():
            raise GateFailure("V15 verified fixture process or socket survived SIGKILL.")
        pid_file = h.safe_path(root, "host.pid", base=root)
        if pid_file.is_file() and pid_file.read_text().strip() == str(pid):
            pid_file.unlink()
        disarm_image_barrier(root)
        journal_count_before = len(journal_for_operation(root, opid))
        removal_case = "prepared-with-output-before-original-removal"

        restarted = restart_same_candidate(
            root, expected_dll_sha256, previous_identity=(pid, starttime), require_test_plugin=True,
        )
        token = restarted["token"]
        _, status = h.request("JellyP/Status", token=token)
        detail = operation_detail(token, opid)
        op_after = detail["Operation"]
        record_after = json.loads(record_path.read_text())
        output_after_restart = file_probe(record_after.get("OutputPath"))
        journal_count_after = len(journal_for_operation(root, opid))
        op_count_after = len(h.request("JellyP/Executions?limit=100", token=token)[1].get("Items", []))
        check(evidence, restarted["pid"] != pid and restarted["starttime"] != starttime,
              "V15 restart produced a new PID/starttime identity")
        check(evidence, op_after["State"] == "Interrupted", "V15 restart marks the operation Interrupted")
        check(evidence, op_after["MutationStarted"] is True, "V15 restart preserves MutationStarted")
        check(evidence, status.get("PendingRecovery") == 1, "V15 restart reports exactly one pending recovery")
        check(evidence, record_after.get("State") in ("Prepared", "Restoring", "RecoveryRequired"),
              "V15 interrupted journal still requires recovery")
        check(evidence, journal_count_after == journal_count_before, "V15 restart did not replay or add a journal")
        check(evidence, op_count_after == op_count_before, "V15 restart did not create a replay operation")
        check(evidence, op_after["State"] == "Interrupted", "V15 no automatic replay after restart")
        check(evidence, output_after_restart["exists"] and output_after_restart["hash"] == output_hash,
              "V15 restart left the pre-kill output untouched")

        backup = h.safe_path(
            root,
            "data/plugins/Jellyfin.Plugin.JellyP/migration/originals/" + h.normalized(record_after["Id"]) + ".original",
            base=root, must_exist=True,
        )
        original_hash = h.sha256(backup)
        check(evidence, original_hash == state_hash(state, record_after["Source"]["ItemId"]),
              "V15 retained original has the exact synthetic source hash")
        previous_recovery_execution = (h.request("JellyP/Status", token=token)[1].get("LastRun", {}) or {}).get("ExecutionId")
        h.request("JellyP/Rollback/" + record_after["Id"], method="POST", token=token)
        recovered = wait_report_terminal(token, timeout=120, exclude_execution=previous_recovery_execution)
        report = recovered["LastRun"]
        record_recovered = json.loads(record_path.read_text())
        _, recovered_status = h.request("JellyP/Status", token=token)
        active = primary_info(root, token, record_after["Source"]["ItemId"])
        output_after_recovery = file_probe(record_after.get("OutputPath"))
        journal_count_after_recovery = len(journal_for_operation(root, opid))
        output_removal_proven = bool(output_before["exists"] and not output_after_recovery["exists"])
        check(evidence, report["Phase"] == "Completed", "V15 explicit recovery completed")
        check(evidence, record_recovered.get("State") == "RolledBack", "V15 journal is RolledBack after recovery")
        check(evidence, record_recovered.get("BackupDeleted") is True, "V15 backup is deleted after recovery")
        check(evidence, recovered_status.get("PendingRecovery") == 0, "V15 recovery leaves no pending recovery")
        check(evidence, active["hash"] == state_hash(state, record_after["Source"]["ItemId"]),
              "V15 restored artwork is byte-exact synthetic PNG")
        check(evidence, record_recovered.get("SourceHash", "").upper() == state_hash(state, record_after["Source"]["ItemId"]),
              "V15 recovered journal keeps the exact owned source hash")
        check(evidence, output_after_recovery["exists"] is False,
              "V15 recovery leaves no output at the recorded path")
        check(evidence, journal_count_after_recovery == journal_count_before,
              "V15 recovery did not add a journal")
        check(evidence, output_removal_proven, "V15 removed the WebP that existed before kill")
        check(evidence, operation_detail(token, opid)["Operation"]["State"] == "Interrupted",
              "V15 explicit recovery did not rewrite the interrupted operation")
        evidence["v15"] = {
            "operationId": opid,
            "planId": plan["PlanId"],
            "killedPid": pid,
            "killedStarttime": starttime,
            "killGuard": kill_evidence,
            "barrier": {
                "path": str(barrier),
                "reached": str(reached),
                "release": str(release),
                "reachedPath": reached_path,
                "armedFolders": armed_folders,
            },
            "inotifyPreparedEventObserved": inotify_prepared_observed,
            "inotifyEvents": observed_events,
            "removalCase": removal_case,
            "sourceBeforeKill": source_before,
            "outputBeforeKill": output_before,
            "backupBeforeKill": backup_before,
            "outputAfterRestart": output_after_restart,
            "outputAfterRecovery": output_after_recovery,
            "outputRemovalProven": output_removal_proven,
            "journalId": record_before["Id"],
            "journalStateAtKill": record_before["State"],
            "journalStateAfterRestart": record_after["State"],
            "journalStateAfterRecovery": record_recovered.get("State"),
            "backupDeletedAfterRecovery": record_recovered.get("BackupDeleted"),
            "originalHash": original_hash,
            "restoredHash": active["hash"],
            "outputPathAbsent": not output_after_recovery["exists"],
            "pendingRecoveryAfterRestart": status.get("PendingRecovery"),
            "pendingRecoveryAfterRecovery": recovered_status.get("PendingRecovery"),
            "operationStateAfterRestart": op_after["State"],
            "operationStateAfterRecovery": operation_detail(token, opid)["Operation"]["State"],
            "journalCountBeforeRestart": journal_count_before,
            "journalCountAfterRestart": journal_count_after,
            "journalCountAfterRecovery": journal_count_after_recovery,
            "operationCountBeforeRestart": op_count_before,
            "operationCountAfterRestart": op_count_after,
            "restartedPid": restarted["pid"],
            "restartedStarttime": restarted["starttime"],
            "restartedDllSha256": restarted["installedSha256"],
            "restartedTestPlugin": restarted.get("testPlugin"),
        }
        return token
    finally:
        release_image_barrier(release)
        disarm_image_barrier(root)
        if control is not None:
            control.close()
        watcher.kill()
        try:
            watcher.wait(timeout=5)
        except Exception:
            pass


def run_gates(root, state, expected_dll_sha256, scenarios=("v08", "v10", "v15")):
    os.environ["JELLYP_TEST_SLOW_FILE"] = str(root / SLOW_CONTROL_NAME)
    os.environ["JELLYP_TEST_SLOW_RELEASE"] = str(root / SLOW_RELEASE_NAME)
    os.environ["JELLYP_TEST_IMAGE_BARRIER"] = str(root / IMAGE_BARRIER_NAME)
    token, info = login_verified(root)
    installed = h.safe_path(root, "data/plugins/JellyP/" + h.DLL_NAME, base=root, must_exist=True)
    installed_hash = h.sha256(installed)
    if installed_hash != expected_dll_sha256.upper():
        raise GateFailure("Running fixture does not have the expected candidate DLL.")
    ensure_test_plugin_active(root, token)
    token, info = login_verified(root)
    evidence = {
        "schema": 1,
        "createdUtc": now_iso(),
        "head": subprocess.run(["git", "rev-parse", "HEAD"], cwd=REPO, check=True, text=True, capture_output=True).stdout.strip(),
        "root": str(root),
        "hostVersion": info["Version"],
        "programDataPath": info["ProgramDataPath"],
        "candidateDllSha256": expected_dll_sha256.upper(),
        "installedDllSha256": installed_hash,
        "guards": {
            "markedRoot": True,
            "loopbackOnly": True,
            "exactArgvAndCwd": True,
            "ownerUid": os.getuid(),
            "starttimeCheckedBeforeKill": True,
            "pidfdUsedForStopKillCont": True,
            "loadedAssembly": str(installed),
        },
        "v08": {},
        "v10": {},
        "v15": {},
        "checks": [],
        "limitations": [
            "The slow metadata provider and image-save barrier are test-only fixture plugins, not product code.",
            "The V15 kill window comes from a real image save held by the fixture barrier, not a product fault endpoint.",
            "No physical disk was filled and no OS permissions or services were changed.",
            "This run does not repeat the historical 121/819 UI journey or planning checks.",
        ],
    }
    error = None
    cleanup_error = None
    try:
        if "v08" in scenarios:
            run_v08_pre_mutation(root, token, state, evidence)
            run_v08_post_mutation(root, token, state, evidence)
        if "v10" in scenarios:
            run_v10_related(root, token, state, evidence)
            run_v10_unrelated(root, token, state, evidence)
        if "v15" in scenarios:
            token = run_v15(root, token, state, evidence, expected_dll_sha256)
    except BaseException as failure:
        error = failure
        evidence["error"] = f"{type(failure).__name__}: {failure}"
    try:
        evidence["cleanup"] = reset_safe_automation(root, token)
    except BaseException as failure:
        cleanup_error = failure
        evidence["cleanupError"] = f"{type(failure).__name__}: {failure}"
    finally:
        h.write_private(h.safe_path(root, EVIDENCE_NAME, base=root), json.dumps(evidence, indent=2) + "\n")
    if error is not None:
        if cleanup_error is not None:
            try:
                error.add_note(f"safe cleanup failed: {cleanup_error}")
            except AttributeError:
                print(f"safe cleanup failed: {cleanup_error}", file=sys.stderr)
        raise error
    if cleanup_error is not None:
        raise cleanup_error
    return evidence


def stop_and_verify(root):
    h.stop_host(root)
    if h.port_open():
        raise GateFailure("Port 18096 remained open after fixture stop.")
    pid_file = h.safe_path(root, "host.pid", base=root, must_exist=False)
    return {"stopped": True, "portFree": True, "pidFilePresent": pid_file.exists()}


def write_final_evidence(root, final, error=None):
    path = h.safe_path(root, EVIDENCE_NAME, base=root, must_exist=False)
    if path.is_file():
        evidence = json.loads(path.read_text())
    else:
        evidence = {"schema": 1, "root": str(root), "checks": []}
    evidence["final"] = final
    if error is not None:
        evidence.setdefault("errors", []).append(f"{type(error).__name__}: {error}")
    h.write_private(path, json.dumps(evidence, indent=2) + "\n")
    return evidence


def main(argv=None):
    global CANDIDATE_VERSION
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--phase", choices=("setup", "run", "v15", "all", "stop"), default="all")
    parser.add_argument("--candidate", type=Path, help="Candidate plugin ZIP for setup/all.")
    parser.add_argument("--expected-dll-sha256", help="Expected candidate DLL SHA256 for setup/run/all.")
    parser.add_argument("--expected-candidate-version", default=CANDIDATE_VERSION, help="Candidate plugin version for setup/all.")
    parser.add_argument("--test-plugin-dll", type=Path, default=DEFAULT_TEST_PLUGIN)
    parser.add_argument("--install-test-plugin", action="store_true",
                        help="Stop the fixture, reinstall --test-plugin-dll, and restart before run/v15.")
    parser.add_argument("--keep-running", action="store_true", help="Do not stop the fixture after the all phase.")
    args = parser.parse_args(argv)
    CANDIDATE_VERSION = args.expected_candidate_version
    root = h.guard_root(args.root)
    os.environ["JELLYP_TEST_IMAGE_BARRIER"] = str(root / IMAGE_BARRIER_NAME)
    if args.phase == "stop":
        final = stop_and_verify(root)
        print(json.dumps(final, sort_keys=True))
        return
    preflight()
    if not h.is_sha256(args.expected_dll_sha256):
        parser.error("--expected-dll-sha256 must be 64 hex characters")
    if args.install_test_plugin and not args.test_plugin_dll.is_file():
        parser.error(f"Test plugin DLL not found: {args.test_plugin_dll}")
    if args.phase in ("setup", "all"):
        if not args.candidate or not args.candidate.is_file():
            parser.error("--candidate is required for setup/all")
        meta, dll_hash = h.read_plugin_archive(args.candidate, CANDIDATE_VERSION)
        if dll_hash != args.expected_dll_sha256.upper():
            parser.error("Candidate DLL SHA256 does not match --expected-dll-sha256")
        if not args.test_plugin_dll.is_file():
            parser.error(f"Test plugin DLL not found: {args.test_plugin_dll}")

    error = None
    stop_error = None
    final = None
    try:
        if args.phase in ("setup", "all"):
            # Stop a previous fixture before replacing the candidate or test plugin.
            h.stop_host(root)
            h.install_plugin(root, args.candidate, CANDIDATE_VERSION)
            ensure_fixture_state(root, args.test_plugin_dll)
            print(f"setup complete: {root / STATE_NAME}")
        if args.phase in ("run", "all", "v15"):
            state = load_state(root)
            if args.install_test_plugin and args.phase in ("run", "v15"):
                h.stop_host(root)
                install_test_plugin(root, args.test_plugin_dll)
                h.start_host(root)
            if args.phase == "run":
                run_gates(root, state, args.expected_dll_sha256)
                print(f"run complete: {root / EVIDENCE_NAME}")
            elif args.phase == "v15":
                run_gates(root, state, args.expected_dll_sha256, scenarios=("v15",))
                print(f"v15 complete: {root / EVIDENCE_NAME}")
            else:
                run_gates(root, state, args.expected_dll_sha256)
    except BaseException as failure:
        error = failure
    finally:
        should_stop = args.phase == "all" and not args.keep_running
        if args.phase == "setup" and error is not None and not args.keep_running:
            should_stop = True
        if should_stop:
            try:
                final = stop_and_verify(root)
            except BaseException as failure:
                stop_error = failure
                final = {"stopped": False, "error": f"{type(failure).__name__}: {failure}"}
        elif args.phase == "all":
            final = {"keptRunning": True, "portFree": not h.port_open()}
        if args.phase in ("setup", "all") or error is not None:
            try:
                write_final_evidence(root, final, error or stop_error)
            except BaseException as failure:
                if error is None:
                    error = failure
                else:
                    try:
                        error.add_note(f"could not write final evidence: {failure}")
                    except AttributeError:
                        print(f"could not write final evidence: {failure}", file=sys.stderr)
    if error is not None:
        if stop_error is not None:
            try:
                error.add_note(f"fixture stop failed: {stop_error}")
            except AttributeError:
                print(f"fixture stop failed: {stop_error}", file=sys.stderr)
        raise error
    if stop_error is not None:
        raise stop_error
    if final is not None and final.get("stopped") is False:
        raise GateFailure(final.get("error", "fixture did not stop"))


if __name__ == "__main__":
    main()
