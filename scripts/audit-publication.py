#!/usr/bin/env python3
"""Audit the Git index (or untracked candidates) without printing matched secrets."""
import argparse
import ipaddress
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ALLOWED_ROOTS = {'.github', 'Jellyfin.Plugin.JellyP', 'tests', 'scripts', 'docs'}
ALLOWED_FILES = {'.editorconfig', '.gitignore', 'JellyP.slnx', 'LICENSE', 'README.md',
                 'CONTRIBUTING.md', 'SECURITY.md', 'CHANGELOG.md', 'CODE_OF_CONDUCT.md',
                 'build.yaml', 'global.json'}
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
    print(f'Publication audit passed for {len(files)} text files. Manual review is still required.')


if __name__ == '__main__':main()
