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
