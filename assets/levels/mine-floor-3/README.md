# 矿洞三层关卡白模 V1

制作依据：[`mine-floor-3-v1.svg`](../../../docs/level-design/mine-floor-3-v1.svg) 和配套机制文档。使用 Blender 5.2.1 生成，1 Blender unit = 1m；坐标转换为 `(X, -Y, Z)`，北为 Blender +Y。

## 文件

- `mine-floor-3-whitebox.blend`：可编辑白模，默认轴测相机。
- `mine-floor-3-isometric.png`：轴测预览。
- `mine-floor-3-plan.png`：正交俯视预览。
- 生成脚本：`tools/blender/build_mine_floor_3.py`，使用本机 Blender 后台运行，不依赖实时插件。

## 内容与组织

包含六个设计区域、4m净宽通道、3m高外围墙、观察窗与4m裂隙、G1支路门、6×6m囚笼和2m笼门、G3塌方、竖井上下层接口，以及1.8m玩家与NPC比例参照。地面由区域并集生成，内部接缝不加墙；墙厚0.25m，布置在净空范围之外。竖井梯槽移至主廊西侧，保留主廊4m净宽。二层入口只保留接口占位对象，不扩建完整二层。

集合：`01_Floors`、`02_Walls`、`03_Mechanisms`、`04_Shaft`、`05_ScaleReferences`、`06_Annotations`、`07_Presentation`。隐藏 `06_Annotations` 可关闭路线、标签和视线辅助。各区域 Bounds 空对象的 `plan_xywh` 属性保存原始尺寸。

查看解锁状态时，可在 Outliner 隐藏 `G1_ChainDoor_HIDE_TO_OPEN`、`G2_CageDoor_HIDE_TO_OPEN`；出口需同时隐藏 `G3_Blocker_HIDE_TO_OPEN` 与全部 `G3_Rock_` 对象。切换 `Camera_Plan` 可核对平面；默认相机为 `Camera_Isometric`。

## 验证范围

已完成米制坐标搭建、地面关键连通段检查、裂隙保留检查、门洞宽度检查，并检查轴测与俯视渲染。检查报告生成在忽略目录 `outputs/mine-floor-3/`。该模型是静态白模，竖井为结构接口示意，尚未实现梯子攀爬、角色控制、引擎碰撞、存档或技能状态。真实角色通行与跟随镜头仍需导入游戏引擎验证。

重建（仓库根目录）：

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/blender/build_mine_floor_3.py
```

脚本会重建此白模文件及两张预览；若已在 Blender 手工修改，请另存文件再重建。
