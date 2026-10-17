#!/usr/bin/env python3
"""Exercise public catalog installation ONLY on a marked disposable localhost server.

Start a fresh Jellyfin 12.1 bound to 127.0.0.1:18096, with data/config/cache/logs
under --root. Create .jellyp-smoke-root there explicitly. Run --phase install,
restart that disposable process, then run --phase verify. To upgrade an older fixture, run --phase upgrade instead
of install, restart, then verify; saved configuration must survive. No production support.
"""
import argparse
import json
import secrets
import urllib.parse
import urllib.request
from pathlib import Path
import package

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--root', required=True, type=Path)
parser.add_argument('--phase', required=True, choices=('install', 'upgrade', 'verify'))
args = parser.parse_args()
root = args.root.resolve()
if not (root / '.jellyp-smoke-root').is_file():
    raise SystemExit('Refusing unmarked root.')
base = 'http://127.0.0.1:18096/'
auth = 'MediaBrowser Client="JellyP catalog fixture", Device="disposable fixture", DeviceId="jellyp-catalog-fixture", Version="0.1"'
token = None


def request(path, body=None):
    headers = {'Authorization': auth + (', Token="' + token + '"' if token else ''), 'Content-Type': 'application/json'}
    data = json.dumps(body).encode() if body is not None else None
    with urllib.request.urlopen(urllib.request.Request(base + path, data=data, headers=headers), timeout=120) as response:
        payload = response.read()
        return json.loads(payload) if payload and 'json' in response.headers.get('Content-Type', '') else payload


password_file = root / 'test-password'
if not password_file.exists():
    if args.phase != 'install' or request('System/Info/Public').get('StartupWizardCompleted'):
        raise SystemExit('Use a fresh disposable server for installation.')
    password_file.write_text(secrets.token_urlsafe(32));password_file.chmod(0o600)
    request('Startup/User')
    request('Startup/Configuration', {'UICulture': 'en-US', 'MetadataCountryCode': 'US', 'PreferredMetadataLanguage': 'en'})
    request('Startup/User', {'Name': 'jellyp-catalog-admin', 'Password': password_file.read_text()})
    request('Startup/RemoteAccess', {'EnableRemoteAccess': False, 'EnableAutomaticPortMapping': False})
    request('Startup/Complete', {})
token = request('Users/AuthenticateByName', {'Username': 'jellyp-catalog-admin', 'Pw': password_file.read_text()})['AccessToken']
assert Path(request('System/Info')['ProgramDataPath']).resolve() == root / 'data', 'Server does not match disposable root.'
meta = package.metadata()
plugin_id = meta['guid'].replace('-', '')
if args.phase in ('install', 'upgrade'):
    installed = [p for p in request('Plugins') if p['Id'].replace('-', '') == plugin_id]
    if args.phase == 'install':
        assert not installed
    else:
        assert any(p['Status'] == 'Active' and p['Version'] != meta['version'] for p in installed), 'Expected an older active plugin.'
        previous = request('Plugins/' + meta['guid'] + '/Configuration')
        (root / 'catalog-previous-settings.json').write_text(json.dumps(previous))
    url = f'https://raw.githubusercontent.com/{package.REPOSITORY}/catalog/manifest.json'
    request('Repositories', [{'Name': 'JellyP disposable validation', 'Url': url, 'Enabled': True}])
    found = next(p for p in request('Packages') if p['guid'].replace('-', '') == plugin_id)
    assert any(v['version'] == meta['version'] for v in found['versions'])
    params = urllib.parse.urlencode({'assemblyGuid': meta['guid'], 'version': meta['version'], 'repositoryUrl': url})
    request('Packages/Installed/JellyP?' + params, {})
    folder = root / 'data/plugins' / ('JellyP_' + meta['version'])
    assert (folder / package.DLL_NAME).is_file()
    installed = json.loads((folder / 'meta.json').read_text())
    assert installed['version'] == meta['version']
    print('Native Jellyfin catalog download, checksum and extraction passed. Restart this disposable process, then run --phase verify.')
else:
    plugin = next(p for p in request('Plugins') if p['Id'].replace('-', '') == plugin_id and p['Version'] == meta['version'])
    assert plugin['Status'] == 'Active' and plugin['Version'] == meta['version']
    config = request('Plugins/' + meta['guid'] + '/Configuration')
    snapshot = root / 'catalog-previous-settings.json'
    if snapshot.exists():
        assert config == json.loads(snapshot.read_text()), 'Saved configuration changed during upgrade.'
    else:
        assert not config['Enabled'] and config['DryRun'] and config['PreserveOriginals']
    assert len(config['ImagePolicies']) == 11
    assert request('JellyP/Status')['PendingRecovery'] == 0
    page = request('web/ConfigurationPage?name=jellyp')
    assert b'JellyPPolicies' in page and b'Execution mode' in page and b'PrepareEmergency' in page
    tasks = [t for t in request('ScheduledTasks') if t['Category'] == 'JellyP']
    assert len(tasks) == 2 and all(not t['Triggers'] for t in tasks)
    print('Catalog-installed plugin active after restart; configuration, embedded UI, recovery status and empty schedules verified.')
