# 使用 REA 静态分析 APK

项目：[morluto/rea](https://github.com/morluto/rea)。Android 使用方法见[官方文档](https://github.com/morluto/rea/blob/main/docs/android-analysis.md)。

本次使用 rea-agents 6.4.0、完整 JDK 21 和 jadx-headless-mcp 0.7.1。工具位于本地忽略目录 `work/rea-apk/tools/`，结果位于 `outputs/rea-apk/`。APK、第三方工具和分析输出不提交到 Git。

`scripts/analyze-apk-rea.sh` 接受一个 APK 路径，验证 JADX 固定校验值和 JDK 编译模块，调用 REA 的 artifact/package 静态分析，检查 Evidence 返回结构，并确认原 APK 的 SHA-256 没有改变。

```bash
export JAVA_HOME="$PWD/work/rea-apk/tools/jdk-21.0.12.1+1/Contents/Home"
export REA_JADX_MCP_JAR="$PWD/work/rea-apk/tools/jadx-headless-mcp-0.7.1-all.jar"
export REA_CLI_JS="$PWD/work/rea-apk/tools/rea/package/scripts/rea.mjs"
bash scripts/analyze-apk-rea.sh /absolute/path/application.apk
```

依赖 Node.js、`rg`、`shasum` 和完整 JDK。目录名可能随 JDK 更新而变化。可通过 npm 安装官方 `rea-agents` 包，并将 `REA_CLI_JS` 指向实际安装路径。

进一步分析使用 `search-android-classes`、`inspect-android-class`、`inspect-android-method` 和 `trace-android-references`。先枚举成员，再按具体签名选择重载。每次 CLI 请求会独立载入 APK，大型包需要额外时间和内存。

Manifest 权限是声明，不代表实际调用；反编译代码是派生结果。上述步骤不运行 APK，不验证签名，也不恢复 Unity IL2CPP 原生游戏逻辑。原生分析及资源解码需要对应工具和独立验证。

## 提取和转换资源

先调用 REA 的 `extract-artifact`，将返回的 `normalized_result.output_root` 移至本地忽略目录，并保存路径映射。提取 Evidence 的 `artifacts` 列表包含每个原始文件的相对路径、大小和 SHA-256，可逐项检查移入后的文件。

REA 负责原始文件提取。补充转换使用 [UnityPy](https://github.com/K0lb3/UnityPy) 1.25.4、Pillow 和 imageio-ffmpeg，安装在独立的本地 Python 虚拟环境中：

```bash
python3 -m venv work/rea-apk/tools/resource-venv
work/rea-apk/tools/resource-venv/bin/python -m pip install UnityPy==1.25.4 pillow imageio-ffmpeg
work/rea-apk/tools/resource-venv/bin/python scripts/export-apk-resources.py \
  outputs/rea-apk/raw-apk outputs/rea-apk/resources --skip-wem-decode
```

输出目录必须尚不存在，脚本按文件内容识别直接图片，按 Unity 对象类型导出 Texture2D、Sprite、Mesh、TextAsset、Font 和可解码 AudioClip。文件名包含源文件标识与对象 ID；清单保留来源、输出路径和 SHA-256。OBJ 是单独网格，不包含完整场景、骨骼动画或材质绑定。原始 AssetBundle 保留在解包目录中。

Wwise 支持本次验证的 little-endian AKPK、20 字节表条目和 BNK DIDX/DATA 布局；遇到其他格式会记录错误并保留原文件。它将 PCK 内的 stream 和 BNK 内嵌媒体拆成 WEM。FFmpeg 不能直接转换 Wwise Vorbis 时，使用官方 [ww2ogg](https://github.com/hcs64/ww2ogg) 编译产物及其 codebooks：

```bash
work/rea-apk/tools/resource-venv/bin/python scripts/convert-wwise-audio.py \
  outputs/rea-apk/resources work/rea-apk/tools/ww2ogg/ww2ogg \
  work/rea-apk/tools/ww2ogg/packed_codebooks_aoTuV_603.bin
```

该脚本先转 OGG，再转 PCM WAV，检查音频帧数和完整 PCM 长度。`audio-index.json` 包含声道、采样率、时长和哈希。它不会将 Wwise 事件组合、混音、循环和运行时状态重建成完整游戏音效系统。初始直接解码失败记录仍保留在 `resource-errors.json`；后续转换成功与否以 `audio-index.json` 和 `audio-errors.json` 为准。
