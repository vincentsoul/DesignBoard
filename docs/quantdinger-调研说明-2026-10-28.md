# QuantDinger 调研说明

调研日期：2026 年 10 月 28 日（北京时间）  
研究范围：产品定位、主要功能、市场支持、系统架构、部署、成本、许可和试用建议。  
核验方式：查阅官方站点、官方使用文档、GitHub 发布记录及部分配置源码；未安装运行，未连接账户，未验证订单执行或策略收益。本文是软件选型说明，不涉及证券推荐。

## 1. 结论先读

**QuantDinger 是一个可自行部署的 AI 量化交易工作平台，适合把市场研究、Python 策略编写、历史回测和交易运行放在同一套系统里。** 对有 Python 与 Docker 基础、希望掌握部署和数据的个人或小团队，值得做一次受控试用。AI 分析与代码生成降低了起步成本，但仍需要人检查策略逻辑、数据与交易条件。[官方产品站](https://quantdinger.com/)

本次选型判断：

| 使用目标 | 判断 | 原因 |
| --- | --- | --- |
| 学习量化、研究 AI 辅助策略开发 | 适合试用 | 有研究、代码、回测、运行的完整工作流 |
| 自建个人策略研究台 | 值得评估 | 可以控制部署，策略与实验历史可以积累 |
| 直接接入国内 A 股自动实盘 | 暂不能据此选型 | 当前官方 V2 实盘边界未列出国内券商交易通道 |
| 无人值守管理真实资金 | 需先验证 | 回测、账户权限、断线恢复和订单对账是不同问题 |
| 改造成对外收费的交易平台 | 需单独评估 | 涉及前端许可、品牌、多用户隔离及运营成本 |

以上为基于文档的判断，不是运行评测。建议先完成“一个策略、一个市场、一次回测、一次信号模式重启验证”，再决定是否长期部署。

## 2. 项目身份与版本核验

以官网指向的 [OpenByteInc/QuantDinger](https://github.com/OpenByteInc/QuantDinger) 为主要研究对象。搜索结果中存在其他组织仓库和历史地址，不能把不同分支的功能、安装命令与默认配置混用。

| 核验对象 | 本次观察 | 应如何理解 |
| --- | --- | --- |
| GitHub latest release | `v5.7.8`，发布时间 2026-10-27 09:42 UTC，即北京时间 17:42 | 发布接口返回的最新正式发布 |
| 官网与文档首页 | 标注 `v5.7.1` | 页面介绍的版本，不代表所有下载内容相同 |
| 本次读取的 main 提交 | `8b2af21c288d87a5571d67ee6b2bfa860b06a4ba` | 记录源码核验位置 |
| 该提交的 VERSION | `5.6.1` | 与发布页和官网标识存在差异 |

来源：[发布页](https://github.com/OpenByteInc/QuantDinger/releases/tag/v5.7.8)、[文档首页](https://www.quantdinger.com/doc/README.html)、[固定提交的 VERSION](https://raw.githubusercontent.com/OpenByteInc/QuantDinger/8b2af21c288d87a5571d67ee6b2bfa860b06a4ba/VERSION)。

**部署前应固定发布标签和镜像版本，并确认前后端兼容。** 本次未确定上述差异的根因，也没有证明 v5.7.8 镜像包含本文提及的全部功能。架构规划文档中的 V6 内容不作为已交付能力。

## 3. 功能与典型工作流

### 3.1 AI 市场研究

研究流程是选市场和标的、确认行情时间与产品身份、指定分析目标、运行模型任务，再将结果保存或转入策略开发。官方要求检查数据缺失、结论依据和模型分歧。研究报告能帮助形成假设，实际策略仍需写清入场、退出、仓位和风险规则。[AI Research 指南](https://www.quantdinger.com/doc/product/AI_RESEARCH.html)

实际意义：可以更快形成“为什么交易、何时交易”的草案。使用外部模型或行情服务时仍有网络调用；自部署不等于所有内容都留在本机。本文未逐一审计模型请求载荷，敏感策略内容是否外发需在试用中核查。

### 3.2 Python 策略与指标

当前文档以 Strategy API V2 为主要策略合同，围绕策略代码、manifest、编译校验和部署组织开发。manifest 声明市场、标的、周期、预热等执行要求。图表指标与可交易策略的职责需要区分，绘图成功不能作为可下单的证明。[策略开发指南](https://www.quantdinger.com/doc/trading/STRATEGY_DEV_GUIDE.html)

建议先从简单模板开始：固定单一标的，给出明确的开平仓规则和最大仓位。AI 生成代码后，检查是否引用未来数据、是否重复下单、数量单位是否正确，再进入回测。

### 3.3 历史回测

回测应保存代码版本、标的、周期、时间范围、初始资金、手续费与滑点。读结果时先查数据覆盖，再查逐笔成交、最大回撤和基准对比。官方明确指出：Strategy API V2 当前未模拟加密资产资金费；存在历史行情也不代表具备实盘通道。[回测中心指南](https://www.quantdinger.com/doc/product/BACKTEST_CENTER.html)

因此，合约回测收益需要进一步考虑资金费、流动性和清算风险。只保留表现最好的参数，会放大过拟合风险。官网还介绍了参数搜索、滚动验证和保留样本等策略演化能力，但本次未实测优化任务和结果复现。[产品功能介绍](https://quantdinger.com/)

### 3.4 信号、模拟与真实执行

需要区分三种验证目的：

| 环节 | 回答的问题 | 不能证明的事情 |
| --- | --- | --- |
| 历史回测 | 在历史数据和设定撮合条件下，策略怎样表现 | 当前账户和网络是否能执行 |
| signal / 内部虚拟账户 | 实时信号、通知、虚拟成交与权益记录是否正常 | 外部券商真实成交条件 |
| 券商或交易所模拟环境（若该通道提供） | 外部 API、订单生命周期是否按预期工作 | 实盘流动性和收益 |
| 实盘 | 真实账户的订单、成交和持仓是否一致 | 未来表现 |

官网说明，signal-only 虚拟账户不向券商发送真实订单；首次使用指南要求先验证信号与重启状态，再考虑真实执行。各通道模拟环境是否可用，应分别确认。[官网](https://quantdinger.com/)、[首次使用指南](https://www.quantdinger.com/doc/getting-started/FIRST_RUN.html)

## 4. 支持哪些市场

以下依据本次打开的官方实盘安全指南，主要描述 Strategy API V2 的当前边界。账户类型、地区与实时产品目录仍可能进一步收紧范围。

| 市场或产品 | 官方列出的通道 | 关键限制 |
| --- | --- | --- |
| 加密资产 | Binance、Bitget、Bybit、OKX、Gate、HTX | 现货与合约能力取决于平台和账户 |
| 美股券商证券 | Alpaca、IBKR | 当前策略政策为只做多 |
| 交易所股票相关产品 | 部分直接股票、代币化股票、股票永续通道 | 必须区分证券、代币和衍生品；目录与地区限制不同 |
| 其他可解析市场 | 文档未列出对应 V2 实盘通道 | 不能从行情或回测能力推导交易能力 |

文档还明确指出，不支持混合市场实盘部署。产品宣传中的“crypto / stocks / forex”不能直接理解为所有股票和外汇账户都能执行策略。对国内 A 股、港股、外汇，应先拿到目标通道的当前文档和实际账户能力，之后再判断。[实盘支持边界](https://www.quantdinger.com/doc/trading/LIVE_TRADING_SAFETY.html)

## 5. 系统结构与二次开发

当前文档描述的主要结构如下。实际拓扑以所选发布版本的 Compose 为准。

```text
Web / 移动端 / HTTP / MCP
             │
             ▼
        HTTP API 与权限检查
             │
     ┌───────┼─────────┐
     ▼       ▼         ▼
 PostgreSQL  后台任务    交易运行与订单执行
 持久状态    Celery      trading worker
             │         ▲
          Redis jobs   策略分发 / 求值 worker
                       ▲
                 Kafka 运行事件

Redis cache：可淘汰缓存
可选监控：Prometheus / Grafana / Alertmanager
```

HTTP API、有限时长任务和长期交易运行分属不同进程；PostgreSQL 保存业务状态，两套 Redis 分别承担缓存与任务职责，Kafka 承担运行事件流。这样有利于任务隔离与运行恢复，但意味着部署要维护多个组件。[官方系统结构](https://www.quantdinger.com/doc/README.html)

公开 Compose 配置还显示，默认源码部署会本地构建后端，Web 与移动 H5 则使用 GHCR 镜像。**克隆后端仓库并不等于获得全部客户端的同许可源码。** 对二次开发，需另外确认客户端源码、接口兼容与构建方式。[固定提交 Compose](https://github.com/OpenByteInc/QuantDinger/blob/8b2af21c288d87a5571d67ee6b2bfa860b06a4ba/docker-compose.yml)

## 6. 部署方式与成本

### 6.1 建议的试用环境

使用独立目录和 Docker Compose v2，先在本机或测试服务器运行。以下为官方源码部署路径的整理；命令供评估使用，本次未执行：

```bash
git clone https://github.com/OpenByteInc/QuantDinger.git
cd QuantDinger
# 部署前选择并核验一个发布标签，确认对应客户端镜像版本。
python3 scripts/bootstrap_env.py
# 在本机私密编辑 .env，完成管理员与必要服务配置。
docker compose up -d --build
docker compose ps
```

默认入口为桌面端 `http://127.0.0.1:8888`、移动 H5 `http://127.0.0.1:8889`，健康检查为 `http://127.0.0.1:5000/api/health`。官方同时提供安装脚本和预构建镜像部署路径。[部署指南](https://www.quantdinger.com/doc/deployment/CLOUD_DEPLOYMENT_EN.html)

升级前保存数据库备份、当前版本与镜像信息，按目标版本的迁移说明操作。不要通过删除数据卷解决数据库升级问题。

### 6.2 机器与费用

本次没有完成资源压测，不能给出可靠的最低配置或月费。为给测试留余量，可先按 **4 核 CPU、8–16 GB 内存、SSD** 准备试用机，再观察实际占用；这是本文的工程预算建议，不是官方最低配置。并行回测、更多策略、监控及本地模型都会改变需求。

实际成本由以下部分组成：

| 成本项 | 核算方法 |
| --- | --- |
| 机器、磁盘与备份 | 按本地硬件或云账单，计入持久数据和恢复空间 |
| 模型调用 | 按研究任务数、模型和输入输出量计费 |
| 行情、新闻及基本面 | 检查数据商权限、限频与历史范围 |
| 交易成本 | 手续费、点差、滑点，以及适用的资金费 |
| 客户端商业授权 | 按各客户端许可证和实际用途确认 |
| 运维时间 | 升级、告警、备份恢复和异常订单核对 |

这里没有采用官网动态套餐价格，也未把“可以下载部署”解释为全部服务永久免费。

### 6.3 NAS 使用判断

如果计划放在 NAS，先核对 CPU 架构、各镜像平台、内存余量、持久卷权限与到外部服务的连通性。NAS 可作为研究台候选环境；真实执行还需要验证重启、断网、磁盘满和备份恢复。上述是部署建议，本次未在 NAS 上测试，也未验证 ARM 镜像完整性。

## 7. Agent 与 MCP 集成

QuantDinger 提供 Agent Gateway，MCP 服务是其包装层。当前文档列出的 Python 包为 `quantdinger-mcp==0.6.2`，有 58 个工具，覆盖行情、研究工作区、策略、回测及受权限限制的执行相关操作。客户端应使用单独的 Agent Token，并按最小范围授权；不应复用管理员密码。本文只调研该能力，未配置客户端或写入任何令牌。[MCP 接入文档](https://www.quantdinger.com/doc/agent/MCP_SETUP.html)

建议初期只开放行情读取与回测所需能力。将智能体引入系统前，先明确它能读取什么、能修改什么，以及哪些操作必须由人确认。MCP 工具数量不能作为自动交易可靠性的指标。

## 8. 许可与安全：选型前必须明确

### 8.1 后端和客户端分开判断

官方说明后端源码采用 Apache 2.0；Web 前端、移动 H5 和原生客户端分别采用自己的 source-available 许可。商标、名称、Logo、水印与品牌使用也有单独政策。因此，后端开源许可不能直接作为整套产品可任意换标销售的依据。[后端 LICENSE](https://github.com/OpenByteInc/QuantDinger/blob/main/LICENSE)、[官方许可说明](https://github.com/OpenByteInc/QuantDinger#license-and-commercial-terms)、[品牌政策](https://github.com/OpenByteInc/QuantDinger/blob/main/TRADEMARKS.md)

如果用于内部个人研究，重点确认实际使用的客户端版本条款；如果计划修改界面、去除品牌、分发镜像或对外收费，应逐项检查客户端许可证，必要时取得授权。本次未完成客户端许可证逐条审查，未给出商业授权已经满足的结论。

### 8.2 安全能力与未验证部分

官方安全评估列出了凭据保护、租户归属检查、策略执行边界、订单幂等、对账及供应链扫描等控制。但这属于项目公开的安全说明，不能视作本次已经独立验证。外部数据商、模型商和交易平台也有各自的可用性与安全边界。[安全评估](https://www.quantdinger.com/doc/security/SECURITY_ASSESSMENT.html)

试用与后续上线应关注：

- 凭据只保存在私密配置中，不进入 Git、报告或共享截图。
- 真实账户使用受限权限，分清研究、通知和下单授权。
- 验证停止策略、撤单与清仓各自的实际效果，不能默认它们等价。
- 保存策略版本、订单与成交记录，验证异常恢复后的持仓一致性。
- 多用户开放前，独立检查账户隔离、策略执行隔离和资源限制。

涉及真实执行时，应按官方检查单验证标的、账户、限额、状态恢复与应急路径。[实盘安全检查单](https://www.quantdinger.com/doc/trading/LIVE_TRADING_SAFETY.html)

## 9. 建议的首次试用与验收

目标是验证工作流是否适合自己，而非追求一次漂亮的回测收益。以下步骤尚未执行。

| 步骤 | 操作 | 验收标准 |
| --- | --- | --- |
| 1. 固定版本 | 记录发布标签、前后端镜像和配置位置 | 能说明实际安装了什么，数据保存在哪里 |
| 2. 检查数据 | 选一个标的和周期，检查时间、币种及缺失 | 数据身份与覆盖范围清楚 |
| 3. 运行研究 | 完成一次 AI 分析 | 结论能对应到数据，知道哪些内容发给外部模型 |
| 4. 校验策略 | 选择简单 V2 模板，人工检查仓位和信号 | 编译通过，交易规则可以解释 |
| 5. 回测复核 | 固定数据与成本，重复同一测试 | 结果可复核，抽查成交与规则一致 |
| 6. 信号运行 | 保持真实执行关闭，检查通知与虚拟记录 | 触发频率和状态符合预期 |
| 7. 恢复演练 | 重启容器，模拟短暂断网，恢复备份到隔离环境 | 状态能恢复，记录连续，异常可发现 |
| 8. 决定去留 | 汇总缺陷、资源消耗、调用费用与许可证问题 | 能明确下一阶段的投入与阻碍 |

出现以下情况时，应暂停扩大使用：目标市场缺少明确交易通道；成本模型缺项足以改变结论；重启后状态无法核对；客户端授权与用途不匹配；数据范围不足以支持研究问题。

## 10. 最终选型意见

建议将 QuantDinger 列为“AI 辅助量化研究与策略工作台”的试用候选。它的吸引力在于工作流整合和自部署；主要投入在于理解策略合同、验证市场接入，以及维护多组件运行环境。

当前最需要补充的证据是：统一的可部署版本组合、目标市场的实际通道、回测与信号运行的复现结果、异常恢复表现，以及所用客户端的许可范围。完成这些验证后，再决定是否接入真实账户或进行产品化改造。

## 参考资料

以下均为本次调研的一手来源；动态 main 与官网页面可能变化，具体结论以上文核验日期及固定提交为准。

1. [官方仓库](https://github.com/OpenByteInc/QuantDinger)
2. [官方产品站](https://quantdinger.com/)
3. [官方文档总览](https://www.quantdinger.com/doc/README.html)
4. [v5.7.8 发布记录](https://github.com/OpenByteInc/QuantDinger/releases/tag/v5.7.8)
5. [固定提交 VERSION](https://github.com/OpenByteInc/QuantDinger/blob/8b2af21c288d87a5571d67ee6b2bfa860b06a4ba/VERSION)
6. [首次使用](https://www.quantdinger.com/doc/getting-started/FIRST_RUN.html)
7. [AI 研究](https://www.quantdinger.com/doc/product/AI_RESEARCH.html)
8. [策略开发](https://www.quantdinger.com/doc/trading/STRATEGY_DEV_GUIDE.html)
9. [回测中心](https://www.quantdinger.com/doc/product/BACKTEST_CENTER.html)
10. [实盘边界与安全](https://www.quantdinger.com/doc/trading/LIVE_TRADING_SAFETY.html)
11. [部署指南](https://www.quantdinger.com/doc/deployment/CLOUD_DEPLOYMENT_EN.html)
12. [固定提交 Compose 配置](https://github.com/OpenByteInc/QuantDinger/blob/8b2af21c288d87a5571d67ee6b2bfa860b06a4ba/docker-compose.yml)
13. [MCP 接入](https://www.quantdinger.com/doc/agent/MCP_SETUP.html)
14. [安全评估](https://www.quantdinger.com/doc/security/SECURITY_ASSESSMENT.html)
15. [后端许可证](https://github.com/OpenByteInc/QuantDinger/blob/main/LICENSE)
16. [品牌政策](https://github.com/OpenByteInc/QuantDinger/blob/main/TRADEMARKS.md)
