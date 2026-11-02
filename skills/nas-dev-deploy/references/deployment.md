# 安全更新现有 Compose 项目

先读取私密连接档案和当前项目画像。下面是 DesignBoard 的示例，其他项目须调整路径、服务、数据备份和验证方式。此流程不能代替用户的部署授权。

## 1. 核对和准备

远端只读检查：目标目录、`docker compose ps`、目标容器挂载和已有端口。读取输出时不要打印 `.env` 或 secret。确认业务正在使用的卷，不把源码仓库 `boards/` 当成生产数据。

本地测试通过并提交后，使用 `package_runtime.py --include ...` 创建归档。它读取 Git 提交，不读取工作区改动。检查生成的 `.manifest.json`：发布提交、运行时成员和文件摘要；私密文件不应出现。

把归档传到管理员拥有的暂存路径，例如 `/home/admin/designboard-release.tar.gz`。辅助工具的 `run --sudo` 只对已审阅脚本使用；脚本本身不包含密码、DNS 密钥或个人数据。

## 2. 备份、构建、替换

示例远端脚本（`archive` 为本次上传文件；路径范围要与实际归档吻合）：

```sh
set -eu
cd /home/admin/designboard
stamp=$(date +%Y%m%d-%H%M%S)
backup=/home/admin/designboard-deploy-backups/$stamp
mkdir -p "$backup"
chmod 700 /home/admin/designboard-deploy-backups "$backup"

# 程序备份的范围随实际发布内容调整；生产 secrets/override 不在归档里。
tar -czf "$backup/program.tar.gz" Dockerfile .dockerignore package.json \
  server.mjs mcp-server.mjs index.html editor.html styles.css portal.css src prototypes

docker image tag designboard:local "designboard:before-$stamp"
docker exec designboard-designboard-1 sh -c \
  'find /data/boards -type f -exec sha256sum {} +' | sort > "$backup/data-before.sha256"
docker exec designboard-designboard-1 tar -czf - -C /data boards > "$backup/boards.tar.gz"

# 归档仅可来自已检查的 package_runtime.py；解压前先列出并确认成员。
tar -tzf /home/admin/designboard-release.tar.gz
tar -xzf /home/admin/designboard-release.tar.gz

docker compose config --quiet
BUILDKIT_PROGRESS=plain docker compose build designboard > "$backup/build.log" 2>&1 || {
  tail -40 "$backup/build.log"
  exit 1
}
docker compose up -d --no-deps designboard > "$backup/recreate.log" 2>&1
cat "$backup/recreate.log"
printf 'BACKUP=%s\n' "$backup"
```

JSON 文件和多个地图文件的在线 tar 不保证跨文件同一时刻快照；要求严格一致性时，先协调暂停写入或使用应用/文件系统一致性快照。数据库应用必须用原生备份机制，不能直接复制正在写入的数据库文件。

构建失败不会替换正在运行的容器。如果本次解压已改变程序文件，按程序备份恢复目录；新引入的文件需根据发布清单单独清理，不能删除整个 src/ 或用户数据目录。

## 3. 上线验收

- 以有限次数检查目标服务 `healthy`，例如最多 12 次、每次间隔 5 秒。每次等待短于 60 秒，并适时向用户报告状态。
- `curl --fail https://<应用域名和端口>/api/health`，默认校验证书，不用 `-k`。
- 公开登录页应可打开，未登录的业务 API 应被拒绝；不要把健康接口公开视为认证绕过。
- 公网变更资源与归档内容比较。浏览器验证对应操作；会写业务数据时使用用户授权的测试数据，并清理本次产生的测试内容。
- 比较数据摘要；发现差异先判断是否有用户并发编辑，不自动还原数据。记录备份位置供回滚使用。
- 删除本次暂存脚本/上传包；保留本次备份和旧镜像，除非用户要求清理。不要执行广泛 prune。

## 4. 程序回滚

使用**本次**实际备份和镜像标签，不照抄过期日期：

1. 保存失败日志；还原本次更新的程序文件，依据清单处理仅由本次新增的文件。
2. `docker image tag designboard:before-<实际时间戳> designboard:local`。
3. `docker compose up -d --no-deps --no-build --force-recreate designboard`。
4. 重新核对健康和原入口。一轮回滚失败则停止破坏性操作并报告。

程序回滚默认不恢复数据库或画布文件；恢复数据备份会覆盖新编辑，需先查明必要性并取得对应授权。Git 保留真实提交历史，修复部署问题应产生新提交，不能为匹配旧部署强推远端。
