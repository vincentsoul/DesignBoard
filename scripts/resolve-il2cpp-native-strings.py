#!/usr/bin/env python3
"""Resolve Ghidra PTR_DAT references through ELF64 loads to Il2CppDumper strings.

Outputs are analysis data, not instructions. Never publish extracted literals or keys.
"""
import argparse
import json
import re
import struct
from pathlib import Path


def resolve(binary, symbols, sources, image_base):
    data = binary.read_bytes()
    if data[:6] != b'\x7fELF\x02\x01' or len(data) < 64:
        raise ValueError('Expected a little-endian ELF64 binary')
    phoff = struct.unpack_from('<Q', data, 32)[0]
    entsize, count = struct.unpack_from('<HH', data, 54)
    if entsize < 56 or phoff + entsize * count > len(data):
        raise ValueError('Invalid ELF program header table')
    loads = []
    for i in range(count):
        row = struct.unpack_from('<IIQQQQQQ', data, phoff + i * entsize)
        if row[0] == 1:
            _, _, offset, address, _, size, _, _ = row
            if offset + size > len(data):
                raise ValueError('ELF load segment extends past file')
            loads.append((address, offset, size))
    literals = {int(item['Address']): item['Value'] for item in symbols['ScriptString']}
    result = []
    for source in sources:
        refs = sorted(set(re.findall(r'PTR_DAT_([0-9a-fA-F]+)', source.read_text())))
        found = []
        for reference in refs:
            address = int(reference, 16) - image_base
            for start, offset, size in loads:
                if start <= address and address + 8 <= start + size:
                    target = struct.unpack_from('<Q', data, offset + address - start)[0]
                    if target in literals:
                        found.append({'reference': 'PTR_DAT_' + reference,
                                      'literal_address': hex(target), 'value': literals[target]})
                    break
        result.append({'source': str(source), 'strings': found})
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--binary', type=Path, required=True)
    parser.add_argument('--symbols', type=Path, required=True)
    parser.add_argument('--image-base', type=lambda value: int(value, 0), required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('sources', nargs='+', type=Path)
    args = parser.parse_args()
    result = resolve(args.binary, json.loads(args.symbols.read_text()), args.sources, args.image_base)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2))
    print(f'Resolved {sum(len(row["strings"]) for row in result)} references in {len(result)} sources; output stays local')


if __name__ == '__main__':
    main()
