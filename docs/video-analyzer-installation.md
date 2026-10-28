# Video Analyzer 本机安装

安装日期：2026-10-28。

源码：https://github.com/Creu-Labs/video-analyzer

本机目录：`/Users/user/Documents/designboard/work/video-analyzer`。
源码、虚拟环境、视频和输出均位于主仓库忽略的 `work/` 中。

## 环境与安装复现

使用 uv 创建 Python 3.11 独立环境，不需要 Conda：

```bash
cd /Users/user/Documents/designboard
git clone https://github.com/Creu-Labs/video-analyzer.git work/video-analyzer
cd work/video-analyzer
uv venv --python 3.11 .venv
uv pip install --python .venv/bin/python -r requirements.txt opencv-python pytest
```

## 运行

在项目目录的 `.env` 中自行设置 `OPENAI_API_KEY`，或通过进程环境提供。
密钥不存入本说明，不提交 Git。

```bash
cd /Users/user/Documents/designboard/work/video-analyzer
.venv/bin/python run_video_analyzer.py --video "/绝对路径/video.mp4" --interval 5 --system-prompt "请用中文描述画面内容和前后变化。"
```

`--interval 5` 表示每 5 秒抽取一张图片。默认模型为 `gpt-4o-mini`。
图片位于 `frames/`，带时间戳的分析 JSON 位于 `analysis/`。
运行视觉分析会将抽取的图片发送至模型服务并产生 API 费用。

## 验证结果与限制

- CLI `--help` 正常启动。
- 临时生成 3 秒视频，每秒抽帧，得到 3 张有效图片。
- 使用离线模拟模型响应验证了图片请求构建、上下文传递和结果时间戳。
- 未配置真实 API 密钥，未执行在线模型分析。
- 上游唯一 pytest 测试失败：测试替换了 extractor 模块的方法，却未替换 analyzer 已导入的方法，导致尝试打开不存在的 `fake.mp4`。测试中的模拟客户端接口和结果字段也与当前实现不同。安装未修改上游代码。
- 当前抽帧文件名只精确到整秒，建议使用不少于 1 秒的间隔，避免同秒图片覆盖。
