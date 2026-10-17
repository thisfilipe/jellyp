#!/usr/bin/env python3
"""Generate a Jellyfin catalog from the exact ZIP being released."""
import argparse
import datetime as dt
import hashlib
import json
import re
from pathlib import Path
import package


def version_key(version):
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+", version):
        raise ValueError("Invalid four-part catalog version.")
    return tuple(int(n) for n in version.split('.'))


def changelog(version):
    text = (package.ROOT / 'CHANGELOG.md').read_text()
    match = re.search(r'^## \[' + re.escape(version) + r'\][^\n]*\n(.*?)(?=^## |\Z)', text, re.M | re.S)
    if not match:
        raise ValueError('Add this version to CHANGELOG.md before release.')
    return match.group(1).strip()


def update(previous, archive, timestamp):
    meta = package.metadata()
    package.validate_archive(archive, meta)
    if Path(archive).name != f"JellyP_{meta['version']}.zip":
        raise ValueError('Archive name must match the project version.')
    if dt.datetime.fromisoformat(timestamp.replace('Z', '+00:00')).utcoffset() != dt.timedelta(0):
        raise ValueError('Catalog timestamp must use UTC.')
    entry = {key: meta[key] for key in ('guid', 'name', 'overview', 'description', 'owner', 'category')}
    release = {
        'version': meta['version'], 'targetAbi': meta['targetAbi'],
        'sourceUrl': f"https://github.com/{package.REPOSITORY}/releases/download/v{meta['version']}/{archive.name}",
        # Jellyfin's installer requires MD5; SHA256 is published alongside the ZIP.
        'checksum': hashlib.md5(Path(archive).read_bytes(), usedforsecurity=False).hexdigest(),
        'timestamp': timestamp, 'changelog': changelog(meta['version']),
    }
    if not isinstance(previous, list) or len(previous) > 1:
        raise ValueError('Expected a single-plugin catalog.')
    old = previous[0] if previous else {**entry, 'versions': []}
    if old['guid'] != meta['guid'] or old['name'] != meta['name']:
        raise ValueError('Refusing to change an existing plugin identity.')
    versions = old['versions']
    if len({v['version'] for v in versions}) != len(versions):
        raise ValueError('Duplicate catalog versions.')
    same = next((v for v in versions if v['version'] == release['version']), None)
    if same:
        for key in ('checksum', 'sourceUrl', 'targetAbi', 'changelog'):
            if same[key] != release[key]:
                raise ValueError('Published versions are immutable; increment the version.')
    else:
        versions = [*versions, release]
    entry['versions'] = sorted(versions, key=lambda v: version_key(v['version']), reverse=True)
    return [entry]


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', required=True, type=Path)
    parser.add_argument('--previous', type=Path)
    parser.add_argument('--timestamp', required=True)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    previous = json.loads(args.previous.read_text()) if args.previous else []
    result = update(previous, args.archive, args.timestamp)
    args.output.write_text(json.dumps(result, indent=2) + '\n')
