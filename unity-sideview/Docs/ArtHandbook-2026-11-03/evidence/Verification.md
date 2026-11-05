# 手册验证记录

日期：2026-11-03，Asia/Shanghai。验证对象是文档、读取快照、坐标计算和离线手册，不是新地图的游戏运行验收。

## 覆盖与结果

| 检查对象 | 检查方式 | 结果 |
|---|---|---|
| 资产范围 | 当前Art目录逐图PNG头、导入器和文件读取 | 5张背景、3个场景；角色两张单列记录 |
| 导入状态 | 同一项目的Unity Editor只读查询 | 6000.3.27f1；PPU100、Single、中心pivot、有效Max4096；背景当前导入格式RGBA32 |
| 保存场景 | 解析WorldScene、Transform、BoxCollider与Prefab overrides | Street9、Cafe5、Lighthouse7，共21交互锚点 |
| 数学一致性 | 像素→世界→像素往返；背景Transform与装配公式核对 | 全部通过；不代表视觉碰撞已运行验收 |
| 三地图切换 | Playwright操作离线HTML的select | 表格分别9/5/7行，底图及参数同步 |
| 底图变体 | Street主图/白昼切换 | 图像资源不同，正确更新；Cafe白昼项disabled |
| 镜头与角色 | 拖动滑条、点击锚点、键盘Enter定位 | Street镜头右端x4.44；首个锚点定位x-13.20；Lighthouse镜头固定 |
| 显隐控制 | 地面、锚点、镜头、角色复选项 | 均能更新；关闭锚点后SVG圆标记为0 |
| 图片 | 浏览器检查naturalWidth/complete | 7个参考/封面图正常；地图嵌入显示正常 |
| 离线/桌面 | 本地file URL、1440×1100视口 | 无pageerror，无全页横向溢出 |
| 手机阅读 | 390×844视口 | scrollWidth=innerWidth=390；宽表在表内横向滚动 |
| 文档内容 | 三个完整Markdown渲染区、实际中文检查 | 3份规范均包含在HTML |
| SVG/JSON | XML解析三张guides；JSON解析模板/示例 | 通过 |
| 机械样式检查 | Impeccable detector对HTML模板检查 | 最终[]；不代表主观美术接受 |
| 工作区变化 | 66个被检查源文件哈希复核 | 65个未变；角色SpriteSheetImporter.cs被外部工作更新，见下文 |

## 源码快照边界

`Assets/Seabreeze/Editor/SpriteSheetImporter.cs` 的哈希与初次审计不同，最终读取时有显式手动 `ImportProductionWalk` 入口，源文件修改时间为21:25:56。当前五张地图、三份场景、World/角色显示代码与其他纳入哈希的源文件未变。新增入口未被本任务调用；没有导入生产走路图集或声称验证其行为。原始哈希保留在project-audit.json以便追溯；最终差异另存source-drift.json。

## 人工查看的文档截图

- handbook-desktop.png：封面、参考图库与导航。
- handbook-mobile.png：手机阅读布局。
- map-measurements.png：Street底图上的几何镜头框、地面线、交互编号、角色高度。
- map-explorer.png：地图工具完整区域。拍摄时隐藏了页面粘性导航，防止长区域截图中挡住地图。

这些图是手册和参数图的截图，不是新一轮Unity Play Mode截图。本任务结束前Editor仍stopped，Street已保存且isDirty=false。

## 未验证的事项

未生成新的正式地图资产；未把候选图装配进Unity；未启动Play或构建；未验证新前景/道具/夜雨变体的运行表现；未执行用户美术验收。批量生产应先完成手册中规定的试点闭环。
