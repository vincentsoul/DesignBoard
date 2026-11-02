#!/usr/bin/env python3
"""POSIX SSH/SCP helper. Credentials stay in a private local profile, not argv."""
import argparse
import errno
import json
import os
from pathlib import Path
import pty
import re
import select
import shlex
import signal
import stat
import sys
import time

DEFAULT_PROFILE = Path.home() / '.codex/private/nas/evantra.json'
SUDO_PROMPT = '[codex-nas-sudo] password:'


def load_profile(path):
    path = Path(path).expanduser()
    info = path.stat()
    if not stat.S_ISREG(info.st_mode) or stat.S_IMODE(info.st_mode) & 0o077 or info.st_uid != os.getuid():
        raise ValueError('Profile must be a regular file owned by this user, with mode 600 or stricter')
    profile = json.loads(path.read_text())
    for key in ('host', 'user'):
        if not isinstance(profile.get(key), str) or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]*', profile[key]):
            raise ValueError('Invalid SSH host or username')
    port = profile.get('port', 22)
    if not isinstance(port, int) or isinstance(port, bool) or not 1 <= port <= 65535:
        raise ValueError('Invalid SSH port')
    for key in ('password', 'sudoPassword'):
        value = profile.get(key)
        if value is not None and (not isinstance(value, str) or any(c in value for c in '\r\n\x00')):
            raise ValueError('Invalid password field')
    return profile


def run_pty(argv, prompts, secrets=(), timeout=600):
    """Respond only to exact transport prompts; redact buffered output before emitting."""
    child, fd = pty.fork()
    if child == 0:
        try:
            os.execvp(argv[0], argv)
        except Exception:
            os._exit(127)
    output = bytearray()
    window = bytearray()
    answered = set()
    deadline = time.monotonic() + timeout
    status = None
    stopped = False
    try:
        while True:
            if time.monotonic() >= deadline:
                raise TimeoutError('Remote command timed out')
            ready, _, _ = select.select([fd], [], [], min(1, max(.01, deadline - time.monotonic())))
            if ready:
                try:
                    chunk = os.read(fd, 65536)
                except OSError as error:
                    if error.errno != errno.EIO:
                        raise
                    chunk = b''
                if not chunk:
                    break
                output.extend(chunk)
                if len(output) > 8 * 1024 * 1024:
                    del output[:-8 * 1024 * 1024]
                window.extend(chunk)
                if len(window) > 8192:
                    del window[:-8192]
                for marker, value in prompts.items():
                    if marker not in answered and marker.encode() in window:
                        if not value:
                            raise ValueError('Credential required for SSH or sudo; check the private profile')
                        os.write(fd, (value + '\n').encode())
                        answered.add(marker)
                        window.clear()
                        break
            waited, code = os.waitpid(child, os.WNOHANG)
            if waited:
                status = code
                # Read final PTY output, if any, before EOF.
                while select.select([fd], [], [], .05)[0]:
                    try:
                        chunk = os.read(fd, 65536)
                    except OSError as error:
                        if error.errno != errno.EIO:
                            raise
                        break
                    if not chunk:
                        break
                    output.extend(chunk)
                break
    except BaseException:
        stopped = True
        try:
            os.kill(child, signal.SIGTERM)
        except ProcessLookupError:
            pass
        raise
    finally:
        os.close(fd)
        if status is None:
            if stopped:
                try:
                    os.kill(child, signal.SIGKILL)
                except ProcessLookupError:
                    pass
            _, status = os.waitpid(child, 0)
        text = output.decode('utf-8', errors='replace')
        for secret in sorted(set(value for value in secrets if value), key=len, reverse=True):
            text = text.replace(secret, '[REDACTED]')
        sys.stdout.write(text)
        sys.stdout.flush()
    return os.waitstatus_to_exitcode(status)


def transport(profile, scp=False):
    args = ['scp' if scp else 'ssh', '-P' if scp else '-p', str(profile.get('port', 22)),
            '-o', 'StrictHostKeyChecking=yes', '-o', 'ConnectTimeout=10',
            '-o', 'NumberOfPasswordPrompts=1', '-o', 'ForwardAgent=no']
    if profile.get('identityFile'):
        args += ['-i', str(Path(profile['identityFile']).expanduser()), '-o', 'IdentitiesOnly=yes']
    if not profile.get('password'):
        args += ['-o', 'BatchMode=yes']
    else:
        args += ['-o', 'PreferredAuthentications=publickey,password']
    return args


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--profile', type=Path, default=DEFAULT_PROFILE)
    sub = parser.add_subparsers(dest='action', required=True)
    sub.add_parser('check', help='Read-only host and Docker discovery')
    run = sub.add_parser('run', help='Run a reviewed local shell script on the NAS')
    run.add_argument('--script', type=Path, required=True)
    run.add_argument('--sudo', action='store_true')
    run.add_argument('--timeout', type=int, default=600)
    upload = sub.add_parser('upload', help='Upload a single file to an absolute remote path')
    upload.add_argument('source', type=Path)
    upload.add_argument('destination')
    args = parser.parse_args()
    profile = load_profile(args.profile)
    target = f"{profile['user']}@{profile['host']}"
    password = profile.get('password')
    sudo_password = profile.get('sudoPassword', password)
    prompts = {f"{target}'s password:": password, SUDO_PROMPT: sudo_password}
    if args.action == 'upload':
        if not args.source.is_file():
            raise ValueError('Source must be a regular file')
        if not args.destination.startswith('/') or any(c in args.destination for c in '\r\n\x00'):
            raise ValueError('Remote destination must be an absolute path')
        argv = transport(profile, scp=True) + ['--', str(args.source.resolve()), f'{target}:{args.destination}']
        timeout = 120
    else:
        if args.action == 'check':
            root = profile.get('projectDir')
            script = 'set -eu\nhostname\nuname -m\ncommand -v docker\ndocker compose version\n'
            if root:
                script += f'test -d {shlex.quote(root)} && printf "Configured project directory exists\\n"\n'
            timeout, sudo = 30, False
        else:
            script = args.script.read_text()
            timeout, sudo = args.timeout, args.sudo
            if timeout <= 0:
                raise ValueError('Timeout must be positive')
        command = 'sh -c ' + shlex.quote(script)
        if sudo:
            command = 'sudo -p ' + shlex.quote(SUDO_PROMPT) + ' -- ' + command
        argv = transport(profile) + ['-tt', target, command]
    return run_pty(argv, prompts, (password, sudo_password), timeout)


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (ValueError, OSError, TimeoutError, json.JSONDecodeError) as error:
        print(f'NAS helper: {error}', file=sys.stderr)
        raise SystemExit(1)
