#!/usr/bin/env python3
"""Audit the Git index (or untracked candidates) without printing matched secrets."""
import argparse
import hashlib
import ipaddress
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ALLOWED_ROOTS = {'.github', 'Jellyfin.Plugin.JellyP', 'tests', 'scripts', 'docs'}
ALLOWED_FILES = {'.editorconfig', '.gitignore', 'JellyP.slnx', 'LICENSE', 'README.md',
                 'CONTRIBUTING.md', 'SECURITY.md', 'CHANGELOG.md', 'CODE_OF_CONDUCT.md',
                  'build.yaml', 'global.json', 'assets/branding/README.md'}
# Only these owner-approved PNGs may cross the binary-source boundary. Keep
# unknown binaries rejected; changing artwork requires an explicit hash review.
BRANDING_SHA256 = {
    'assets/branding/jellyp-plugin.png': '250d2c78f3ccff65eb68fbadedd6d98740e393e2cf2eff8c816456200ec307b8',
    'assets/branding/masters/jellyp-catalog-navy.png': 'b44b2754ecceb94bbe283ac14f0cc71a0c8891485f15db5e6e1e1b54e4d8ddef',
    'assets/branding/masters/jellyp-symbol.png': 'c6ebb298d39d212bb6fa25a62fb06d7daafd375e247198f65b6271bc28986c5a',
    'assets/branding/masters/jellyp-wordmark-dark.png': 'd6a88e08187b4bffb12fde707016955e010030e695c6ba262de1094e8a562b74',
    'assets/branding/masters/jellyp-wordmark-light.png': '184e616a66ecc54e93384313c7c3d5804f463821b494c6c5158c2feb9478be6b',
    'assets/branding/jellyp-catalog.png': '3012ba678c922cd4f041e33863f6f0691cdc4861171c2944038afa6f8f21baf8',
    'Jellyfin.Plugin.JellyP/Web/branding/jellyp-symbol.png': 'd1ef44d509dd6bc8296fc9a1e41491e6d74a45f3a0722b4e922da5ed6eba6841',
    'Jellyfin.Plugin.JellyP/Web/branding/jellyp-wordmark-dark.png': 'b5f340dfc323055cadb48dba0fa677e88684c79fdcfcd014439d8c0805a8135a',
    'Jellyfin.Plugin.JellyP/Web/branding/jellyp-wordmark-light.png': '5d315ddaa4b96c4defa9733f4de8c37aa9baadbc115e88f71640323e1af7484d',
}


def approved_branding(name, data):
    return data.startswith(b'\x89PNG\r\n\x1a\n') and hashlib.sha256(data).hexdigest() == BRANDING_SHA256.get(name)


PATTERNS = {
    'private key': rb'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
    'GitHub token': rb'\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})',
    'AWS access key': rb'\b(?:AKIA|ASIA)[A-Z0-9]{16}\b',
    'local home path': rb'/(?:home|Users)/[A-Za-z0-9_.-]+/',
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--working-tree', action='store_true', help='Include untracked, nonignored candidates before staging')
    args = parser.parse_args()
    command = ['git', 'ls-files', '-z', '--cached']
    if args.working_tree:
        command += ['--others', '--exclude-standard']
    files = sorted(set(subprocess.check_output(command, cwd=ROOT).decode().split('\0')) - {''})
    if not files:
        raise SystemExit('No publication files found.')
    issues = []
    for name in files:
        path = Path(name)
        if name in BRANDING_SHA256:
            data = (ROOT / path).read_bytes() if args.working_tree else subprocess.check_output(['git', 'show', ':' + name], cwd=ROOT)
            if not approved_branding(name, data):
                issues.append((name, 'branding PNG differs from approved hash'))
            continue
        if path.parts[0] not in ALLOWED_ROOTS and name not in ALLOWED_FILES:
            issues.append((name, 'unexpected publication path'));continue
        if any(part in {'.local', 'bin', 'obj', 'artifacts', '__pycache__'} for part in path.parts) or path.suffix in {'.db', '.sqlite', '.sqlite3', '.log', '.original', '.key', '.pem'}:
            issues.append((name, 'private/generated file'));continue
        data = (ROOT / path).read_bytes() if args.working_tree else subprocess.check_output(['git', 'show', ':' + name], cwd=ROOT)
        try:data.decode('utf-8')
        except UnicodeDecodeError:
            issues.append((name, 'binary source asset'));continue
        for description, pattern in PATTERNS.items():
            if re.search(pattern, data):issues.append((name, description))
        for address in re.findall(rb'\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b', data):
            try:ip = ipaddress.ip_address(address.decode())
            except ValueError:continue
            # Four-part Jellyfin/assembly versions are not IPv4 destinations.
            if not ip.is_loopback and re.search(rb'(?:https?://|Host=|ssh )' + re.escape(address), data):
                issues.append((name, 'non-loopback host address'))
    if issues:
        for name, description in issues:print(name + ': ' + description)
        raise SystemExit('Publication audit failed. Review locally; no secret values were printed.')
    print(f'Publication audit passed for {len(files)} files (text and hash-approved branding PNGs). Manual review is still required.')


if __name__ == '__main__':main()
