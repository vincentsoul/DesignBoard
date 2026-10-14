#!/usr/bin/env bash
set -euo pipefail

# Keep APKs, tool installations, and generated evidence outside tracked files.
if [[ $# -ne 1 || ! -f "$1" ]]; then
  echo 'Usage: scripts/analyze-apk-rea.sh /absolute/path/application.apk' >&2
  exit 2
fi
: "${JAVA_HOME:?Set JAVA_HOME to a full JDK 17 or newer}"
: "${REA_JADX_MCP_JAR:?Set REA_JADX_MCP_JAR to the verified JADX 0.7.1 JAR}"
: "${REA_CLI_JS:?Set REA_CLI_JS to rea-agents/scripts/rea.mjs}"

root=$(cd "$(dirname "$0")/.." && pwd)
apk=$(cd "$(dirname "$1")" && pwd)/$(basename "$1")
out="$root/outputs/rea-apk/$(date +%Y%m%d-%H%M%S)-$$"
mkdir -p "$out"
expected='6e5eacf500b64292bfb73c49797c1958f6ee44646e43e868039ae7feb573ff75'
actual=$(shasum -a 256 "$REA_JADX_MCP_JAR")
if [[ "${actual%% *}" != "$expected" ]]; then
  echo 'JADX JAR digest differs from REA pinned release metadata.' >&2
  exit 1
fi
"$JAVA_HOME/bin/java" --list-modules | rg -q '^jdk.compiler@'
shasum -a 256 "$apk" > "$out/apk-before.sha256"
export REA_JADX_HEAP_MIB="${REA_JADX_HEAP_MIB:-4096}"
node "$REA_CLI_JS" inspect-artifact "$apk" --format json > "$out/artifact-evidence.json"
node "$REA_CLI_JS" inspect-android-package "$apk" --format json > "$out/package-evidence.json"
shasum -a 256 "$apk" > "$out/apk-after.sha256"
cmp "$out/apk-before.sha256" "$out/apk-after.sha256"
node --input-type=module - "$out" <<'JS'
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
for (const name of ['artifact-evidence.json', 'package-evidence.json']) {
  const evidence = JSON.parse(readFileSync(join(process.argv[2], name), 'utf8'));
  if (!evidence.evidence_id || !evidence.normalized_result) {
    throw new Error(`${name}: analysis did not return successful Evidence`);
  }
}
JS
printf 'Evidence saved to %s\n' "$out"
