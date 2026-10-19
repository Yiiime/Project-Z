# 手机直播 UI 本地字体

获取日期：2026-10-11。字体与许可均来自 [Google Fonts 官方仓库](https://github.com/google/fonts)。本目录用于离线静态 UI，不需要安装系统字体，也不需要访问 Google Fonts 在线 CSS。

## 文件与使用范围

| 本地文件 | 官方原文件 | 字重范围 | 大小 |
|---|---|---|---:|
| `Manrope-Variable.ttf` | `Manrope[wght].ttf` | `wght` 200–800，normal | 164,700 B |
| `NotoSansSC-Variable.ttf` | `NotoSansSC[wght].ttf` | `wght` 100–900，normal | 17,772,300 B |

以上为仓库提供的完整可变字体；只修改本地文件名，没有改字体内容或制作字符子集。字体共 17,937,000 B，约 17.11 MiB。保留 Noto Sans SC 的完整原始字符覆盖，避免只为当前固定文案生成的子集在自由输入时缺字。它覆盖简体中文及其他官方包含的字符，不代表支持全部 Unicode、生僻扩展汉字或彩色 emoji；遇到未覆盖字符由系统后备字体处理。

Manrope 用于拉丁文字与数字，Noto Sans SC 补足中文。英文 UI 可使用同一字体栈，避免切换语言时依赖联网。两个字体均只有 normal 样式；界面不需要斜体时可关闭浏览器模拟斜体。

## 官方来源与校验

| 文件 | 官方下载地址 | Git blob SHA-1 |
|---|---|---|
| Manrope 字体 | [Google Fonts / ofl/manrope](https://raw.githubusercontent.com/google/fonts/main/ofl/manrope/Manrope%5Bwght%5D.ttf) | `75274da58537d6123b14f2cd0c355ad4681fc2b3` |
| Manrope 许可 | [OFL.txt](https://raw.githubusercontent.com/google/fonts/main/ofl/manrope/OFL.txt) | `e271172a9ed0cddc895aabb6509f1c7d880b492d` |
| Noto Sans SC 字体 | [Google Fonts / ofl/notosanssc](https://raw.githubusercontent.com/google/fonts/main/ofl/notosanssc/NotoSansSC%5Bwght%5D.ttf) | `fb0637bafbcd804fe32152370a1225990745b4bc` |
| Noto Sans SC 许可 | [OFL.txt](https://raw.githubusercontent.com/google/fonts/main/ofl/notosanssc/OFL.txt) | `1c9f43281b8f216c5461fe9ac729afbade7724e4` |

字重、字体名称与许可声明依据官方 [Manrope METADATA.pb](https://raw.githubusercontent.com/google/fonts/main/ofl/manrope/METADATA.pb) 和 [Noto Sans SC METADATA.pb](https://raw.githubusercontent.com/google/fonts/main/ofl/notosanssc/METADATA.pb)。下载时以 GitHub Contents API 返回的 Git blob SHA-1 校验完整内容；上表记录这次实际取得的版本，不假定 `main` 链接永远不变。Git blob SHA-1 是对 `blob <字节数>\0` 加文件内容计算的哈希，不等于普通文件 SHA-1。

Noto Sans SC 最终通过[同一官方仓库的 raw 地址](https://raw.githubusercontent.com/google/fonts/refs/heads/main/ofl/notosanssc/NotoSansSC%5Bwght%5D.ttf)分块下载；每块核对 HTTP Content-Range 与长度，完整组装后通过上表 Git blob 校验，才原子写入正式文件。未保留临时下载文件。

| 字体 | 文件 SHA-256 |
|---|---|
| Manrope | `3ae11c49db0455a3cc33e37d380f20fdb8c7f8b41dc07625c177e3d87a9d6ae6` |
| Noto Sans SC | `a3041811a78c361b1de50f953c805e0244951c21c5bd412f7232ef0d899af0da` |

本地解析确认两个文件具有上述 `wght` 可变轴；Noto Sans SC 的 Unicode cmap 包含 30,890 个非空字形映射。交付时 `index.html` / `app.js` 中的 195 个不同中文字符、额外中文输入样句合计 80 个不同字符、英文建议及数字样句合计 37 个不同字符，均通过 cmap 覆盖检查。这是字体文件层验证，页面断行、视觉字重与实际输入仍由浏览器验收。

## 许可

两款字体都使用 SIL Open Font License 1.1。原始许可随文件保存在 `Manrope-OFL.txt` 与 `NotoSansSC-OFL.txt`。

- Manrope：Copyright 2018 The Manrope Project Authors。
- Noto Sans SC：Copyright 2014–2021 Adobe，Reserved Font Name 为 `Source`，以附带原文为准。
- OFL 允许将字体随软件嵌入、打包和再分发；分发时保留版权及许可原文。字体本身不可单独出售；修改字体时须遵守保留字体名称等要求。使用字体制作的 UI 或输出文档不因此被要求采用 OFL。

## 接入示例

以下路径假定 HTML 位于 `ui/live-phone-v2/`。这里仅记录接入方式，未替代页面实现与浏览器验收。

```css
@font-face {
  font-family: "Manrope";
  src: url("./fonts/Manrope-Variable.ttf") format("truetype");
  font-style: normal;
  font-weight: 200 800;
  font-display: swap;
}

@font-face {
  font-family: "Noto Sans SC";
  src: url("./fonts/NotoSansSC-Variable.ttf") format("truetype");
  font-style: normal;
  font-weight: 100 900;
  font-display: swap;
}

html, button, input, textarea {
  font-family: "Manrope", "Noto Sans SC", sans-serif;
  font-synthesis: none;
}
```

无需 `unicode-range` 或远程字体 URL。若将来压缩为 WOFF2，应保留完整所需字符覆盖、再次校验输入和离线加载，并更新本文件；当前版本保留完整上游 TTF。
