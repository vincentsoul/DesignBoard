# QuantDinger 飞牛 OS 部署与维护

## 部署目标

本部署独立于 NAS 已有白板、微信等项目。使用官方 v5.7.8 的桌面 Web 与后端镜像，项目位于 NAS 的 `/vol1/1000/docker/quantdinger`。

同版本移动端镜像在部署时返回 `manifest unknown`，因此移动端服务放在可选 profile 中，未混用其他版本。当前入口提供桌面 Web。

## 访问与端口

入口：[打开 QuantDinger](https://evantra.cloud:9443/)。应使用域名访问，以便浏览器校验证书；直接通过 NAS IP 访问不属于本部署的 HTTPS 入口。

路由器仅需转发：

| 协议 | 外部端口 | 内部地址 | 内部端口 |
| --- | --- | --- | --- |
| TCP | 9443 | 192.168.0.126 | 9443 |

9443 由独立 Caddy 网关提供 HTTPS，网页请求转发至前端，`/api` 与 `/api/*` 直接转发至后端。网关复用既有域名证书存储和 DNS 证书续期配置；不改变既有服务端口。

前端调试端口 18888、后端 15000、数据库 15432、缓存 Redis 16379、Kafka 39092 均只绑定宿主机回环地址。任务 Redis 只在 Docker 网络内使用。这些内部端口不需要路由器转发。

## 账号与服务配置

部署生成独立管理员密码并关闭公开注册。管理员凭据保存在本机私密目录 `secure/quantdinger/` 的访问文件中，不进入 Git。运行时密钥和服务配置保存在 NAS 项目的 `.env`，不得复制到公开文档或提交仓库。

未接入交易所或券商账户，未启用真实资金策略。使用 AI 功能还需在应用中配置自己的模型服务与 API Key；本次部署不自动复用其他项目的模型凭据。

## 启停与检查

SSH 登录 NAS 后：

```sh
cd /vol1/1000/docker/quantdinger
sudo docker compose -f docker-compose.ghcr.yml -f compose.nas.yaml ps
sudo docker compose -f docker-compose.ghcr.yml -f compose.nas.yaml logs --tail 100 backend
```

启动现有本地镜像：

```sh
sudo docker compose -f docker-compose.ghcr.yml -f compose.nas.yaml up -d --pull never
```

停止整个项目：

```sh
sudo docker compose -f docker-compose.ghcr.yml -f compose.nas.yaml stop
```

服务使用自动重启策略。不要使用 `down -v`：它会删除项目的数据卷。网关使用的外部证书卷与既有服务共享，也不得删除。

## 持久化与备份

项目使用独立的 PostgreSQL、后端数据、后端日志、任务 Redis 与 Kafka 数据卷。备份范围包括：

- PostgreSQL 的一致性数据库备份。
- 后端数据卷，以及需要保留的任务 Redis、Kafka 状态。
- NAS 项目的 `.env`、Compose 文件与 Caddyfile，按包含秘密的私密文件保存。
- 既有 HTTPS DNS 配置与证书存储，沿用原服务的私密备份方式。

恢复测试应在隔离环境进行。不能只备份程序镜像，也不能直接复制运行中的数据库目录来替代一致性备份。

## 版本升级

当前部署固定 5.7.8。升级前检查目标版本的后端、前端镜像均存在，阅读迁移说明并备份数据库和当前配置。NAS 直连 Docker Hub 曾超时；本次采用本机从官方仓库下载 linux/amd64 镜像、校验各层 SHA-256、打包经 SSH 传输，再校验包并导入的方式。

暂不建议直接切换 `latest` 或运行未核对的安装脚本。升级时保留既有密钥、数据卷和 NAS 覆盖配置，不能重新生成数据库密码后直接连接旧卷。

## 2026-10-28 部署验证记录

安装与整套服务停止后重新启动均成功。迁移、环境同步和 Kafka topic 初始化任务退出码为 0；桌面前端、后端、数据库、两套 Redis、Kafka、交易 worker、调度 worker、Celery worker/beat、策略分发/求值与审计 worker 均通过各自健康检查。HTTPS 网关正常运行。

通过公开域名验证：可信 TLS 证书、前端页面与健康检查返回 200；未登录读取账户信息返回 401；错误密码与历史默认账号密码均返回 401；独立管理员可以登录并读取账户信息；公开注册配置为关闭。整套项目重启后，上述检查再次通过。浏览器可正常显示登录表单；未代用户勾选法律声明或隐私政策。

这验证了当前空白研究环境的安装、认证与重启后访问，不代表已经验证策略历史、AI 调用、交易所行情、券商连接、回测或真实成交。当前没有接入真实资金账户。

低负载状态下本项目容器合计约占用 1.3–1.5 GiB 内存，重启后 NAS 可用内存约 6.9 GiB；这是当时的空闲观测，不是回测峰值承诺。后台任务并发设为 1，部分服务设置了独立内存上限。

已在 NAS 项目 `backups/initial-20261028.dump` 保存初始 PostgreSQL 自定义格式备份，目录权限设为私密；尚未做隔离恢复演练。导入成功的镜像包及本机下载缓存已清理。账号文件、运行配置、连接凭据、数据库备份和安装过程产物均未提交 Git。

参考：[官方部署指南](https://www.quantdinger.com/doc/deployment/CLOUD_DEPLOYMENT_EN.html)、[v5.7.8 部署配置](https://github.com/OpenByteInc/QuantDinger/blob/v5.7.8/docker-compose.ghcr.yml)。
