#!/usr/bin/env python3
"""Publish a catalog only after matching assets exist on a public GitHub release.

Requires gh authenticated with repository contents:write. Called only by trusted
release workflows; no credentials are written into files or URLs.
"""
import argparse
import base64
import hashlib
import json
import subprocess
import tempfile
from pathlib import Path
import catalog
import package


def gh(*args, body=None, missing_ok=False):
    command = ['gh', 'api', *args]
    if body is not None:
        command += ['--input', '-']
    result = subprocess.run(command, input=json.dumps(body) if body is not None else None,
                            text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        if missing_ok and 'HTTP 404' in result.stderr:
            return None
        raise RuntimeError('GitHub API request failed; catalog not published.')
    return json.loads(result.stdout) if result.stdout else None


def publish(repository, archive):
    if repository != package.REPOSITORY:
        raise ValueError('Repository must match the release configuration.')
    version = package.metadata()['version']
    release = gh(f'repos/{repository}/releases/tags/v{version}')
    if release['draft'] or release['prerelease'] or release['tag_name'] != 'v' + version:
        raise ValueError('A published non-prerelease release is required.')
    # Download through gh, compare both the ZIP and its SHA256 sidecar.
    with tempfile.TemporaryDirectory(prefix='jellyp-release-check-') as temporary:
        subprocess.run(['gh', 'release', 'download', 'v' + version, '--repo', repository,
                        '--pattern', archive.name, '--pattern', archive.name + '.sha256',
                        '--dir', temporary], check=True)
        downloaded = Path(temporary) / archive.name
        expected = hashlib.sha256(archive.read_bytes()).hexdigest()
        sidecar = Path(str(downloaded) + '.sha256').read_text().split()
        if sidecar != [expected, archive.name] or hashlib.sha256(downloaded.read_bytes()).hexdigest() != expected:
            raise ValueError('Published assets differ from the tested build; catalog unchanged.')
    content = gh(f'repos/{repository}/contents/manifest.json?ref=catalog', missing_ok=True)
    previous = json.loads(base64.b64decode(content['content'])) if content else []
    result = catalog.update(previous, archive, release['published_at'])
    encoded = (json.dumps(result, indent=2) + '\n').encode()
    if content and encoded == base64.b64decode(content['content']):
        print('Catalog already matches the published release.')
        return
    branch = gh(f'repos/{repository}/git/ref/heads/catalog', missing_ok=True)
    if branch is None:
        commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
        gh(f'repos/{repository}/git/refs', '--method', 'POST', body={'ref': 'refs/heads/catalog', 'sha': commit})
    body = {'branch': 'catalog', 'message': f'Publish JellyP {version} catalog entry',
            'content': base64.b64encode(encoded).decode()}
    if content:
        body['sha'] = content['sha']
    gh(f'repos/{repository}/contents/manifest.json', '--method', 'PUT', body=body)
    print('Published catalog for JellyP ' + version)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', required=True)
    parser.add_argument('--archive', type=Path, required=True)
    args = parser.parse_args()
    publish(args.repository, args.archive)
