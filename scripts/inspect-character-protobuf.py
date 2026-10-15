#!/usr/bin/env python3
"""Recover this APK's CharacterData ProtoMember tags from IL2CPP attribute assembly.

Input assembly must use raw ELF RVAs (no Ghidra image-base adjustment).
This recognizes only immediate w1 assignments and fails on unrecognized calls.
"""
import argparse
import json
import re
from pathlib import Path


def inspect(dump, assembly, constructor):
    # Select by namespace and exact class, never the unrelated global CharacterData.
    marker = dump.index('public class CharacterData // TypeDefIndex: 4780')
    if dump.rfind('// Namespace:', 0, marker) < 0 or 'TrailProtoBufModel' not in dump[dump.rfind('// Namespace:', 0, marker):marker]:
        raise ValueError('Expected the observed TrailProtoBufModel.CharacterData layout')
    block = dump[marker:dump.index('\n\t// Methods', marker)]
    fields = []
    attributes = []
    for line in block.splitlines():
        if '[ProtoMemberAttribute]' in line:
            address = re.search(r'RVA: (0x[0-9A-Fa-f]+)', line)
            if not address:
                raise ValueError('Attribute RVA missing')
            attributes.append(int(address[1], 16))
        field = re.search(r'public (bool|int|DogData) (\w+);', line)
        if field:
            if len(attributes) != 1:
                raise ValueError('Expected one ProtoMember attribute per field')
            fields.append({'name': field[2], 'type': field[1], 'attribute_rva': attributes.pop()})
    instructions = []
    for line in assembly.splitlines():
        row = re.match(r'\s*([0-9a-f]+):\s+[0-9a-f]{8}\s+(.+)', line)
        if row:
            instructions.append((int(row[1], 16), row[2]))
    for index, field in enumerate(fields):
        start = field['attribute_rva']
        end = fields[index + 1]['attribute_rva'] if index + 1 < len(fields) else start + 64
        w1 = None
        tags = []
        for address, operation in instructions:
            if not start <= address < end:
                continue
            immediate = re.match(r'(?:mov\s+w1,|orr\s+w1, wzr,)\s*#(0x[0-9a-f]+|\d+)', operation)
            if immediate:
                w1 = int(immediate[1], 0)
            elif re.match(r'mov\s+w1, wzr\b', operation):
                w1 = 0
            elif re.match(r'\w+\s+w1,', operation):
                raise ValueError(f'Unrecognized w1 assignment at {address:#x}')
            call = re.match(r'(?:bl|b)\s+(0x[0-9a-f]+)\b', operation)
            if call:
                if int(call[1], 16) == constructor:
                    if w1 is None or w1 <= 0:
                        raise ValueError('Unknown ProtoMember tag')
                    tags.append(w1)
                w1 = None  # Caller-saved register may be changed by every call.
        if len(tags) != 1:
            raise ValueError(f'Expected exactly one constructor call for {field["name"]}')
        field['tag'] = tags[0]
        field['attribute_rva'] = hex(start)
    if len(fields) != 22 or len({field['tag'] for field in fields}) != 22:
        raise ValueError('Expected 22 unique tags for this APK')
    return {'type': 'TrailProtoBufModel.CharacterData', 'fields': fields}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dump', type=Path, required=True)
    parser.add_argument('--assembly', type=Path, required=True)
    parser.add_argument('--constructor', type=lambda v: int(v, 0), default=0x3539410)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = inspect(args.dump.read_text(), args.assembly.read_text(), args.constructor)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2))
    print(f'Recovered {len(result["fields"])} independently read attribute tags')


if __name__ == '__main__':
    main()
