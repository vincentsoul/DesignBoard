#!/usr/bin/env python3
"""Export readable resources from a REA-extracted APK; never run target code."""
import argparse
import collections
import gc
import hashlib
import json
import re
import shutil
import struct
import subprocess
from pathlib import Path

import UnityPy
from PIL import Image
import imageio_ffmpeg


def safe_name(value):
    return re.sub(r'[^\w.\-]+', '_', str(value))[:100] or 'unnamed'


def preload_split_references(env, assets_file):
    """UnityPy 1.25.4 does not discover split dependencies via find_file."""
    for external in assets_file.externals:
        if env.get_cab(external.path):
            continue
        basename = Path(external.path.replace('\\', '/')).name
        split = Path(env.path) / (basename + '.split0')
        if split.is_file():
            env.load_file(str(split), is_dependency=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('raw', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--skip-wem-decode', action='store_true',
                        help='Keep raw WEMs for a separate decoder without trying FFmpeg')
    args = parser.parse_args()
    raw, out = args.raw.resolve(), args.output.resolve()
    if not raw.is_dir() or out.exists() or out == raw or out in raw.parents:
        parser.error('Provide an existing extracted directory and a fresh output directory')
    out.mkdir(parents=True)
    rows, errors = [], []
    counts = collections.Counter()

    def record(source, dest, kind, **extra):
        rows.append(dict(source=str(source), output=str(dest.relative_to(out)), kind=kind,
                         sha256=hashlib.sha256(dest.read_bytes()).hexdigest(), **extra))
        counts[kind] += 1

    def write(source, relative, content, kind, **extra):
        dest = out / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(content)
        record(source, dest, kind, **extra)

    def error(source, stage, exc):
        errors.append(dict(source=str(source), stage=stage, error=str(exc)[:500]))

    files = sorted(p for p in raw.rglob('*') if p.is_file())
    # Detect media by bytes, including PNGs with a .jpg suffix.
    for path in files:
        rel = path.relative_to(raw)
        with path.open('rb') as stream:
            head = stream.read(32)
        try:
            if head.startswith((b'\x89PNG\r\n\x1a\n', b'\xff\xd8\xff')):
                with Image.open(path) as img:
                    img.load()
                    ext = '.png' if img.format == 'PNG' else '.jpg'
                    width, height = img.size
                dest = out / 'images-direct' / Path(str(rel) + ext)
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(path, dest)
                record(rel, dest, 'image-direct', width=width, height=height)
            elif head.startswith(b'OggS') or (head[:3] == b'ID3') or path.suffix.lower() in {'.mp3', '.ogg', '.wav', '.mp4'}:
                write(rel, Path('media-direct') / rel, path.read_bytes(), 'media-direct')
        except Exception as exc:
            error(rel, 'direct-media', exc)

    # Process Unity bundles and serialized Data files individually to bound memory.
    candidates = [p for p in files if str(p.relative_to(raw)).startswith(
        ('assets/AssetBundles/', 'assets/Bundles/', 'assets/bin/Data/'))
        and not re.search(r'\.split[1-9]\d*$', p.name)
        and p.suffix not in {'.resS', '.resource', '.manifest', '.dat', '.dll', '.config'}]
    for index, path in enumerate(candidates):
        rel = path.relative_to(raw)
        try:
            env = UnityPy.load(str(path))
            for obj in env.objects:
                kind = obj.type.name
                if kind not in {'Texture2D', 'Sprite', 'Mesh', 'AudioClip', 'TextAsset', 'Font'}:
                    continue
                try:
                    data = obj.read()
                    name = safe_name(getattr(data, 'm_Name', kind))
                    identity = hashlib.sha256(str(rel).encode()).hexdigest()[:12]
                    asset_id = safe_name(getattr(obj.assets_file, 'name', 'asset'))
                    base = Path('unity') / kind / identity / f'{asset_id}_{obj.path_id}_{name}'
                    if kind in {'Texture2D', 'Sprite'}:
                        if kind == 'Sprite':
                            preload_split_references(env, obj.assets_file)
                        elif data.m_Width <= 0 or data.m_Height <= 0:
                            raise ValueError('Texture has no static pixels (zero dimensions)')
                        img = data.image
                        dest = out / Path(str(base) + '.png')
                        dest.parent.mkdir(parents=True, exist_ok=True)
                        img.save(dest)
                        record(rel, dest, kind, object_name=name, path_id=obj.path_id,
                               width=img.width, height=img.height)
                    elif kind == 'Mesh':
                        write(rel, Path(str(base) + '.obj'), data.export().encode(), kind, object_name=name)
                    elif kind == 'TextAsset':
                        content = data.m_Script
                        if isinstance(content, str):
                            content = content.encode('utf-8', 'surrogateescape')
                        write(rel, Path(str(base) + '.bytes'), bytes(content), kind, object_name=name)
                    elif kind == 'Font' and getattr(data, 'm_FontData', None):
                        write(rel, Path(str(base) + '.ttf'), bytes(data.m_FontData), kind, object_name=name)
                    elif kind == 'AudioClip':
                        for sample, content in data.samples.items():
                            write(rel, Path(str(base) + '_' + safe_name(sample)), content, kind, object_name=name)
                except Exception as exc:
                    error(f'{rel}:{obj.path_id}:{kind}', 'unity-object', exc)
            del env
        except Exception as exc:
            error(rel, 'unity-file', exc)
        if index % 100 == 0:
            gc.collect()
            print(f'Unity {index}/{len(candidates)}: {dict(counts)}; errors={len(errors)}', flush=True)

    # AKPK tables: language bytes, bank table, stream table, external table.
    # This handles the little-endian 20-byte-entry variant, rejecting other layouts.
    for path in [p for p in files if p.suffix.lower() == '.pck']:
        rel = path.relative_to(raw)
        blob = path.read_bytes()
        try:
            if blob[:4] != b'AKPK' or struct.unpack_from('<I', blob, 8)[0] != 1:
                raise ValueError('Unsupported PCK format/endian marker')
            language_size, banks_size, streams_size, external_size = struct.unpack_from('<4I', blob, 12)
            offset = 28 + language_size
            for label, size in [('bank', banks_size), ('stream', streams_size), ('external', external_size)]:
                if size < 4 or offset + size > len(blob):
                    raise ValueError('PCK table outside file')
                total = struct.unpack_from('<I', blob, offset)[0]
                if size != 4 + total * 20:
                    raise ValueError('Unsupported PCK entry layout')
                for i in range(total):
                    ident, alignment, length, sector, language = struct.unpack_from('<5I', blob, offset + 4 + i * 20)
                    start = alignment * sector
                    if not alignment or start + length > len(blob):
                        raise ValueError('PCK entry outside file')
                    content = blob[start:start + length]
                    ext = '.bnk' if label == 'bank' else '.wem'
                    base = Path('wwise') / safe_name(path.stem) / f'{label}_{ident}_lang{language}_{i}'
                    write(rel, Path(str(base) + ext), content, 'wwise-' + label,
                          media_id=ident, language_id=language)
                    if label == 'bank':
                        sections = {}
                        pos = 0
                        while pos + 8 <= len(content):
                            tag = content[pos:pos + 4]
                            n = struct.unpack_from('<I', content, pos + 4)[0]
                            if pos + 8 + n > len(content):
                                raise ValueError('BNK chunk outside bank')
                            sections[tag] = content[pos + 8:pos + 8 + n]
                            pos += 8 + n
                        if b'DIDX' in sections and b'DATA' in sections:
                            didx, payload = sections[b'DIDX'], sections[b'DATA']
                            if len(didx) % 12:
                                raise ValueError('Invalid BNK DIDX size')
                            for j in range(0, len(didx), 12):
                                mid, start, length = struct.unpack_from('<3I', didx, j)
                                if start + length > len(payload):
                                    raise ValueError('BNK media outside DATA')
                                write(rel, Path(str(base) + f'_embedded_{mid}_{j // 12}.wem'),
                                      payload[start:start + length], 'wwise-embedded', media_id=mid)
                offset += size
        except Exception as exc:
            error(rel, 'wwise-package', exc)

    # Convert only successfully decoded WEMs; failed files retain their raw bytes.
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    wem_files = (sorted((out / 'wwise').rglob('*.wem'))
                 if (out / 'wwise').exists() and not args.skip_wem_decode else [])
    for index, path in enumerate(wem_files):
        dest = out / 'audio-wav' / path.relative_to(out / 'wwise').with_suffix('.wav')
        dest.parent.mkdir(parents=True, exist_ok=True)
        try:
            result = subprocess.run([ffmpeg, '-nostdin', '-v', 'error', '-i', str(path),
                                     '-c:a', 'pcm_s16le', '-y', str(dest)],
                                    capture_output=True, timeout=30)
            if result.returncode or not dest.exists() or dest.stat().st_size <= 44:
                dest.unlink(missing_ok=True)
                raise ValueError(result.stderr.decode(errors='replace')[:500])
            record(path.relative_to(out), dest, 'audio-wav')
        except Exception as exc:
            error(path.relative_to(out), 'wem-decode', exc)
        if index % 100 == 0:
            print(f'Audio {index}/{len(wem_files)}; WAV={counts["audio-wav"]}', flush=True)
    (out / 'resource-index.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2))
    (out / 'resource-errors.json').write_text(json.dumps(errors, ensure_ascii=False, indent=2))
    (out / 'resource-summary.json').write_text(json.dumps(dict(counts=counts, errors=len(errors),
        unitypy_version=UnityPy.__version__, raw=str(raw)), ensure_ascii=False, indent=2))
    print(json.dumps(dict(counts=counts, errors=len(errors))), flush=True)


if __name__ == '__main__':
    main()
