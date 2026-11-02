#!/usr/bin/env python3
"""Archive explicit runtime paths from a Git commit, excluding working-tree edits."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import subprocess
import tarfile


def git(repo, *args):
    return subprocess.check_output(['git', '-C', str(repo), *args])


def private_path(name):
    path = PurePosixPath(name)
    private_parts = {'secure', 'private', 'boards', 'outputs', 'work', '.ssh', '.git', 'node_modules', '__pycache__'}
    return (any(part in private_parts for part in path.parts)
            or any(part == '.env' or (part.startswith('.env.') and part not in {'.env.example', '.env.sample'}) for part in path.parts)
            or path.name == 'compose.override.yaml'
            or path.name.startswith(('id_ed25519', 'id_rsa'))
            or path.suffix.lower() in {'.pem', '.key', '.p12', '.pfx', '.sqlite', '.sqlite3', '.db'})


def package(repo, revision, includes, output):
    repo = Path(repo).resolve()
    output = Path(output).resolve()
    if not includes:
        raise ValueError('Specify runtime paths explicitly')
    for name in includes:
        p = PurePosixPath(name)
        if not name or name == '.' or p.is_absolute() or '..' in p.parts or name.startswith('-'):
            raise ValueError('Includes must be explicit relative runtime paths')
    commit = git(repo, 'rev-parse', '--verify', revision + '^{commit}').decode().strip()
    tracked = git(repo, 'ls-tree', '-rz', '--name-only', commit).decode().split('\0')
    members = sorted(name for name in tracked if name and any(name == inc.rstrip('/') or name.startswith(inc.rstrip('/') + '/') for inc in includes))
    if not members:
        raise ValueError('No tracked runtime files match the supplied includes')
    blocked = [name for name in members if private_path(name)]
    if blocked:
        raise ValueError('Refusing private or data paths: ' + ', '.join(blocked))
    for inc in includes:
        if not any(name == inc.rstrip('/') or name.startswith(inc.rstrip('/') + '/') for name in members):
            raise ValueError(f'Include not found in selected commit: {inc}')
    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists() or Path(str(output) + '.manifest.json').exists():
        raise ValueError('Output already exists; select a new release path')
    data = git(repo, 'archive', '--format=tar.gz', commit, '--', *members)
    files = []
    try:
        output.write_bytes(data)
        with tarfile.open(output, 'r:gz') as archive:
            for item in archive.getmembers():
                path = PurePosixPath(item.name)
                if path.is_absolute() or '..' in path.parts or item.issym() or item.islnk():
                    raise ValueError('Archive contains unsafe paths or symbolic links')
                if item.isfile():
                    if private_path(item.name):
                        raise ValueError('Archive contains a private path')
                    contents = archive.extractfile(item).read()
                    files.append({'path': item.name, 'bytes': len(contents), 'sha256': hashlib.sha256(contents).hexdigest()})
        if {f['path'] for f in files} != set(members):
            raise ValueError('Git export attributes changed archive contents; inspect export-ignore settings')
        manifest = {'commit': commit, 'archiveSha256': hashlib.sha256(data).hexdigest(), 'files': files}
        Path(str(output) + '.manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
        return manifest
    except BaseException:
        output.unlink(missing_ok=True)
        Path(str(output) + '.manifest.json').unlink(missing_ok=True)
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, required=True)
    parser.add_argument('--revision', default='HEAD')
    parser.add_argument('--include', action='append', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    manifest = package(args.repo, args.revision, args.include, args.output)
    print(json.dumps({'commit': manifest['commit'], 'fileCount': len(manifest['files']), 'output': str(args.output), 'manifest': str(args.output) + '.manifest.json'}, ensure_ascii=False))


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        raise SystemExit(str(error))
