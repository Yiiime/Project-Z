# 来自月亮的 Z · UI 设计集 v1

这份设计集包含 **18 张关键界面的静态 UI 示意图**，采用 PC 横屏画幅。导览器用于查看原图、阅读说明与浏览页面关系；图中的对话、潜行、迁移、计时、存档和设置均未接入真实游戏逻辑。

## 打开方式

解压并保留整个 `v1` 文件夹，双击 `index.html` 即可在浏览器中查看。页面已内嵌当前的 18 屏目录，不需要联网、启动服务器或手动选择文件。请不要只发送或移动 `index.html`，它仍需要同目录的样式、脚本和 `assets` 原图。

浏览器若没有自动打开，可将 `index.html` 拖入浏览器窗口。若随包目录损坏，页面会提供手动选择同目录 `manifest.json` 的恢复入口；读取操作只在本机发生。

开发预览也支持 HTTP。使用项目根目录中的终端，可运行：

```sh
python3 -m http.server 8764 --bind 127.0.0.1 --directory ui/v1
```

其中 `-m http.server` 启动 Python 自带的静态文件服务，`8764` 指定端口，`--bind 127.0.0.1` 限制为本机访问，`--directory ui/v1` 指定要展示的目录。打开浏览器中的 `http://127.0.0.1:8764`；结束预览时，在该终端按 `Ctrl+C` 停止服务。

## 浏览方式

- 左侧目录按章节分组；左上按钮可收起或展开目录。
- 底部“上一张／下一张”或键盘 `←`、`→` 浏览相邻界面；空格前进一张。焦点位于按钮或链接时，空格保留该控件的正常操作。
- 点击原图进入放大查看，按 `Esc` 或“退出放大”关闭；图像始终完整显示，不裁切。
- “全屏”控制浏览器全屏；“显示说明”展开当前界面的玩家目标、画面内容和设计说明。
- 图下“界面导览”按钮只跳转示意图，用于说明页面关系。可以通过网址末尾的 `#界面id` 直接定位，例如 `#14-transfer`。
- `Tab` 可依次移动键盘焦点。移动端也可浏览，目录与说明改为可关闭的侧栏；主要评审画幅仍为 PC 横屏。

## 文件组织

```text
v1/
├── index.html       展示器与离线目录副本
├── viewer.css       展示器样式
├── viewer.js        界面导览逻辑
├── manifest.json    18 屏标题、章节、说明与跳转关系的目录源文件
├── prompts.json     图片制作提示记录
├── README.md        本说明
└── assets/          01-title.png 至 18-pause.png 等 18 张原图
```

18 屏依次覆盖：主菜单、身份与会话、自由探索、日程取舍、证据与共同记录、私下交流、夜间直播、次日可见后果、Z 主动调查、本人核验、合作决定、路线与分工、实时潜行、迁移守候、抵达公司之外、知情留下、暴露重试，以及暂停与存档设置。

## 修改目录后同步离线副本

`manifest.json` 是目录源文件。HTTP 打开时优先读取它，读取失败才使用页面内嵌副本；双击文件打开时直接读取内嵌副本。因此，修改 `manifest.json` 后，交付前必须同步 `index.html` 中 `id="embedded-manifest"` 的 JSON 内容，并重新检查 18 张原图与跳转目标。

以下命令在项目根目录运行，只更新 HTML 内的 JSON 副本。它会把可能结束 HTML 脚本标签的字符安全转义，不改动原始 JSON 文件或图片：

```sh
python3 - <<'PY'
import json
import re
from pathlib import Path

root = Path('ui/v1')
data = json.loads((root / 'manifest.json').read_text())
payload = json.dumps(data, ensure_ascii=False, indent=2)
for char, escaped in [('&', r'\u0026'), ('<', r'\u003c'), ('>', r'\u003e'),
                      ('\u2028', r'\u2028'), ('\u2029', r'\u2029')]:
    payload = payload.replace(char, escaped)
block = '<script type="application/json" id="embedded-manifest">\n' + payload + '\n    </script>'
path = root / 'index.html'
pattern = r'<script\s+type="application/json"\s+id="embedded-manifest">[\s\S]*?</script>'
html, count = re.subn(pattern, lambda match: block, path.read_text(), count=1)
if count != 1:
    raise SystemExit('未找到唯一的 embedded-manifest，请检查 index.html。')
path.write_text(html)
embedded = re.search(pattern, html).group(0).split('>', 1)[1].rsplit('</script>', 1)[0]
assert json.loads(embedded) == data
print('已同步并验证离线目录：', len(data['screens']), '屏')
PY
```

只调整布局或替换同名图片时无需同步目录；改变屏幕标题、说明、图片路径、章节或跳转关系时需要同步。
