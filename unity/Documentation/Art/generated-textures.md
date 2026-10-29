# 第二版服装贴图

两张贴图均由内置 `image_gen` 工具生成，没有输入参考图。生成结果原样复制到项目，再通过 Unity 材质设置颜色与平铺比例；不是游戏截图，也未用于伪装实机效果。

| 资产 | 项目路径 | Unity 用法 |
| --- | --- | --- |
| 细碎花布料 | `Assets/ProjectZ/Art/Characters/Z/Textures/Z_Floral_v2.jpg` | 长裙，平铺 7 × 4 |
| 针织布料 | `Assets/ProjectZ/Art/Characters/Z/Textures/Z_Knit_v2.jpg` | 开衫，紫色着色，平铺 3.5 × 3 |

工具输出文件名使用 `.png`，检测到实际编码为 JPEG；项目中按实际格式使用 `.jpg` 扩展名，像素内容未改动。

## 碎花贴图提示词

Use case: stylized-concept. Asset type: seamless square albedo texture for a modest long skirt in a polished Japanese 3D character game. Create a flat, straight-on 2048x2048 dark navy fabric swatch with very small, delicate scattered gray-blue floral sprigs and tiny leaves. Background uniform deep navy #202B40; floral ink low-contrast muted blue gray #626E83 with occasional #7A8190 petals, never white. Each sprig approximately 1 percent of image width, 2 to 4 small irregular petals with fine stems and two tiny leaves; distribute naturally with varied rotation and subtle size, about 30 sprigs across the width. Elegant restrained ditsy floral dress fabric. Extremely subtle matte woven cloth grain. Even neutral illumination, no shadows, no folds, no perspective, no clothing silhouette, no borders, no text, no symbols. Pattern must repeat seamlessly on all four edges. The complete frame is only the fabric texture, opaque background.

## 针织贴图提示词

Use case: stylized-concept. Asset type: seamless albedo fabric tile for a fine cable-knit cardigan on a Japanese 3D game character. A perfectly flat straight-on square 2048x2048 texture swatch. Neutral off-white and very pale gray only, to be color-tinted by a game shader. Six slim vertical braided cable columns alternating with narrow fine-knit rib columns. Delicate, elegant medium-gauge knit, soft yarn, restrained low-relief shading and fine real stitch structure. Braids continuous and uniform in width, seamless repeating on all four sides. Soft even diffuse illumination with very subtle occlusion inside stitches, no broad shadows, no highlights, no folds, no perspective, no border, no garments, no text, no objects. Whole image filled by the knitted surface.
