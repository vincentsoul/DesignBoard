#!/usr/bin/env python3
"""Patch a disposable apktool tree for the supplied 4.2.0 ARM64 APK.

Requires UnityPy. This does not download, install, or execute the target APK.
APK content, the recovered substitution map, and modified assets stay ignored.
"""
import argparse
import hashlib
import struct
import xml.etree.ElementTree as ET
from pathlib import Path

import UnityPy


def prepare(decoded, raw, adapter):
    if decoded == raw or raw in decoded.parents:
        raise ValueError('Use a separate disposable decoded tree, outside the original input directory')
    metadata = (raw / 'assets/bin/Data/Managed/Metadata/global-metadata.dat').read_bytes()
    assert metadata.startswith(b'mark'), 'Unexpected metadata carrier'
    metadata = bytes(b ^ 0xA8 for b in metadata[4:])
    assert metadata[:8] == struct.pack('<II', 0xFAB11BAF, 24)
    # Field initializer identified through FileEncrypt..cctor and dump.cs.
    table = metadata[0x1042B0D:0x1042B0D + 1024]
    assert hashlib.sha1(table).hexdigest() == 'c2ea48b5a66392c5116afd5fea4ddb8c620b91ed'
    forward = list(range(256))
    for i, j in enumerate(struct.unpack('<256I', table)):
        assert j < 256
        forward[i], forward[j] = forward[j], forward[i]
    forward = bytes(v ^ 0x47 for v in forward)
    backward = bytearray(256)
    for i, j in enumerate(forward):
        backward[j] = i
    bundle_path = Path('assets/AssetBundles/Android/lua.assetbundle')
    encrypted = (raw / bundle_path).read_bytes()
    plain = encrypted.translate(bytes(backward))
    assert plain.startswith(b'UnityFS\0')
    assert plain.translate(forward) == encrypted
    env = UnityPy.load(plain)
    changed = 0
    for obj in env.objects:
        if obj.type.name != 'TextAsset':
            continue
        data = obj.read()
        if data.m_Name == 'xMain.lua':
            assert data.m_Script.count('return XMain') == 1
            data.m_Script = 'CS.UnityEngine.Debug.LogWarning("[TrailLocal] xMain loaded")\n' + data.m_Script.replace('return XMain', adapter.read_text() + '\nreturn XMain')
            data.save()
            changed += 1
    assert changed == 1, 'Expected one xMain entry'
    modified = env.file.save(packer='lz4')
    # Confirm the replacement survives Unity serialization before encrypting it.
    verify = UnityPy.load(modified)
    assert any(o.type.name == 'TextAsset' and o.read().m_Name == 'xMain.lua'
               and 'record("transport installed")' in o.read().m_Script for o in verify.objects)
    (decoded / bundle_path).write_bytes(modified.translate(forward))

    binary_path = Path('lib/arm64-v8a/libil2cpp.so')
    binary = bytearray((raw / binary_path).read_bytes())
    assert hashlib.sha256(binary).hexdigest() == '30b2e2fc607aa4468ddadfef41337fa17d2c5e40f70da1bf9c2b88ddf8899af2', 'Unsupported native library'
    assert binary[0x47D6028:0x47D602C] == bytes.fromhex('b1000094'), 'Native build differs'
    # Keep CoStart, including its resident Lua bundle load. Replace only the
    # remote updater UI coroutine factory with GameStart followed by return null.
    address = 0x47D6C30
    patch = bytes.fromhex('fd7bbfa9fd030091e1031faa')
    patch += struct.pack('<I', 0x94000000 | (((0x47D68F4 - (address + 12)) // 4) & 0x3FFFFFF))
    patch += bytes.fromhex('e0031faafd7bc1a8c0035fd6')
    binary[address:address + len(patch)] = patch
    (decoded / binary_path).write_bytes(binary)

    ns = 'http://schemas.android.com/apk/res/android'
    ET.register_namespace('android', ns)
    a = lambda key: '{' + ns + '}' + key
    manifest = decoded / 'AndroidManifest.xml'
    tree = ET.parse(manifest)
    root = tree.getroot()
    root.set('package', 'com.netease.ma71.local')
    app = root.find('application')
    app.set(a('label'), '边境之旅·本地测试')
    app.set(a('usesCleartextTraffic'), 'true')
    app.set(a('debuggable'), 'true')
    for provider in app.findall('provider'):
        value = provider.get(a('authorities'))
        if value and 'com.netease.ma71.local' not in value:
            provider.set(a('authorities'), value.replace('com.netease.ma71', 'com.netease.ma71.local'))
    launcher = app.find("activity[@" + a('name') + "='com.netease.ntunisdk.external.protocol.ProtocolLauncher']")
    activity = app.find("activity[@" + a('name') + "='com.netease.ma71.Ma71']")
    assert launcher is not None and activity is not None
    for intent in list(launcher.findall('intent-filter')):
        launcher.remove(intent)
        activity.append(intent)
    activity.set(a('exported'), 'true')
    activity.set(a('label'), '边境之旅·本地测试')
    tree.write(manifest, encoding='utf-8', xml_declaration=True)
    print('Prepared original Unity client with local HTTP adapter; runtime verification still required.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--decoded', type=Path, required=True)
    parser.add_argument('--raw', type=Path, required=True)
    parser.add_argument('--adapter', type=Path, default=Path('prototypes/trail-server/client/local-transport.lua'))
    args = parser.parse_args()
    prepare(args.decoded.resolve(), args.raw.resolve(), args.adapter.resolve())
