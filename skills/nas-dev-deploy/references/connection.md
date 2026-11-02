# 本机 NAS 连接档案

## 已配置的位置

`~/.codex/private/nas/evantra.json`，目录权限 700，文件权限 600，仅当前本机用户可读写。它在 skill 和 Git 仓库之外；同步 skill 时不同步此文件。

档案包含下列字段（这里仅列结构，不放真实凭据）：

- `host`、`port`、`user`：SSH 主机、端口和账号。
- `password`：当前确认可用的 SSH 密码；或使用 `identityFile` 指定现有私钥。不要自行创建或登记新密钥。
- `sudoPassword`：可选；省略时使用 SSH 密码。若无密码且 `sudo -n` 不可用，停止需要提权的阶段。
- `projectDir`：默认项目在 NAS 上的部署目录。
- `serviceUrl`、`managementUrl`：用户访问和 NAS 管理入口。网页管理端口不能当成 SSH 端口。

字段可从最近成功的连接记录恢复，之后以用户提供的新信息为准。创建或更新私密档案时避免把明文密码写进命令参数或脚本文字；使用隐藏终端输入，或用户已授权的凭据存储。不要把档案全文显示到工具结果。

## 连接工具

需要 macOS/Linux、Python 3、系统 OpenSSH；不依赖 sshpass、pexpect 或 npm。

```sh
python3 <skill-dir>/scripts/nas.py check
python3 <skill-dir>/scripts/nas.py run --script /tmp/read-project-state.sh
python3 <skill-dir>/scripts/nas.py upload /tmp/app-release.tar.gz /home/admin/app-release.tar.gz
python3 <skill-dir>/scripts/nas.py run --sudo --script /tmp/upgrade-reviewed.sh --timeout 900
```

`--profile /absolute/path/profile.json` 可使用其他档案。默认只读取个人档案，执行远端脚本和上传操作必须来自用户当前授权的任务。`check` 只读取主机、架构、Docker/Compose 版本和目录状态，不部署、不重启。

工具会先尝试已配置的 SSH 认证，最多应答一次 SSH 密码提示；sudo 使用独立标记。密码不出现在进程参数和输出，输出会在命令结束后统一脱敏。长构建应将详细日志写进 NAS 本次备份目录，适时向用户更新状态。

SSH 主机密钥使用 `StrictHostKeyChecking=yes`。未知主机或密钥变更时停止，先通过可信途径核对指纹；不得用 `StrictHostKeyChecking=no` 绕过。认证失败也不等于“密码一定被修改”，还可能是账号/SSH 配置问题。

## 区分凭据

NAS SSH 密码、应用登录密码、数据库密码和 DNS API 密钥是不同用途。不能因为其中一个密码可用就尝试替代另一个，除非有明确的用户记录或成功登录证据。

当前项目的应用凭据和 DNS 配置留在 NAS 的生产 `secure/` 中。通常部署只需 SSH 权限，不需要读回这些文件。主机上同时运行其他应用，不能为了部署当前项目重置 NAS 账号、全局代理或 Docker 守护进程。
