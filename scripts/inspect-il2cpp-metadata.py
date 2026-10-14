#!/usr/bin/env python3
"""Inspect the bounded v24.1 metadata layout observed in this APK, without executing it.

Structure reference: Perfare/Il2CppDumper, Il2Cpp/MetadataClass.cs.
This produces symbol evidence, not executable method bodies or wire schemas.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

SELECTED = {
    'NetClient', 'NetOperation', 'NetOperationType', 'PlayerLoginMsg',
    'PlayerInventory', 'XLuaInventoryItf', 'XLuaTownItf', 'INET_INVENTORY',
    'INET_RECIPE', 'INET_STORE', 'INET_TOWNLANDSCAPE', 'INET_RUNNINGBUSINESS',
    'INet_Pet', 'LoginQueueMgr', 'TradePlayerBuyOpt', 'TradePlayerSellOpt',
}


def inspect(source):
    original = source.read_bytes()
    encoding = 'plain'
    data = original
    if original.startswith(b'mark'):
        key = original[4] ^ 0xAF
        data = bytes(value ^ key for value in original[4:])
        encoding = f'mark prefix + byte XOR 0x{key:02x}'
    if len(data) < 272 or struct.unpack_from('<II', data) != (0xFAB11BAF, 24):
        raise ValueError('Not the supported IL2CPP v24 metadata carrier')

    def section(index, stride):
        offset, size = struct.unpack_from('<II', data, 8 + index * 8)
        if offset < 272 or offset + size > len(data) or size % stride:
            raise ValueError(f'Invalid metadata section {index}')
        return offset, size // stride

    string_offset, string_size = section(2, 1)

    def string(index):
        if not 0 <= index < string_size:
            raise ValueError('String index outside metadata heap')
        start = string_offset + index
        end = data.index(b'\0', start, string_offset + string_size)
        return data[start:end].decode('utf-8', errors='strict')

    type_offset, type_count = section(19, 100)
    method_offset, method_count = section(5, 52)
    field_offset, field_count = section(11, 12)
    parameter_offset, parameter_count = section(10, 12)
    rows = []
    for index in range(type_count):
        value = struct.unpack_from('<19i8H2I', data, type_offset + index * 100)
        name, namespace = string(value[0]), string(value[1])
        if value[21] and not 0 <= value[11] <= field_count - value[21]:
            raise ValueError('Field range outside metadata table')
        if value[19] and not 0 <= value[12] <= method_count - value[19]:
            raise ValueError('Method range outside metadata table')
        if name not in SELECTED:
            continue
        fields, methods = [], []
        for field in range(value[11], value[11] + value[21]):
            member = struct.unpack_from('<iii', data, field_offset + field * 12)
            fields.append({'name': string(member[0]), 'type_index': member[1]})
        for method in range(value[12], value[12] + value[19]):
            member = struct.unpack_from('<11i4H', data, method_offset + method * 52)
            if member[1] != index:
                raise ValueError('Method declaring type does not match selected type')
            if member[14] and not 0 <= member[3] <= parameter_count - member[14]:
                raise ValueError('Parameter range outside metadata table')
            parameters = []
            for parameter in range(member[3], member[3] + member[14]):
                entry = struct.unpack_from('<iii', data, parameter_offset + parameter * 12)
                parameters.append({'name': string(entry[0]), 'type_index': entry[2]})
            methods.append({'name': string(member[0]), 'parameters': parameters,
                            'metadata_method_index': method})
        rows.append({'name': name, 'namespace': namespace, 'type_index': index,
                     'metadata_byte_offset': type_offset + index * 100,
                     'fields': fields, 'methods': methods})
    return {
        'source_path': str(source.resolve()),
        'source_sha256': hashlib.sha256(original).hexdigest(),
        'decoded_sha256': hashlib.sha256(data).hexdigest(),
        'encoding': encoding, 'layout': 'v24.1-compatible (100/52/12-byte records)',
        'counts': {'types': type_count, 'methods': method_count, 'fields': field_count},
        'types': rows,
        'limitations': ['Symbol metadata only; no method bodies, protobuf field tags, '
                        'transport framing, enum wire values, or server algorithms recovered.'],
        'structure_reference': 'https://github.com/Perfare/Il2CppDumper/blob/master/Il2CppDumper/Il2Cpp/MetadataClass.cs',
    }


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('metadata', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    result = inspect(args.metadata)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2))
    print(json.dumps(result['counts']))
