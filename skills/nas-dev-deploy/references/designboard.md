# DesignBoard 项目部署画像

这些是已验证过的项目约定；每次上线仍检查现状，用户的新指令优先。

| 项目 | 约定 |
| --- | --- |
| 源码 | 当前 DesignBoard 工作区；Git 远端 `vincentsoul/DesignBoard` |
| 私密主机档案 | `~/.codex/private/nas/evantra.json`，包含实际 SSH 和访问地址 |
| NAS 系统 | 飞牛 NAS，x86_64，已有 Docker / Docker Compose |
| 项目目录 | `/home/admin/designboard` |
| Compose 文件 | `compose.yaml` 与 NAS 上自动加载的 `compose.override.yaml` |
| 安全配置源码 | `compose.secure.yaml`；未经本次需求确认不要覆盖生产 override |
| 应用服务 / 镜像 | `designboard` / `designboard:local` |
| 当前容器名 | 通常 `designboard-designboard-1`；以 `docker compose ps` 为准 |
| 网关 | `gateway`，Caddy + 阿里云 DNS 模块；证书由网关自动续期 |
| 数据 | 应用容器 `/data/boards`，包含白板以及 MVMap、LennethMap 数据 |
| 数据卷 | 通常 `designboard_board-data`；以 inspect/Compose 结果为准 |
| 公网 | HTTPS 网关使用宿主机 4173；应用本身不发布宿主机端口 |
| 认证 | 登录表单、会话 Cookie；配置来自 `secure/auth.json` 的只读 secret |
| 健康检查 | `GET /api/health` → `{"status":"ok"}` |

认证配置必须有效才能启动。`secure/auth.json` 应归容器 UID/GID 1000:1000 所有、权限 600；DNS 凭据单独提供给 Caddy。不得从源码“生成默认密码”覆盖已有账号。

## 运行时路径

先检查 Dockerfile；当前 COPY 范围通常包括：

```text
Dockerfile
.dockerignore
package.json
server.mjs
mcp-server.mjs
index.html
editor.html
styles.css
portal.css
src/
prototypes/mvmap-zh/
prototypes/lenneth-map/
```

完整运行时打包示例（最终以当前 Dockerfile 为准）：

```sh
python3 <skill-dir>/scripts/package_runtime.py --repo . --output /tmp/designboard-release.tar.gz \
  --include Dockerfile --include .dockerignore --include package.json \
  --include server.mjs --include mcp-server.mjs --include index.html \
  --include editor.html --include styles.css --include portal.css \
  --include src --include prototypes/mvmap-zh --include prototypes/lenneth-map
```

小修复可只更新明确的变化文件，再从 NAS 现有项目构建。但先确认 NAS 中其他运行时文件和该发布版本兼容；不能用局部更新掩盖版本不一致。

## 曾经踩过的坑

- 服务本地监听 127.0.0.1，容器中需监听 0.0.0.0。镜像已设置 HOST，不要再次开放后端公网端口。
- NAS 管理网页端口、SSH 端口和应用端口各不相同，以私密档案为准。
- 内置浏览器可能不显示 Basic Auth 原生弹窗。此项目已经使用页面登录，不要恢复网关 `basic_auth`。
- 保存是整份 JSON，图片内嵌会放大请求；“返回主页”不能在没有未保存修改时重新上传整个白板。保存失败必须保留当前内容。
- 新增前端 JS 文件除了放进 `src/`，还需登记 `server.mjs` 的静态资源白名单。
- 仅容器健康不代表生产文件正确；可通过公网下载变更后的公开静态脚本并与发布包逐字节比较。
- Node 镜像曾无法从 Docker Hub 下载；曾用官方镜像公共镜像源 `public.ecr.aws/docker/library/node:24-alpine` 下载并本机标记。仅在确实需要且确认镜像来源时采用，不修改其他项目或全局 daemon 配置。
- 仓库经常被其他聊天同时使用。提交和打包不能包含其他聊天尚未提交的文档、脚本或个人资料。
