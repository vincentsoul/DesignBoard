# 小宇量化（JZhu Trading）飞牛 OS 试用部署

部署日期：2026-10-28。使用[官网部署包](https://static.okweb.com.au/jzhu-ai/download/jzhu-trading.zip)，独立安装于 NAS `/vol1/1000/docker/jzhu-trading`，Compose 项目名为 `jzhu-trading`。

## 访问入口

HTTPS 入口：[小宇量化](https://evantra.cloud:9444/)。访问凭据保存在本机私密文件 `secure/jzhu/访问方式.txt`，不提交 Git。

路由器需配置以下转发，不能复用 QuantDinger 的 9443：

| 协议 | 外网端口 | 内网地址 | 内网端口 |
| --- | --- | --- | --- |
| TCP | 9444 | 192.168.0.126 | 9444 |

独立 Caddy 网关提供可信域名证书和访问密码保护，复用 NAS 已有域名的证书存储与 DNS 续期配置。内部网页端口为 `127.0.0.1:18081`，应用 API 为 `127.0.0.1:18180`；数据库与 AKTools 行情服务不向宿主机发布端口。这些内部端口无需转发至公网。

## 安装方式与隔离

部署包含 TimescaleDB/PostgreSQL、Java 应用、AKTools 行情服务、网页端和 HTTPS 网关。官方镜像来自部署包指定的阿里云镜像仓库；下载后记录并固定镜像摘要，详情保存在 NAS 项目 `installed-images.json`。日常启动不自动拉取 `latest`。

未运行官方 `start.sh` 或 `restart.sh`：它们会停止占用 18080 或 8180 的其他容器，而 NAS 已有其他应用使用 18080。本部署直接使用独立 Compose 项目并修改端口，不执行这种冲突处理。

应用、数据库、行情服务和前端设置了内存上限；Java 堆上限为 1 GiB，应用容器上限为 2 GiB。初次低负载观测全项目约占用 740 MB，数据同步和回测的峰值尚未测试。

## 数据与试用范围

- `data/postgres/` 保存数据库。
- `config/` 保存系统 ID 等配置；系统 ID 关联厂商的试用及订阅识别，应与数据一起备份。
- `db-init/` 是官方数据库初始化文件。
- `backups/` 保存初始数据库与配置备份。数据库备份未做恢复演练，不能据此承诺灾难恢复已经验证。
- NAS 项目中的网关配置和凭据文件按私密配置管理，不进入代码仓库。

未代用户绑定邮箱、购买订阅、运行收费 AI 任务或启动付费回测；未接入券商账户。厂商对试用次数、订阅和市场支持的说明见[常见问题](https://jzhu.net/faq)。A 股定位为研究、回测和信号提醒，不能将本次安装理解为已接入 A 股自动下单。

应用首次启动会初始化股票目录并预加载部分数据，页面可访问不代表全部行情已经同步。AI 输出和回测的具体质量不属于本次安装验证范围。

## 安装验证记录

- 数据库、Java 应用、AKTools、网页端均通过健康检查；HTTPS 网关正常运行，页面标题为“小宇量化 - JZhu Trading”。
- 在 NAS 上以域名 SNI 连接回环 HTTPS 入口，证书校验正常；匿名及错误密码返回 401，正确密码可访问网页和 `/api/web/features`，均返回 200。
- 整个项目停止后重新启动，上述检查再次通过；系统 ID 和数据库股票目录记录数均保持一致。
- QuantDinger 与 DesignBoard 的外网健康检查仍返回 200，微信入口仍对匿名访问返回 401。
- 截至本次安装结束，外网 9444 尚未成功完成 HTTPS 连接，路由器转发待确认；不能将 NAS 本机验证视为已验证公网可达。
- 初始数据库导出成功，但工具对 Timescale 内部循环外键给出提示；尚未验证恢复流程。配置备份与系统 ID 保存在 NAS 私密目录。

没有运行回测或 AI 分析来消耗试用次数，没有验证微信信号推送、具体标的历史行情或订阅功能。

## 维护命令

SSH 登录 NAS 后，进入项目目录：

```sh
cd /vol1/1000/docker/jzhu-trading
sudo docker compose -p jzhu-trading -f compose.yml -f compose.nas.yaml ps
sudo docker compose -p jzhu-trading -f compose.yml -f compose.nas.yaml logs --tail 100 app
```

启动、停止：

```sh
sudo docker compose -p jzhu-trading -f compose.yml -f compose.nas.yaml up -d --pull never
sudo docker compose -p jzhu-trading -f compose.yml -f compose.nas.yaml stop
```

更新前先备份 `data`、`config`、当前镜像摘要及私密部署配置，再检查新版兼容性并更新镜像摘要。不要直接运行官方启动或重启脚本；不要删除 `data/`、`config/` 或共享证书卷。
