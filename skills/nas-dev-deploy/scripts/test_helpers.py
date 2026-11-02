#!/usr/bin/env python3
"""Isolated helper tests: no live NAS calls or production credentials."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
import unittest

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent

def module(name):
    spec = importlib.util.spec_from_file_location(name, HERE / (name + '.py'))
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value

nas = module('nas')
packager = module('package_runtime')

class HelpersTest(unittest.TestCase):
    def test_profile_rejects_publicly_readable_credentials(self):
        with tempfile.TemporaryDirectory() as folder:
            file = Path(folder) / 'profile.json'
            file.write_text(json.dumps({'host': 'example.invalid', 'user': 'admin', 'password': 'fixture-password'}))
            file.chmod(0o644)
            with self.assertRaises(ValueError): nas.load_profile(file)
            file.chmod(0o600)
            self.assertEqual(nas.load_profile(file)['host'], 'example.invalid')

    def test_transport_answers_exact_prompt_without_leaking_echoed_credentials(self):
        with tempfile.TemporaryDirectory() as folder:
            script = Path(folder) / 'prompt.py'
            script.write_text('''import sys,termios
attrs=termios.tcgetattr(0);attrs[3]&=~termios.ECHO;termios.tcsetattr(0,termios.TCSANOW,attrs)
sys.stdout.write("admin@example.invalid's password:");sys.stdout.flush()
a=sys.stdin.readline().strip()
sys.stdout.write("[codex-nas-sudo] password:");sys.stdout.flush()
b=sys.stdin.readline().strip()
print("credentials:",a,b)
sys.exit(0 if a=="fixture-password" and b=="fixture-sudo" else 1)
''')
            captured = io.StringIO()
            with contextlib.redirect_stdout(captured):
                code = nas.run_pty([sys.executable, str(script)], {"admin@example.invalid's password:": 'fixture-password', nas.SUDO_PROMPT: 'fixture-sudo'}, ['fixture-password', 'fixture-sudo'], timeout=5)
            self.assertEqual(code, 0)
            self.assertNotIn('fixture-password', captured.getvalue())
            self.assertNotIn('fixture-sudo', captured.getvalue())
            self.assertIn('[REDACTED]', captured.getvalue())

    def test_timeout_terminates_child(self):
        captured = io.StringIO()
        with contextlib.redirect_stdout(captured), self.assertRaises(TimeoutError):
            nas.run_pty([sys.executable, '-c', 'import time; time.sleep(20)'], {}, timeout=.1)

    def test_archive_uses_committed_content_and_blocks_private_members(self):
        with tempfile.TemporaryDirectory() as folder:
            repo = Path(folder) / 'repo'; repo.mkdir()
            def git(*args): return subprocess.run(['git', '-C', str(repo), *args], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            git('init', '-b', 'main'); git('config', 'user.name', 'Fixture'); git('config', 'user.email', 'fixture@example.invalid')
            (repo / 'src').mkdir(); (repo / 'secure').mkdir()
            (repo / 'src/app.js').write_text('committed runtime')
            (repo / 'secure/auth.json').write_text('fixture-only secret')
            git('add', '.'); git('commit', '-m', 'fixture')
            (repo / 'src/app.js').write_text('uncommitted other task')
            (repo / 'src/untracked.js').write_text('not for release')
            output = Path(folder) / 'release.tar.gz'
            manifest = packager.package(repo, 'HEAD', ['src'], output)
            self.assertEqual([file['path'] for file in manifest['files']], ['src/app.js'])
            with tarfile.open(output) as archive:
                self.assertEqual(archive.extractfile('src/app.js').read(), b'committed runtime')
            with self.assertRaises(ValueError): packager.package(repo, 'HEAD', ['secure'], Path(folder) / 'private.tar.gz')
            with self.assertRaises(ValueError): packager.package(repo, 'HEAD', ['../outside'], Path(folder) / 'outside.tar.gz')
            with self.assertRaises(ValueError): packager.package(repo, 'HEAD', ['missing'], Path(folder) / 'missing.tar.gz')
            with self.assertRaises(ValueError): packager.package(repo, 'HEAD', ['src'], output)

    def test_archive_rejects_symlinks(self):
        with tempfile.TemporaryDirectory() as folder:
            repo = Path(folder) / 'repo'; repo.mkdir()
            def git(*args): return subprocess.run(['git', '-C', str(repo), *args], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            git('init', '-b', 'main'); git('config', 'user.name', 'Fixture'); git('config', 'user.email', 'fixture@example.invalid')
            (repo / 'src').mkdir(); (repo / 'src/link').symlink_to('/etc/passwd')
            git('add', '.'); git('commit', '-m', 'fixture')
            output = Path(folder) / 'bad.tar.gz'
            with self.assertRaises(ValueError): packager.package(repo, 'HEAD', ['src'], output)
            self.assertFalse(output.exists())

if __name__ == '__main__': unittest.main()
