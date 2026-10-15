#!/usr/bin/env python3
"""Build/sign the local ARM64 test variant; tools and all outputs stay ignored."""
import argparse
import hashlib
import os
import secrets
import shutil
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EXPECTED = 'f7deda6deaea54375d63e6f58777c873c1ff41a8d1dd1d04a06d0612716849bc'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('apk', type=Path)
    parser.add_argument('--reuse-decoded', action='store_true', help='Reuse this task\'s disposable apktool tree')
    args = parser.parse_args()
    apk = args.apk.resolve()
    digest = hashlib.sha256()
    with apk.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    if digest.hexdigest() != EXPECTED:
        parser.error('Only the analyzed com.netease.ma71 4.2.0 APK is supported')
    work = ROOT / 'work/rea-apk'
    tools = work / 'tools'
    out = ROOT / 'outputs/rea-apk'
    out.mkdir(parents=True, exist_ok=True)
    jdk = tools / 'jdk-21.0.12.1+1/Contents/Home'
    build_tools = tools / 'android-sdk/build-tools/35.0.0'
    env = {**os.environ, 'JAVA_HOME': str(jdk)}

    def run(command):
        subprocess.run([str(value) for value in command], cwd=ROOT, env=env, check=True)

    decoded = work / 'decoded'
    raw = work / 'local-original-patch-input'
    raw.mkdir(exist_ok=True)
    with zipfile.ZipFile(apk) as archive:
        for name in ['lib/arm64-v8a/libil2cpp.so', 'assets/AssetBundles/Android/lua.assetbundle',
                     'assets/bin/Data/Managed/Metadata/global-metadata.dat']:
            archive.extract(name, raw)
    apktool = [jdk / 'bin/java', '-Xmx4g', '-jar', tools / 'apktool_3.0.3.jar']
    if not args.reuse_decoded:
        if decoded.exists():
            parser.error('Decoded tree exists; pass --reuse-decoded to reuse it')
        run(apktool + ['d', apk, '-o', decoded, '-p', work / 'apktool-framework'])
    run([tools / 'resource-venv/bin/python', ROOT / 'scripts/prepare-trail-local-apk.py',
         '--decoded', decoded, '--raw', raw])
    # Native boot adaptation is verified only for ARM64. Do not ship unpatched ABIs.
    for abi in (decoded / 'lib').iterdir():
        if abi.is_dir() and abi.name != 'arm64-v8a':
            shutil.rmtree(abi)
    unsigned, aligned, signed = [out / name for name in
        ['trail-local-unsigned.apk', 'trail-local-aligned.apk', 'trail-local-4.2.0-arm64.apk']]
    run(apktool + ['b', decoded, '-p', work / 'apktool-framework', '-o', unsigned])
    signing = work / 'local-signing'
    signing.mkdir(mode=0o700, exist_ok=True)
    password, keystore = signing / 'password', signing / 'test.p12'
    if not password.exists():
        password.write_text(secrets.token_hex(32))
        password.chmod(0o600)
    if not keystore.exists():
        run([jdk / 'bin/keytool', '-genkeypair', '-keystore', keystore, '-storetype', 'PKCS12',
             '-alias', 'local-test', '-keyalg', 'RSA', '-keysize', '2048', '-validity', '3650',
             '-dname', 'CN=Trail Local Test', '-storepass:file', password])
        keystore.chmod(0o600)
    run([build_tools / 'zipalign', '-f', '-P', '16', '4', unsigned, aligned])
    run([build_tools / 'apksigner', 'sign', '--ks', keystore, '--ks-key-alias', 'local-test',
         '--ks-pass', f'file:{password}', '--out', signed, aligned])
    run([build_tools / 'apksigner', 'verify', '--verbose', signed])
    run([build_tools / 'zipalign', '-c', '-P', '16', '4', signed])
    print(f'Built {signed}; install and client/server runtime verification are separate steps.')


if __name__ == '__main__':
    main()
