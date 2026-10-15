# 原 APK 最小流程：适配与运行验证记录

目标是原客户端启动 → 本地登录 → 角色载入 → 背包显示。当前已生成独立包名的 ARM64 测试 APK，在本机 Android 11 模拟器完成真实 HTTP 登录、角色列表和进入游戏消息收发，原 Lua 回调已处理响应。完整场景、角色出生和背包尚未完成，进入游戏后仍停在资源加载并出现原 SDK 网络提示。下文前四轮保留当时的静态证据，最新方案见第五轮。

## 已定位的原生逻辑

分析对象为 `com.netease.ma71` 4.2.0，Unity 2018.4.35f1 / IL2CPP。APK SHA-256 为 `f7deda6deaea54375d63e6f58777c873c1ff41a8d1dd1d04a06d0612716849bc`。

REA 完成 APK/Java 分析；原生全量分析超时后，使用 Il2CppDumper 6.7.46 导出地址，再用 Ghidra 12.1.4 定点反编译。Ghidra 导入基址为 `0x100000`，下表为未加基址的 RVA。反编译是静态证据，不能替代运行验证；共享泛型名称与热更新分支尤其需要核实。

| 入口 | RVA | 静态观察 |
| --- | --- | --- |
| `ServerEntity.SendToServer` | `0x409EEB0` | 递增序号，编码 JSON、加密 body、写入三个网络字节序整数 |
| `ServerEntity.GetPkgLen` | `0x40A3324` | 首个整数为总帧长，拒绝大于等于 `0x100000` 的长度 |
| `ServerEntity.OnPkgRecvFin` | `0x40A346C` | 读取包头，解密 body 后按 UTF-8 解析 |
| `ServerEntity.SendGetSessionKey` | `0x40A24B0` | 会话初始化入口 |
| `ServerEntity.OnReceiveSession` | `0x40A2864` | 读取 `KeyLen`、`SessionKey`；以密钥字符串的 UTF-8 字节初始化收发 RC4 |
| `NetClient.SendPlayerLogin` | `0x38FEFC4` | 操作码 10；登录数据依赖 SDK、平台与设备信息 |
| `NetClient.HandleReceivePlayerLogin` | `0x3900A1C` | `status == 0` 表示成功，进入角色更新；可读取 `WeekFirstLoginTimestamp` |
| `NetClient.SendGetInventory` | `0x390761C` | 连接状态就绪后，以操作码 801 发送空 JSON 对象 |
| `GetInventoryOpt.OnOpt` | `0x39BEB30` | 成功状态为 `status == 0`，读取 `items` 并更新背包、生成槽位物品 |

包头固定为 12 字节，三个字段均为大端 int32：

| 偏移 | 字段 |
| --- | --- |
| 0 | 总帧长，含包头 |
| 4 | 操作码 |
| 8 | 序号 |
| 12 起 | body 字节 |

`legacy/frame-codec.mjs` 仅处理这个包头和不透明 body，支持 TCP 拆包、粘包，限制总长为 12…1048575 字节。它没有连接到现有 HTTP 服务，也没有实现完整游戏协议。

会话操作码为 1，心跳为 2。静态分支显示：RC4 初始化前发送 body 不加密；初始化前接收路径调用 RSA 解密，出现 OAEP 解格式化器。最新反汇编确认 OAEP 标志为 true，已用本地临时密钥实现测试握手；实际客户端密钥替换和运行效果尚未验证。登录成功后的角色必需字段、物品 UID/布局字段、SDK 本地替代入口和热更新行为也未完整确认。因此不能仅返回 `{status:0,items:[]}` 就声称最小流程可用。

## 前四轮验证状态（历史记录，最新结果见第五轮）

- 已通过：原生包头与反汇编交叉检查；6 项包头测试、4 项消息体加密测试、6 项会话/TCP 测试、7 项角色编码/响应测试和既有 12 项服务端测试。
- 已通过：Mac 本地 HTTP 服务健康检查。
- 未运行：原 APK 启动、本地登录、角色载入、背包界面。
- 已通过：使用临时密钥的本地 TCP 会话握手和加密背包消息收发，客户端为合成测试端；还验证了样例登录响应和外观解码。
- 未完成：原 APK 会话接入、登录及角色/物品完整响应、连接地址及测试密钥补丁、重打包与签名。

静态输出保存在忽略目录 `outputs/rea-apk/client-minimal/`。正确基址的原生证据位于 `native-targeted/` 和 `native-payloads/`；`native-targeted-base-mismatch/` 是基址错误的废弃结果，不作为证据。APK、反编译正文、原配置和生成的二进制不提交仓库。

## 手机恢复连接后

先启动本地服务，再运行环境预检：

```bash
node prototypes/trail-server/server.mjs
python3 scripts/check-android-minimal.py \
  --adb work/rea-apk/tools/platform-tools/adb --reverse
```

预检检查授权设备、Android/ABI 信息、本地服务和 `adb reverse tcp:4317 tcp:4317` 后的设备 HTTP 可达性；多设备时传 `--serial`。它不安装或启动 APK，预检通过也不表示游戏协议通过。无设备时退出码为 2，报告仍明确记录游戏步骤 `not_run`。

后续需先完成会话和数据契约，再制作可区分的测试包，记录客户端日志、服务端实际请求及背包显示结果。现有 HTTP 演示只证明原型业务可运行。

## 第二轮静态适配：消息体加密

定点分析新增 `RC4.Init`（RVA `0x40A0D9C`）、`RC4.EncryptNoRound`（`0x40A1264`）、`Pack.InitRSA`（`0x40A061C`）。原生分支使用标准 256 字节 RC4 KSA；初始化后 nX/nY 为零。`EncryptNoRound` 复制 Box，并使用局部计数器执行 PRGA，没有将修改后的 Box、nX、nY 写回实例。因此每个消息体从同一密钥的初始流开始。

`legacy/packet-cipher.mjs` 实现该分支，接受调用方提供的密钥字节，返回新的消息体 Buffer。测试使用公开通用 RC4 已知答案（Key/Plaintext、Wiki/pedia、Secret/Attack at dawn），并验证逐包重置、对称解密、输入不变以及加密背包帧的拆包恢复。最后一项使用合成数据，只验证模块组合，不证明 APK 接受该响应。当前未将此兼容算法用于 HTTP 会话或新协议。

会话请求模型字段为 `timestmp`（原拼写）和 `salt`；响应模型为 `KeyLen`、`SessionKey`。`Pack.InitRSA` 调用私钥解码方法。没有将包内密钥或 salt 复制到服务端或仓库；后续测试应替换为本地测试密钥对并验证 RSA OAEP 参数及长度限制。

新增 `scripts/resolve-il2cpp-native-strings.py`，从 ELF64 文件映射 Ghidra 指针引用，再匹配 Il2CppDumper 字符串表，避免把错误基址或共享泛型显示名称当作字段证据。提取结果仅保存到忽略目录：

```bash
python3 scripts/resolve-il2cpp-native-strings.py \
  --binary outputs/rea-apk/extracted/lib/arm64-v8a/libil2cpp.so \
  --symbols outputs/rea-apk/client-minimal/il2cpp-symbols/script.json \
  --image-base 0x100000 \
  --output outputs/rea-apk/client-minimal/payload-strings.json \
  outputs/rea-apk/client-minimal/native-payloads/*.c
```

角色更新路径出现 `ID`、`gamerID`、`name`、`stamina`、`body_condition`、`characterData`、`dataStore`、`recipes`、`townID`、`homeTownID`、`campLocation` 等字符串；背包 SetInventory 重载出现 `item_slots`。这是字段候选及访问证据，不能直接当作完整 JSON schema：仍须确认必填性、嵌套层级、类型及相关初始化顺序。原客户端登录和界面验证状态仍为未运行。

## 第三轮静态适配：RSA 会话与 TCP 验证

`Pack.Decrypt` 的 ARM64 指令在 RVA `0x40A15D0` 将 w2 设为 1，再跳转到 `RSACryptoServiceProvider.Decrypt`（`0x311B5C0`），确认调用 `Decrypt(body, true)`。这表示 OAEP，见 [Microsoft API 文档](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsacryptoserviceprovider.decrypt)。同包 `RSAOAEPKeyExchangeDeformatter.DecryptKeyExchange`（RVA `0x311D470`）的原始反汇编在 `0x311D4E4` 调用 `get_OaepSHA1`，另一分支在 `0x311D520` 调用 `SHA1.Create`，确认 SHA-1。Ghidra 对此函数出现 PIC/分支恢复警告，因此使用反汇编指令和符号交叉验证。测试实现显式选择 OAEP-SHA1，与 [RSAOAEPKeyExchangeDeformatter 参考源码](https://github.com/microsoft/referencesource/blob/main/mscorlib/system/security/cryptography/rsaoaepkeyexchangedeformatter.cs) 一致。

新增 `legacy/session.mjs` 处理明文 GetSession → RSA 加密响应 → RC4 JSON 消息的状态转换。调用方提供本地测试公钥，每个会话生成独立随机密钥；异常请求关闭会话。响应的序号回显及 KeyLen 取 UTF-8 字节数是测试约定，仍待客户端确认。操作码 10 目前仅有明确标记的合成角色样例，未接入 SDK 身份验证和 SQLite 角色持久化。

运行真实 loopback TCP 演示（无需先启动 HTTP 服务）：

```bash
node prototypes/trail-server/legacy/wire-demo.mjs
```

演示在内存中生成临时 2048 位 RSA 密钥对，监听随机本地端口，完成操作码 1 的握手，再发送操作码 10 的样例登录、解码角色外观，最后交换操作码 801 的加密空背包消息，最后关闭端口。私钥和会话密钥不输出、不保存。结果明确包含 `originalClientFlowVerified: false`、`sdkLogin: not_implemented`。它验证传输组合和样例角色解码，不包含 SDK 登录或游戏场景/界面，也没有把原 HTTP 业务服务直接暴露成原协议服务。

新增六项测试覆盖 OAEP-SHA1（拒绝 SHA256 解密）、UTF-8 密钥、会话隔离、未初始化/重复初始化、异常 JSON/UTF-8、RSA 明文过长，以及真实 TCP 收发。本轮加入下一节角色测试后，全部 35 项测试通过。

### 已确认的物品层级片段

`ItemHelper.CreateItemFromServer(JsonData jsonData, Item parentItem, JsonData itmData)`（RVA `0x392E9F0`）在 itmData 为空时读取 `jsonData.item_data`，要求其为对象，否则返回空。随后从这个对象读取：

| 路径 | 观察到的类型/行为 |
| --- | --- |
| `item_data.basic_id` | int；无值或类型错误时本方法使用 0 |
| `item_data.uid` | string；无值或类型错误时使用空字符串 |
| `item_data.quality` | int，然后转换为 float |
| `item_data.position` | 必须是非空对象，否则本方法返回空 |
| `item_data.position.plug` | 必须是 int，否则本方法返回空 |
| `item_data.position.slot_id` | int，缺失时使用 0 |
| `item_data.position.plug_id` | string，缺失时使用空字符串 |
| `item_data.position.infinite_pos` | int；经过地图位置转换 |

背包递归重载读取外层记录的 `item_slots`，对其调用 SetInventory。上述默认值只说明解析行为，不能证明使用 0 或空字符串能生成有效物品；物品定义、容器类型和资产还需要匹配。登录成功响应只用于明确标记的合成样例；尚不具备完整物品 schema，也未验证原客户端接受该样例。

本轮原生证据在忽略目录 `native-contract/` 和 `native-oaep/`；其中 `InventoryItem_FromJson` 为分析标签，实际方法名是 `ItemHelper.CreateItemFromServer`。`InventoryManager_SetInventoryTop` 单独保存，避免与双参数重载同名覆盖。

## 第四轮静态适配：角色字段与外观编码

`DBPlayer.UpdateByJsonData` 在读取根对象的 `ID` 前有存在性检查，缺失时弹窗并返回。其他大部分字段通过 Contains 与类型检查后赋值，缺失字段往往保留原对象值；这不表示全新角色可以省略这些初始化数据。

| 根字段 | 原生读取方式 |
| --- | --- |
| `ID`、`State`、`campLocation`、`furthestCampLoc`、`townID`、`homeTownID`、`homeTownPlotIndex` | int |
| `stamina`、`infinite_pos`、`body_condition`、`isTrading`、`Gender`、`currentSpeed`、`player_house_level` | int；部分字段随后转换为 float 或 bool |
| `name`、`gamerID`、`characterData`、`dataStore`、`timeUpdated` | string；非空 timeUpdated 交给 DateTime.Parse |
| `recipes`、`UnlockRiverCampIds` | array，元素读取为 int |
| `FurthestCampLocInOtherArea` | array，成员读取 `AreaType` 和 `FurthestCampLoc` |

`stamina` 进入 `NumberUtil.CreateHpHeartNumberFromInt`，`infinite_pos` 进入数值缩放函数。当前构造器要求调用方提供原协议整数值，不把 HTTP 原型的 energy/坐标直接套入。

`LocalPlayer.OnResponseSpawnPlayer`（RVA `0x3A93774`）读取 DBPlayer 偏移 `0x30` 的 state，其来自根字段 `State`：0 进入城镇路径，3 进入旅途路径，4 进入营地路径，5 进入关卡门路径，-1 使用当前旅途状态。场景表、营地 ID 和位置是否有效仍需要资产及实机确认。

### 外观字符串

`SaveGameExtensions.ExToBase64`（`0x38C7374`）序列化角色 protobuf、转换为 URL-safe Base64，再拼接 `;V2` 和 `BarberUtil.Serialise` 的结果。`GetCharacterDataEx`（`0x38C74C4`）执行逆操作。URL 编码的原始反汇编确认 `+`→`-`、`/`→`_`，并去掉尾部 padding。理发扩展为六个十进制整数一组的 UTF-8 文本：

```text
nItemId,nModuleID,nModuleIDEx,nColorValue,nColorValueEx,nSize;
```

该文本也使用 URL-safe Base64。空理发字典对应空后缀。

已从 `ProtoMemberAttribute` 的原生构造调用恢复全部 22 个编号，不能仅凭字段声明顺序推断：gender=1，legs=2，body=3，hands=4，feet=5，hair=6，face=7，jaw=8，ears=9，backpack=10，facialhair=11，fatness=12，eyetype=13，mouthtype=14，browtype=15，pupiltype=16，hat=17，skincolour=18，haircolour=19，dog=20，whiskers=21，nose=22。gender 是 bool，dog 是嵌套 DogData，其余是 int32。

`legacy/character-codec.mjs` 编解码上述字段和 V2 外壳；dog 仅保留不透明 protobuf 字节，不实现宠物业务。解码器有意拒绝未建模标签和非规范输入，范围小于原客户端的通用 protobuf 解析器。为避免空前缀被原生 Split(RemoveEmptyEntries) 丢弃，构造外壳时要求至少一个显式字段。

`legacy/role-response.mjs` 根据已观察到的类型构造样例登录响应，将结构化外观编码为 characterData 字符串，将数据字典编码为 dataStore JSON 字符串。ID、State、campLocation 和 name 必须由调用方明确提供；这是构造器的样例约束，不是完整客户端必填字段声明。它不转发 sauth_json 等 SDK 输入。

真实 TCP 演示新增两个阶段：接收加密的合成登录请求，返回并解码样例角色，然后请求空背包。样例中的场景、营地和外观资源编号都是测试值，未宣称能加载对应 Unity 资源。输出增加 `fixtureAppearanceAssetsVerified: false`、`fixtureSceneVerified: false`，保留 `originalClientFlowVerified: false`。

验证包含独立固定字节向量、负 int32、跨字节 tag、嵌套 dog 字节、V2/理发编码、损坏输入、响应类型、未建模字段拒绝和 TCP 组合流程。另用 Google protobuf 动态描述符按恢复的编号独立编码/解码，结果与 Node 字节一致；这证明格式编码，不证明原客户端的模型资源和出生流程通过。

重现字段编号恢复（LLVM 输出使用原始 ELF RVA）：

```bash
xcrun llvm-objdump -d --start-address=0x203589c --stop-address=0x2035df0 \
  outputs/rea-apk/extracted/lib/arm64-v8a/libil2cpp.so \
  > outputs/rea-apk/client-minimal/character-attributes.asm
python3 scripts/inspect-character-protobuf.py \
  --dump outputs/rea-apk/client-minimal/il2cpp-symbols/dump.cs \
  --assembly outputs/rea-apk/client-minimal/character-attributes.asm \
  --output outputs/rea-apk/client-minimal/character-protobuf.json
```

脚本针对此 APK 类型和属性布局，在无法确定构造参数或字段数量时失败，不猜测编号。本轮原生输出位于忽略目录 `native-role/`、`native-character/`、`native-character-codec/`。下一步需要确认出生场景和资源编号、补齐登录触发的其他请求，并处理原客户端的本地登录入口、地址及测试密钥配置，再重打包验证。

## 第五轮：修改版 APK 的实际连接验证

2026-10-15，已使用原 APK 重打包，而非另写一个诊断 App。产物为忽略目录 `outputs/rea-apk/trail-local-4.2.0-arm64.apk`，独立包名 `com.netease.ma71.local`，显示名称“边境之旅·本地测试”，约 700 MiB，仅 ARM64。保留原 Unity 引擎、场景、资源和业务脚本；使用本地测试签名，签名材料不提交。

### 实际采用的连接路径

解码 `lua.assetbundle` 后恢复了 857 个 TextAsset。`FileEncrypt.GenXorMap` 从元数据中的 256 个 int32 初始化交换映射；解码是先 XOR `0x47` 再查逆表。构建器校验字段数据 SHA-1、原 ARM64 库 SHA-256、UnityFS 头和往返字节，并在改写后重新读取 TextAsset。映射数据和提取脚本正文不提交。

恢复的 `xMain.lua`、`client_net_mgr.lua`、`login_pto.lua`、`net_login.lua` 表明该版本的 Lua 网络层使用 MOS2/NEX。前四轮的 RSA/RC4 模块仍是独立兼容实验，不能代表已验证的运行通路。

本地版在 `NetMgr` 的连接/发送方法接入 HTTP，将已解码的命令与字段 POST 到既有服务器的 `/api/client/message`，响应继续交给原 `NetMgr:OnRecv` 及 `NET_LOGIN` 回调。这是双方明确修改的本地传输适配，不是恢复了 MOS2/NEX 原始字节协议。

ARM64 补丁保留 `CoStart` 中的 Lua bundle 加载，将 `InitUpdaterUI` 替换为调用 `GameStart` 后返回空协程，以跳过远程更新。仅选 `CoStartWithoutHotUpdate` 会漏掉 Lua bundle 加载，不能用于此包。登录场景的 Start 使用本地游客入口，绕过等待原 SDK 登录/远程服务器列表。Unity 的 `Put` 方法在该构建的 xLua 绑定中不可用，因此实际使用现有的 `Post(WWWForm)`，服务端只为此路由解析一个 `message` 表单字段。

### 真实运行证据

设备：本机新建 Android 11 / API 30 / ARM64 模拟器 `emulator-5554`。APK 成功通过 v1/v2/v3 签名验证并安装。通过 `adb reverse tcp:4317 tcp:4317` 访问 Mac 的 loopback HTTP 服务。

首次通过的服务器时间为 2026-10-15T04:01:00Z（北京时间 12:01）。服务端记录顺序为：

| 客户端命令 | 服务端事件 | 原客户端响应处理 |
| --- | --- | --- |
| 258 / C_LOGIN_AUTH | client.auth | 364 / S_LOGIN_AUTH 已处理 |
| 263 / C_REQ_GET_ROLE_LIST | client.roles | 358 / S_GET_ROLE_LIST 已处理 |
| 260 / C_LOGIN_ENTER | client.enter | 360 / S_LOGIN_ENTER 已处理 |
| 1001 / C_AVATAR_CLIENT_DEVICE_INFO | client.device | 空响应，未收集设备信息 |

证据文件均保持忽略：`outputs/rea-apk/client-minimal/local-client-runtime.log`、`local-server-runtime.log`、`android-local-connected-logcat.log`、`android-local-connected.png`。客户端日志由 APK 中运行的 Lua 写入应用目录；服务端日志只记录时间、命令号和事件，不记录会话 token 或 SDK 登录信息。

模拟器为测试包 UID 设置了出站规则，只允许本地 4317 端口。原 SDK/遥测代码仍有后台网络尝试，已在此次隔离测试中被阻断；这不是已从 APK 移除全部外部依赖。实测进入游戏后仍处于资源加载，出现原 SDK 的网络错误提示。**可以确认新 APK 已真实连接本地服务器并处理登录链路；不能确认完整可玩、角色出生或背包显示。**

服务端新增两项测试，全部 37 项通过，覆盖实际表单格式、原有 JSON 格式、游客限制、鉴权、会话过期、角色检查、未知命令和日志脱敏。未知命令返回 501，不伪造游戏成功响应。

### 重建与连接

所需工具安装在 `work/rea-apk/tools/`：JDK 21、Apktool 3.0.3、Android Build Tools 35.0.0，以及带 UnityPy 的 `resource-venv`。构建器只接受已分析 APK 的确切 SHA-256。

```bash
python3 scripts/build-trail-local-apk.py \
  /Users/user/Downloads/com.netease.ma71_4.2.0.apk --reuse-decoded
node prototypes/trail-server/server.mjs
```

首次没有 `work/rea-apk/decoded` 时不传 `--reuse-decoded`。构建器只清理自己解包树中未适配的其他 ABI；不修改输入 APK。APK、原始资源、工具、签名和运行数据不上传 GitHub。

连接可用的 ARM64 Android 测试设备后（将设备序号替换为实际值）：

```bash
work/rea-apk/tools/platform-tools/adb -s DEVICE_SERIAL reverse tcp:4317 tcp:4317
work/rea-apk/tools/platform-tools/adb -s DEVICE_SERIAL install -r outputs/rea-apk/trail-local-4.2.0-arm64.apk
work/rea-apk/tools/platform-tools/adb -s DEVICE_SERIAL shell am start -n com.netease.ma71.local/com.netease.ma71.Ma71
```

本轮仅验证 API 30 模拟器，尚未验证用户手机及其他 Android 版本。手机上的外网隔离规则也未设置。应用默认自动执行本地游客登录，不需要原游戏账号。下一阶段需处理原 SDK 初始化残留、`S_AVATAR_INIT` 所需属性、有效场景/外观资源和新背包容器协议。
