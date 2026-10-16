# 本地游戏服务端原型

根据 APK 提取的 IL2CPP 客户端符号拆分业务模块，使用原创 HTTP/JSON 接口实现本地验证。当前包含 8 个模块、17 个变更命令，并用 SQLite 保存角色、交易、小镇、奖励领取和请求回执。

功能证据来自客户端类、字段、方法和参数名。原游戏服务器代码、完整消息契约、实际经济规则及 SDK 登录校验尚未恢复。包头与部分 JSON 字段已另行静态定位，见 [原客户端适配记录](../../docs/trail-client-minimal-flow.md)。`GET /api/capabilities` 返回各模块的客户端符号和原型假设；`catalog.mjs` 中的物品、价格、配方、初始状态和奖励都是演示数据。

已生成原 APK 的独立本地测试版，并在 Android 11 ARM64 模拟器实际完成游客登录、角色列表和进入游戏消息收发。它通过 `client/local-transport.lua` 将原 Lua 网络管理器接入本服务的 HTTP 桥接，继续使用原登录响应回调；不是原 MOS2/NEX 字节协议。角色出生、资源加载、背包和原 SDK 残留依赖仍待适配，当前不能完整游玩。重建、安装和真实日志记录见上述适配记录第五轮。

产品定位、核心玩法、功能规则、数据与存档要求、开发阶段和验收基线见 [本地复现版产品设计文档 PDD 1.0](../../docs/trail-local-product-pdd-2026-10-16.md)。该文档区分运行确认、静态证据和后续设计要求。

## 运行

在仓库根目录使用 Node.js 24.x（或 26+），无需安装 npm 依赖：

```bash
node prototypes/trail-server/server.mjs
```

默认监听 `http://127.0.0.1:4317`。健康检查是 [GET /health](http://127.0.0.1:4317/health)。数据库默认位于仓库忽略目录 `outputs/trail-server/game.sqlite`，停止进程后再次启动会继续使用该数据库。`Ctrl+C` 可停止服务器。

可用 `LOCAL_GAME_PORT` 和 `LOCAL_GAME_DB` 选择端口与本地数据库路径；监听地址只允许 loopback。SQLite 文件、测试输出、会话和角色数据不提交到 Git。

另开一个终端运行完整演示：

```bash
node prototypes/trail-server/demo.mjs
```

演示创建 Alice 和 Bob，完成制作、仓储移动、商店购买、建镇、入镇、贡献、摆摊买卖、周期奖励重试、公告更新、物品使用和存档。每次演示会创建两个新的本地角色。返回结果不输出会话 token。

## HTTP 接口

响应成功为 `{ "ok": true, "result": ... }`；失败为 `{ "ok": false, "error": { "code": "...", "message": "..." } }`。

| 接口 | 用途 |
| --- | --- |
| `GET /health` | 服务健康与服务器时间 |
| `GET /api/catalog` | 演示物品、配方、价格与规则 |
| `GET /api/capabilities` | 已实现命令、证据和假设 |
| `POST /api/session` | 创建本地游客角色和 24 小时会话；body 为 `{ "name": "旅人" }` |
| `POST /api/client/message` | 修改版 APK 的本地 HTTP 桥接；JSON 或单个 `message` 表单字段，登录之外需要本地会话 token |
| `GET /api/player` | 当前角色状态、存档版本和小镇成员身份 |
| `POST /api/heartbeat` | 服务器时间和角色版本 |
| `POST /api/commands` | 执行业务变更命令 |
| `GET /api/trades` | 前 100 条有效摆摊记录 |
| `GET /api/towns?search=...` | 按名字查询小镇，最多返回 100 条 |
| `GET /api/towns/:id` | 小镇、公告、成员、资金与版本 |
| `GET /api/towns/:id/ranking` | 小镇贡献排行 |
| `GET /api/events?after=0` | 当前角色的变更事件，最多 100 条；使用 `nextCursor` 翻页 |

后三类读取接口与所有角色变更都使用 `Authorization: Bearer <本地会话 token>`。新游客会话不识别原游戏账号。会话到期后需创建新游客；此版没有长期账号和会话续期功能。

每个变更命令都要求 `Idempotency-Key` 请求头，以及当前 `expectedRevision`：

```json
{
  "operation": "craft.make",
  "expectedRevision": 0,
  "args": { "recipeId": "rope", "quantity": 2 }
}
```

正常结果包含更新后的 `player`。原样重试相同请求 key 和 payload 会返回已保存结果，`replayed: true`，不会重复扣款或发物品。该结果是首次执行时的快照，之后的最新状态需读取 `/api/player`。同一 key 配不同 payload 返回 `409 idempotency_conflict`；使用过期角色版本返回 `409 revision_conflict`。买方完成交易后卖方的余额和版本也会改变。

## 变更命令

| operation | args | 行为 |
| --- | --- | --- |
| `player.rename` | `name` | 修改当前角色名 |
| `player.checkpoint` | `campId`, `position: {x,y}` | 保存三个演示营地之一的位置 |
| `inventory.move` | `itemId`, `quantity`, `from`, `to` | 在 `backpack` 和 `storage` 间移动 |
| `inventory.drop` | `itemId`, `quantity`, 可选 `container` | 删除指定数量；默认背包 |
| `inventory.use` | `itemId`, `quantity` | 消耗浆果或口粮，补充体力，上限 100 |
| `craft.make` | `recipeId`, `quantity` | 按服务端配方扣材料和发产物 |
| `shop.buy` | `itemId`, `quantity` | 按服务端单价购买 |
| `reward.weekly` | `{}` | 按上海时区、周一边界每周领取一次 |
| `trade.list` | `itemId`, `quantity`, `unitPrice` | 将背包物品转入交易托管 |
| `trade.buy` | `listingId` | 购买整条记录，原子完成买方扣款、卖方收款和物品转移 |
| `trade.cancel` | `listingId` | 卖家取消有效记录并退回托管物品 |
| `town.create` | `name`, 可选 `rules` | 建镇并成为镇长 |
| `town.join` | `townId` | 占用一个空地块，每个角色只加入一个镇 |
| `town.update` | `townId`, `expectedTownRevision`, 可选 `name` / `rules` / `announcement` | 镇长更新小镇信息 |
| `town.contribute` | `amount` | 扣除个人货币，增加镇资金与贡献分 |
| `town.leave` | `{}` | 普通成员离镇；贡献记录随成员身份移除 |
| `town.dissolve` | `{}` | 镇长解散镇，移除全部成员身份；镇资金不退还 |

## 证据对应与推断边界

| 模块 | 客户端符号证据 | 原型选择 |
| --- | --- | --- |
| 登录/心跳 | `NetClient.SendPlayerLogin`、`SendHeartBeat`、`PlayerLoginMsg` | 本地游客 token；未实现 SDK、登录队列、账号冲突 |
| 背包 | `PlayerInventory`、`SendGetInventory`、`XLuaInventoryItf.MoveItemBetweenContainer`、`INET_INVENTORY.Send_DROP_ITEM` | 按物品类型堆叠；未重建原物品 UID、装备品质和背包物理布局 |
| 制作 | `INET_RECIPE` 的熟练度/升级接口，`PlayerInventory.recipes` | 制作流程根据配方领域推断；成本和产量为原创演示规则，原制作请求尚未识别 |
| 交易 | `SendTradePlayerSell(playerId,item_uid,posX,posY,callback)`、`SendTradePlayerBuy(itemUId,playerId,callback)` | 固定价格托管交易；未实现原营地限时交易场景 |
| 小镇/房屋 | `Send_CREATE_OWN_TOWN`、`Send_C_TOWN_RENAME`、`Send_C_CHANGE_TOWN_RULES`、`SendUpdateBlackboard`、`SendBuyPlot` | 8 地块、镇长权限；未实现装修、家具同步和房屋升级 |
| 资金/排行 | `Send_C_EXCHANGE_TOWN_FUNDS`、`Send_C_REQUEST_CONTRIBUTION_RANK` | 货币与贡献按 1:1 演示 |
| 奖励 | `INET_TOWNLANDSCAPE.Send_C_GET_WEEKLY_REWARDS` | 一周一次，额度/资格/时区为原型选择 |
| 后续候选功能 | `INet_Pet`、`INET_RUNNINGBUSINESS`、`INET_TOWNLANDSCAPE` 等 | 宠物、跑商、景观布置等尚未实现 |

用以下命令复现本地元数据证据（不运行 APK，输出保持忽略）：

```bash
python3 scripts/inspect-il2cpp-metadata.py \
  outputs/rea-apk/raw-apk/assets/bin/Data/Managed/Metadata/global-metadata.dat \
  outputs/rea-apk/server-analysis/metadata-evidence.json
```

解析器针对此 APK 中实测的 v24.1 布局，并校验字符串和成员表边界。结构参考 [Il2CppDumper MetadataClass.cs](https://github.com/Perfare/Il2CppDumper/blob/master/Il2CppDumper/Il2Cpp/MetadataClass.cs)。它导出类型、字段、方法和参数名，不能代替原生函数反编译，也不能确定 protobuf 编号或网络消息字节布局。

## 验证

```bash
node --test prototypes/trail-server/test/*.test.mjs
```

37 项测试包含 2 项 APK HTTP 桥接测试、7 项角色编码/响应测试、6 项会话/TCP 测试、4 项消息体加密测试和 6 项原协议包头测试（字节序、拆包、粘包、长度限制和截断），其余覆盖 HTTP 完整流程、未授权访问、非法 JSON/请求体、货币修改拒绝、版本冲突、幂等重试、制作与交易回滚、并发购买、卖家取消、小镇权限/容量、上海周边界、会话过期、事件隔离以及 SQLite 重启。

实现为单个本地进程，业务变更用 `BEGIN IMMEDIATE` 事务完成，所有 SQL 参数使用绑定。服务端不调用原游戏服务器或 SDK。构建器从输入 APK 生成独立测试包，输入文件保持不变；提取内容、原配置、APK、签名与会话不提交。HTTP 接口由本原型定义，仅修改后的客户端增加了适配；未修改的原 APK 不能直接连接。


原协议传输实验可独立运行 `node prototypes/trail-server/legacy/wire-demo.mjs`。它使用临时密钥和合成客户端验证 RSA 握手、样例登录角色解码与 RC4 背包帧收发，运行完即关闭；不执行 SDK 登录或原 APK。当前适配范围和字段证据见 [原客户端适配记录](../../docs/trail-client-minimal-flow.md)。
