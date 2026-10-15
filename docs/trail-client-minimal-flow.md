# 原 APK 最小流程：静态适配记录

目标是原客户端启动 → 本地登录 → 角色载入 → 背包显示。当前完成包头、消息体加密、合成 TCP 会话及角色响应验证和静态协议定位；尚未生成修改版 APK，也未验证原客户端连接。手机暂不可连接，按用户要求先完成静态准备。

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

## 验证状态

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
