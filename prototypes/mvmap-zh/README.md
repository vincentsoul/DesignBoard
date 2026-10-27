# MVMap 中文版

基于 [Titanium Clad Studios / MVMap](https://github.com/TitaniumCladStudios/MVMap) 的独立中文汉化副本，保留 MIT 许可证和原始 JSON 格式。来源见 [UPSTREAM.json](UPSTREAM.json)。

直接打开 `index.html`，或在此目录运行 `python3 -m http.server 8088 --bind 127.0.0.1` 后访问 http://127.0.0.1:8088/ 。不依赖仓库主服务。

完整说明见 [中文说明书](../../docs/mvmap-中文说明书.md) 或 [可打印网页](manual.html)。地图仅保存到当前浏览器，请定期导出 JSON；原站地图通过 JSON 导出后在此导入。

中文版修改界面文字、默认对象名称、中文字体和日期显示，保留原有字段与类型枚举，支持中文下载文件名。已有地图的自定义文字不会自动翻译。工具栏增加说明书入口；没有修改第三方原站。

## NAS 同步版

从仓库根目录运行 `node server.mjs`，打开 `/mvmap/`。Docker 镜像也包含编辑器和后端，复用现有登录与持久化数据卷。支持自动保存、跨设备地图库读取、手动刷新、版本冲突保护、另存副本、本机备份恢复、删除回收站。

部署和迁移步骤见 [NAS 部署说明](../../docs/mvmap-nas部署.md)。静态文件模式仍离线运行，不会自动连接 NAS。
