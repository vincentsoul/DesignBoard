#!/usr/bin/env python3
"""Check Android USB test prerequisites; this does not certify the APK game flow."""
import argparse
import json
import subprocess
import urllib.request
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--adb', type=Path, required=True)
    parser.add_argument('--serial')
    parser.add_argument('--port', type=int, default=4317)
    parser.add_argument('--reverse', action='store_true', help='Map the selected device port to the local server')
    parser.add_argument('--output', type=Path, default=Path('outputs/rea-apk/client-minimal/device-preflight.json'))
    args = parser.parse_args()
    if not args.adb.is_file() or not 1 <= args.port <= 65535:
        parser.error('Provide an existing adb executable and a valid port')
    report = {'original_client_flow_verified': False, 'checks': {}, 'devices': [],
              'game_steps': {name: 'not_run' for name in ['original_apk_launch', 'local_login', 'role_load', 'inventory_ui']}}

    def save(status):
        report['status'] = status
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2))
        print(json.dumps({'status': status, 'checks': report['checks'], 'report': str(args.output)}, ensure_ascii=False))
        return 0 if status == 'preflight_passed' else 2

    def adb(*values, input_data=None, allow_failure=False):
        return subprocess.run([str(args.adb.resolve()), *values], input=input_data,
                              capture_output=True, timeout=15, check=not allow_failure).stdout.decode(errors='replace')

    try:
        with urllib.request.urlopen(f'http://127.0.0.1:{args.port}/health', timeout=3) as response:
            health = json.load(response)
            report['checks']['host_server'] = 'passed' if health.get('result', {}).get('mode') == 'local-prototype' else 'unexpected_service'
    except Exception as exc:
        report['checks']['host_server'] = 'unavailable'
        report['host_error'] = str(exc)
    try:
        inventory = adb('devices', '-l')
        for line in inventory.splitlines():
            columns = line.split()
            if len(columns) >= 2 and columns[1] in ['device', 'unauthorized', 'offline', 'recovery', 'sideload']:
                report['devices'].append({'serial': columns[0], 'state': columns[1], 'description': columns[2:]})
    except Exception as exc:
        report['checks']['adb'] = 'failed'
        report['adb_error'] = str(exc)
        return save('adb_failed')
    devices = [device for device in report['devices'] if device['state'] == 'device' and (not args.serial or device['serial'] == args.serial)]
    if len(devices) != 1:
        report['checks']['adb_device'] = 'missing' if not devices else 'selection_required'
        return save('device_not_ready' if not devices else 'select_one_device')
    serial = devices[0]['serial']
    report['checks']['adb_device'] = 'passed'
    report['selected_serial'] = serial
    try:
        report['device'] = {name: adb('-s', serial, 'shell', 'getprop', prop).strip() for name, prop in {
            'android_sdk': 'ro.build.version.sdk', 'abi_list': 'ro.product.cpu.abilist', 'model': 'ro.product.model',
        }.items()}
        package = adb('-s', serial, 'shell', 'pm', 'path', 'com.netease.ma71', allow_failure=True).strip()
        report['device']['original_package_installed'] = package.startswith('package:')
        if not args.reverse:
            report['checks']['device_to_host_http'] = 'not_requested'
            return save('usb_ready_http_not_checked')
        if report['checks']['host_server'] != 'passed':
            return save('start_local_server_first')
        adb('-s', serial, 'reverse', f'tcp:{args.port}', f'tcp:{args.port}')
        packet = b'GET /health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n'
        response = adb('-s', serial, 'shell', '-T', 'toybox', 'nc', '-w', '3', '127.0.0.1', str(args.port), input_data=packet)
        separator = '\r\n\r\n' if '\r\n\r\n' in response else '\n\n'
        header, payload = response.split(separator, 1)
        result = json.loads(payload)
        if ' 200 ' not in header.splitlines()[0] or result.get('result', {}).get('mode') != 'local-prototype':
            raise ValueError('Device HTTP response is not the local prototype health check')
        report['checks']['device_to_host_http'] = 'passed'
        report['reverse_mapping'] = f'tcp:{args.port} -> host tcp:{args.port}'
        return save('preflight_passed')
    except Exception as exc:
        report['checks']['device_to_host_http'] = 'failed'
        report['device_error'] = str(exc)
        return save('device_check_failed')


if __name__ == '__main__':
    raise SystemExit(main())
