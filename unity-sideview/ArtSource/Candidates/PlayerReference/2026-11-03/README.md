# 主角标准侧面参考 · 候选 v02

状态：等待用户确认角色形象。尚未批准为正式动画身份基准，未接入 Unity，未生成 Walk Cycle。

## 交付

- 文件：player-side-reference-v02.png
- 路径：/Users/bytedance/Desktop/unity_z/ArtSource/Candidates/PlayerReference/2026-11-03/player-side-reference-v02.png
- 画布：836 × 1882 px，PNG，8-bit RGBA。
- 人物有效高度：1679 px（alpha ≥ 32 的包围盒，左上原点 x256–574、y104–1782）。
- 有效轮廓上／下留白：104 / 99 px；头顶、双手、两只鞋完整。
- SHA-256：0870a9359826a823d36ca315fa00e1e57e0be26409112904fdb52add10948e8f
- 方法：内置 image_gen 参考重绘，随后一次限定脸部／透明边缘修正。未使用 CLI/API 备用路径，未用插值放大或后期缩放冒充高清。
- 最终生成源：/Users/bytedance/.codex/generated_images/01a19ffe-9fca-7851-b6d8-e4e40bd9b854/exec-43455d36-111e-4d7d-9470-9712113a2ba2.png
- 初版生成源（工作过程留档，非另一张交付候选）：/Users/bytedance/.codex/generated_images/01a19ffe-9fca-7851-b6d8-e4e40bd9b854/exec-b0d7fa9d-3414-4288-9f36-2caf324da776.png

## 参考角色分工

1. 唯一身份／比例／站姿依据：/Users/bytedance/Desktop/unity_z/Assets/Seabreeze/Resources/Art/player_walk.png 第一列 walk_0。首轮仅把该图片传给生成工具，并明确限定第一列。
2. 仅人工辅助核对服装：/Users/bytedance/Desktop/unity_z/Assets/Seabreeze/Resources/Art/characters.png。没有把它作为生成工具的另一套身份或比例依据。
3. 场景色彩／形象核对：/Users/bytedance/Desktop/unity_z/美术资产参考/场景/image 8.png，以及 /Users/bytedance/Desktop/unity_z/Docs/Verification/street-final.png。未将场景里的插兜站姿引入候选。
4. 修正轮输入：首轮候选 + 原 player_walk 图集，原 walk_0 仍是唯一身份依据。

## 对照结论与限制

- 保留黑蓝短发、可见近侧眼耳、窄脸年轻男性、约六头身的纤细身形。
- 蓝灰敞开翻领夹克、窄条浅色内搭、长袖收口、斜插袋、深色长裤与浅灰系带低帮鞋均对应。
- 向右安静站立，手臂在身侧、未插兜；没有添加背包、武器、饰品、额外角色或文字。
- 未发现明显的八头身化、加宽肩膀或拉长腿；仍保留原图窄幅胸前开口与远侧手／鞋可见的游戏侧视表现。
- 首轮眼睛偏大、脸颊偏柔，且鼻颈处有彩色透明边缘；第二轮限定修正这些问题。
- 复核第二轮的可见主要改善是鼻颈黄红彩边减少；脸部相对首轮变化轻微。与 walk_0 相比，眼睛和脸颊仍略圆柔，鞋底略厚、较规整，等待用户判断是否接受这些细节差异。
- 生成式重绘不能视为逐像素同一轮廓：脸部线条、衣褶和鞋底细节仍有小差异。高倍查看鞋边等处仍有极少量边缘杂色，不能称为已经完成最终抠图清理的游戏 Sprite。
- 本轮仅供角色形象确认，没有宣称得到用户验收，也没有更改原资源或自动进入动画制作。

## 原文件保护

生成前后校验一致：
- player_walk.png：ae2ec1f04ec4194005e0965eaf77289d03b2f384ee233a0cf35da02e8f43c943
- characters.png：f879aabb593bff80a94ad4c2f94000ae8f52dcd688536ae0d8712aa1c640edae
- Player.prefab：1b2e82200c9d494770f9d4d0dede49561d561d4ae474d4b8f617398f54b7cb7a

## 首轮完整提示词

Use case: identity-preserve.
Asset type: ONE production-candidate character reference illustration for a hand-painted Japanese anime 2D side-scrolling exploration game. NOT an animation sheet.

Primary request: Re-render ONLY the young man in the FIRST/LEFTMOST column (walk_0, the neutral standing pose) of the supplied transparent sprite sheet, as a clean high-resolution SINGLE full-body right-facing profile reference. Source canvas is 1774x887; the identity target is the figure inside the first cell x=0..443, approximately x144..291 and y55..815. Ignore the other three poses. This specific walk_0 is the ONLY identity and body-proportion reference. Improve genuine native linework and small surface detail; do NOT merely interpolate/upscale pixels or redesign the character.

LOCK IDENTITY: same youthful slim male, same softly angular face profile, small straight nose and chin, same visible near eye and ear placement, same short tousled near-black hair with blue-black shadows, same specific forward bangs, messy crown and nape silhouette. Preserve EXACT relative head size, neck length, shoulder width, torso length, leg length, arm/hand length and foot size seen in walk_0. Do not make him taller, more muscular, broader, older, cuter, more chibi, more heroic, or more fashion-model-like. Do not add volume to the hair.

LOCK CLOTHING: same muted blue-gray lightweight hip-length open jacket, simple folded collar, same shoulder/sleeve construction, loose sleeves with plain cuffs, same hem and unobtrusive pocket/seam design; narrow visible strip of pale ivory T-shirt; same slim dark navy full-length trousers, same ankle folds; same pale gray low-top lace-up sneakers with understated gray panels and pale soles. Keep source colors and restrained shading. No added logos, jewelry, buttons, straps, backpack, weapon, scarf, hat, bag, charms, belt ornaments, accessories or extra clothing details.

POSE / CAMERA: strict RIGHT-facing orthographic side view, entire body from the highest hair tip through both complete soles. Natural quiet standing, shoulders relaxed, arms hanging outside pockets, hands relaxed beside thighs as in walk_0. Legs nearly together, both feet planted on exactly one horizontal baseline, with only the tiny fore-aft offset needed to read BOTH complete shoes; no step, no weighty contrapposto, no walking action, no bent raised knee. Maintain the original narrow profile, not a three-quarter torso or head turn. Only the near eye is visible. Do not rotate the shoulders toward the viewer.

COMPOSITION: one centered figure, portrait canvas requested 1024x2304 (approximately 4:9). Figure occupies 88–90 percent of canvas height, leaving clear breathing room above hair and below soles, with generous side margins. Aim for native effective character height 1536–2048 pixels if supported. Preserve full canvas. Absolutely no clipping at head, hair, hand, heel or toe.

STYLE: match the original precise anime drawing and restrained cel/painterly shading, clean confident fine outlines with subtle fabric folds. Soft neutral diffuse illumination, enough cool blue shade to retain the game's look, but no sunset orange rim light, no dramatic lighting, no new texture/noise. Keep visible eyes and face at the original rendering style.

BACKGROUND: truly transparent RGBA, clean alpha edge, no color fringe, no white halo, no checkerboard baked in, no scenery, no ground platform or cast shadow. NO text, measurement marks, labels, frame borders, watermark, UI, multiple figures, pose sequence, front/back views, or inset portrait. Deliver only ONE single full-body right profile PNG.
MEASURED PROPORTION LOCK: in the source walk_0 the hair-inclusive head is approximately one-sixth of total figure height (NOT an idealized 7.5- or 8-head adult template). Preserve this exact source youthful head/body ratio. Jacket hem is approximately halfway down the complete figure, near hand fingertips at 59 percent of full height. Retain the source's slim shoulders, compact torso and relative leg length. These are source fidelity constraints, not a request for chibi stylization.

## 限定修正完整提示词

Use case: identity-preserve.
Edit target: image 1, the single high-resolution right-facing full-body character reference.
Identity reference: image 2, ONLY the FIRST/LEFTMOST standing figure walk_0 in the 1774x887 sprite sheet. Ignore the three other figures. Preserve that original character's identity.

Make a strictly limited correction to image 1. KEEP its single character, original 836x1881 portrait canvas, exact full-body size, approximately 1682px visible height, head-to-body proportion, body location, limb pose, garment outline, garment construction and all colors. Do NOT rescale or crop the canvas. Keep generous existing margin above hair and below both complete shoes.

CORRECTION 1: Restore the face to walk_0. Image 1 currently makes the visible eye a little too large/round and the cheek too soft. Make the near eye slightly narrower/smaller and match walk_0's upper eyelid, calm neutral expression, cheek/nose/chin profile. Preserve the original youthful male identity, hairstyle, ear placement and same head dimensions. Do not make him older, muscular, feminine, chibi or more stylized. This is subtle identity correction, not a new face. Keep head and whole body facing strictly RIGHT, no turn toward camera.

CORRECTION 2: Clean the transparency and outline completely. REMOVE stray yellow pixels along nose and lips, red halo around nape/collar, blue/white fringe around trouser legs and sneakers, and any stray low-opacity pixels in empty space. Those are source matting artifacts, not highlights. Reconstruct clean natural-color antialiasing against a genuine alpha background, tightly following the existing silhouette. No chromatic fringe, no white outline, no glow, no edge confetti. Empty background must be fully transparent, no checkerboard, no scenery or cast shadow.

Everything else in image 1 stays unchanged: same short tousled black-blue hair volume, narrow shoulders, approximately six-head proportions, blue-gray plain-collar hip-length jacket worn open over narrow visible ivory shirt, full sleeves and cuffs, diagonal pocket, dark navy trousers, relaxed hands hanging outside pockets, two complete pale gray low-top lace-up sneakers. Keep original walk_0 shoe proportions and restrained sole thickness, not chunky sneakers.
Exactly ONE standing full-body figure, no extra panels, insets, text, accessories or animation frames. Genuine refined native detail, no simple interpolation enlargement. Output transparent PNG.

