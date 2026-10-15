# 原 APK 最小流程：静态适配记录

目标是原客户端启动 → 本地登录 → 角色载入 → 背包显示。当前完成包头编解码和静态协议定位；尚未生成修改版 APK，也未验证原客户端连接。手机暂不可连接，按用户要求先完成静态准备。

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

会话操作码为 1，心跳为 2。静态分支显示：RC4 初始化前发送 body 不加密；初始化前接收路径调用 RSA 解密，出现 OAEP 解格式化器。RSA 参数、具体填充分支和自定义 `EncryptNoRound` 状态语义尚待核实，不能用猜测的握手替代。登录成功后的角色必需字段、物品 UID/布局字段、SDK 本地替代入口和热更新行为也未完整确认。因此不能仅返回 `{status:0,items:[]}` 就声称最小流程可用。

## 验证状态

- 已通过：原生包头与反汇编交叉检查；6 项包头测试和既有 12 项服务端测试。
- 已通过：Mac 本地 HTTP 服务健康检查。
- 未运行：原 APK 启动、本地登录、角色载入、背包界面。
- 未完成：会话加密适配、登录及角色/物品完整响应、连接地址补丁、重打包与签名。

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
