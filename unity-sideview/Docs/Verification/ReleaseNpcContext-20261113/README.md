# NPC 地点感知、独立记录和直播字段发布验收

日期：2026-11-13。发布分支：`task/evanc4823-commits/npc-perception-records-live-expression-1`。PR：https://github.com/Yiiime/Project-Z/pull/64。融合主线 `c1df9ae`。用户授权 Memory 目录；独立第七晚结局 PR #58 保持草稿。

## 实现与行为

- 每个 NPC 按世界、存档时间线和 actorId 隔离环境快照、持久上报队列、确认序号和追加式运行记录；生成提案与实际执行回执分开记录，读档撤销存档之后的记录。
- 初始化、移动开始/到达/受阻/中断、动作变化、对话打开/关闭和卸载场景时上报实际地点及坐标；下一次决策携带最新位置。环境变化后的过期回复不能提交，NPC A 的队列不阻塞 NPC B。
- 明确玩家与 Z 的身份和记忆边界，点名路由到对应角色，其他 NPC 的回复不会把给 Z 的消息标记为已回答；接受同行后持续跟随，聊天暂停、结束恢复，并保留跨场景路线及真实码头位置。
- 直播 host.present/audience.present 增加稳定 actorId、delivery 和角色专属 expression。字幕只有正文，delivery 进入现有 TTS 发声参数，expression 进入 Live2D；此新增 delivery 功能用于直播，日间已有主线语音能力保留。
- 同步最新 main 的首日分组、对话任务卡、日间语音及场景准备流程。

参考 K04/K12：保留个人经历因果和理解/决定/执行/完成分层，采用实际宿主回执，未移植原作数值门槛。

## 验证

- MemoryLab：145/145；直播服务：157/157。
- tools/team/z check：仓库规则、团队自动化、资源引用通过；6486 个资源文件、3328 个 GUID、32 个场景；333 个 C# 文件语法检查无错误。
- Unity 6000.3.27f1 Play Mode：融合最新主线 c1df9ae 后，NPC 真实移动、标题继续入口、对话窗口、独立 HTTP 队列和读档回滚 12/12；delivery/Live2D 渲染、表情切换与恢复、中立表情、字幕和身份隔离 37/37。
- 范围：171 个文件全部在所选区域与额外授权 Memory 目录内；无 EndingDemo/SeventhNight 改动。报告与截图位于 unity-sideview/Docs/Verification/ReleaseNpcContext-20261113/。

## 未验证部分

Unity 验收使用隔离存档与模拟模型，表情验收使用离线文字播放；未实测生产 TTS/RTC/ASR、真实模型台词质量、所有场景同行路线和完整首日/七日剧情。地点沿用现有导航锚点，精确门内/门外区域判定未新增。
