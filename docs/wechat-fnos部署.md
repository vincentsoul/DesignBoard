# 飞牛 OS：Docker 微信

采用 LinuxServer 维护的 `lscr.io/linuxserver/weixin`，在容器中运行微信 Linux 客户端，通过浏览器操作。容器镜像由第三方维护，并非腾讯发布的 Docker 镜像。

参考：[LinuxServer 镜像说明](https://docs.linuxserver.io/images/docker-weixin/)、[镜像 Dockerfile](https://github.com/linuxserver/docker-weixin/blob/master/Dockerfile)。已通过用户登录的 Edge 阅读[知乎文章](https://zhuanlan.zhihu.com/p/1926570416417244599)：原文采用 Webtop Ubuntu XFCE 加官方微信 deb 包。本部署采用同一维护者的微信专用镜像，预先封装桌面环境与官方客户端；其 Dockerfile 从腾讯下载 AppImage，解包安装。原文的 Docker socket 挂载与放宽 seccomp 并非默认必需，本部署不添加。

## 访问和数据

- 使用部署时给出的 NAS 局域网 HTTPS 地址，在浏览器认证窗口填写专用访问账号和密码，然后用手机微信扫码登录。
- 默认使用自签名证书。仅在确认地址为自己的 NAS 后接受浏览器证书提示。
- 部署独立于 DesignBoard，使用独立 Compose 项目和数据目录。
- 容器的 `/config` 持久化到 NAS 的微信项目 `config/` 目录，包含客户端配置及聊天数据，应纳入私人备份，不得加入 Git。
- Compose 文件、`.env` 和访问密码只保存在 NAS 受限目录，不提交仓库。密码不复用 SSH 密码。
- 仅发布 NAS 局域网 IP 上的 HTTPS 端口，没有添加路由器公网转发。需要远程使用时，可通过 VPN 或 SSH 隧道连接。

## 维护

SSH 登录 NAS 后，进入微信项目目录：

```sh
sudo docker compose ps
sudo docker compose logs --tail 100
sudo docker compose restart
```

升级前先停止微信并备份整个 `config/`、项目配置及旧镜像，再执行：

```sh
sudo docker compose pull
sudo docker compose up -d
```

停止服务使用 `sudo docker compose stop`。重建容器不会主动删除 `config/`；删除或清空 NAS 数据目录会丢失本地数据。不要将 NAS 根目录、Docker socket 或其他应用数据挂载到微信容器。

网页是远程桌面，关闭浏览器不等于退出微信。微信是否允许继续登录、聊天同步范围和会话有效期由客户端及微信服务端决定；需要手机扫码、确认或安全验证时由账号本人完成。

## 2026-10-28 部署记录

已在 x86_64 飞牛 NAS 上建立独立微信项目，镜像版本为 `1a7800ae-ls50`，客户端进程报告版本 `4.1.26`。NAS 直连仓库较慢，改由本机下载官方镜像仓库的 amd64 清单与各层，逐层校验 SHA-256，打包后经 SSH 传输；NAS 整包校验通过后用 `docker load` 导入。

已通过 Compose 配置校验、容器运行检查、微信客户端进程检查和 HTTPS 页面认证检查：匿名访问及错误密码均返回 401，正确账号返回 200。通过容器内写入临时探针、NAS 端读取、容器重启后再次读取，验证 `/config` 绑定目录的持久化，随后清理探针。重启后再次通过认证检查，微信进程正常。容器未启用 privileged，仅挂载独立 `/config`，重启策略为 `unless-stopped`；其他既有容器保持运行。

尚未登录用户微信账号，未验证登录后的消息收发、朋友圈、小程序、手机聊天记录迁移或实际聊天数据库恢复。需由账号本人打开桌面并扫码完成微信安全验证。登录前的持久化检查不代表已验证聊天记录迁移。
