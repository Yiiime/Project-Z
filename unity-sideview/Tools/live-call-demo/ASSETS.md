# 素材来源

- `ui/assets/manga-live-night.png`：基于用户提供的 Z 漫画直播间设计进行夜间光照编辑，保持人物与场景位置。
- `ui/assets/live2d-empty-room.png`：基于上述夜景使用图像编辑生成的无人小屋，保留夜晚海景、桌面与道具，供 Live2D 角色叠加。
- `avatar-z.svg`、`avatar-wind.svg`、`avatar-moon.svg`：原设计中的头像资产。
- `ui/card-art.js`：本项目新增的代码绘制 SVG 卡背、塔罗牌和奖品插画。
- `ui/app.js` 的 `avatarFor`：为弹幕观众和玩家绘制的内联 SVG 头像，同一昵称使用固定形象。

MIT 许可适用于应用代码。Z 的人物形象和原始设计插画保留原权利归属。

Live2D Haru 是 Live2D Inc. 的官方示例，不是 Z 的专属模型，也不适用本项目的 MIT 许可。使用受官方素材与模型条款约束。当前私人 Demo 的 `ui/live2d/licenses/NOTICE.txt`、`ui/live2d/sources.json` 保存来源与许可证；本次私有仓库接入包保留此目录及原条款；也保留接收者确认协议后重新获取素材的安装脚本。

PixiJS 6.5.10 与 pixi-live2d-display 0.4.0 各自适用 MIT。Cubism Core 使用 Live2D 专有协议，从官方地址加载，不随分享包分发；具体说明见 [Live2D 接入说明](docs/LIVE2D.md)。
