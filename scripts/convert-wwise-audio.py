#!/usr/bin/env python3
"""Convert extracted Wwise Vorbis using caller-supplied ww2ogg and codebooks."""
import argparse
import hashlib
import json
import subprocess
import wave
from pathlib import Path

import imageio_ffmpeg


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('resources', type=Path)
    parser.add_argument('ww2ogg', type=Path)
    parser.add_argument('codebooks', type=Path)
    args = parser.parse_args()
    root = args.resources.resolve()
    converter, codebooks = args.ww2ogg.resolve(), args.codebooks.resolve()
    if not (root / 'wwise').is_dir() or not converter.is_file() or not codebooks.is_file():
        parser.error('Provide extracted Wwise resources, ww2ogg, and packed codebooks')
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    rows, errors = [], []
    files = sorted((root / 'wwise').rglob('*.wem'))
    for index, source in enumerate(files):
        rel = source.relative_to(root / 'wwise')
        ogg = root / 'audio-ogg' / rel.with_suffix('.ogg')
        wav = root / 'audio-wav' / rel.with_suffix('.wav')
        ogg.parent.mkdir(parents=True, exist_ok=True)
        wav.parent.mkdir(parents=True, exist_ok=True)
        try:
            converted = subprocess.run([str(converter), str(source), '--pcb', str(codebooks),
                                        '-o', str(ogg)], capture_output=True, timeout=30)
            if converted.returncode or not ogg.is_file():
                raise ValueError(converted.stdout.decode(errors='replace')[-500:] +
                                 converted.stderr.decode(errors='replace')[-500:])
            decoded = subprocess.run([ffmpeg, '-nostdin', '-v', 'error', '-i', str(ogg),
                                      '-c:a', 'pcm_s16le', '-y', str(wav)],
                                     capture_output=True, timeout=60)
            if decoded.returncode:
                raise ValueError(decoded.stderr.decode(errors='replace')[-500:])
            with wave.open(str(wav)) as audio:
                frames = audio.getnframes()
                if not frames:
                    raise ValueError('Empty decoded audio')
                channels, rate = audio.getnchannels(), audio.getframerate()
                # Read the complete PCM payload to catch truncated WAVs.
                pcm = audio.readframes(frames)
                if len(pcm) != frames * channels * audio.getsampwidth():
                    raise ValueError('Truncated WAV payload')
            rows.append(dict(source=str(source.relative_to(root)), ogg=str(ogg.relative_to(root)),
                wav=str(wav.relative_to(root)), channels=channels, sample_rate=rate,
                frames=frames, duration_seconds=frames / rate,
                wav_sha256=hashlib.sha256(wav.read_bytes()).hexdigest(),
                ogg_sha256=hashlib.sha256(ogg.read_bytes()).hexdigest(),
                decoder_diagnostics=decoded.stderr.decode(errors='replace')[-500:]))
        except Exception as exc:
            wav.unlink(missing_ok=True)
            ogg.unlink(missing_ok=True)
            errors.append(dict(source=str(source.relative_to(root)), error=str(exc)))
        if index % 100 == 0:
            print(f'Vorbis {index}/{len(files)}: converted={len(rows)} errors={len(errors)}', flush=True)
    (root / 'audio-index.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2))
    (root / 'audio-errors.json').write_text(json.dumps(errors, ensure_ascii=False, indent=2))
    print(f'Converted {len(rows)}/{len(files)} WEM files', flush=True)


if __name__ == '__main__':
    main()
