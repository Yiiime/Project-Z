#!/usr/bin/env python3
"""Restore the saved Cubism model asset GUID after first-time import.

Unity may regenerate the .meta GUID when a generated Cubism Model.asset is
omitted from source control and only its orphan .meta is present. The model
prefab and model JSON metadata are the only tracked references in this demo;
rewrite those three files together so the regenerated asset keeps the R5
project's original GUID.
"""

from __future__ import annotations

import argparse
from pathlib import Path
import re
import sys


EXPECTED_GUID = "0fe9b85c5293e4882a9add0f7b43250b"
ROOT = Path(__file__).resolve().parents[1]
MODEL_DIR = ROOT / "Assets" / "ZDialogueDemo" / "Models" / "Z"
TARGETS = ("Model.asset.meta", "Model.model3.json.meta", "Model.prefab")


def read_guid(meta: Path) -> str:
    match = re.search(r"(?m)^guid:\s*([0-9a-f]{32})\s*$", meta.read_text(encoding="utf-8"))
    if not match:
        raise RuntimeError(f"找不到模型资产 GUID：{meta}")
    return match.group(1)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=ROOT)
    parser.add_argument("--expected-guid", default=EXPECTED_GUID)
    args = parser.parse_args()
    project = args.project.expanduser().resolve()
    model_dir = project / "Assets" / "ZDialogueDemo" / "Models" / "Z"
    meta = model_dir / "Model.asset.meta"
    if not meta.is_file():
        raise RuntimeError(f"缺少生成模型资产的 .meta：{meta}")
    expected = args.expected_guid.lower()
    if not re.fullmatch(r"[0-9a-f]{32}", expected):
        raise RuntimeError("expected GUID 格式无效")
    actual = read_guid(meta)
    if actual == expected:
        print(f"Model.asset GUID 已正确：{expected}")
        return 0

    contents = {}
    for name in TARGETS:
        path = model_dir / name
        if not path.is_file():
            raise RuntimeError(f"缺少需要同步 GUID 的文件：{path}")
        text = path.read_text(encoding="utf-8")
        if actual not in text:
            raise RuntimeError(f"{path} 没有当前生成 GUID：{actual}")
        contents[path] = text.replace(actual, expected)
    for path, text in contents.items():
        path.write_text(text, encoding="utf-8")
    print(f"Model.asset GUID 已恢复：{actual} -> {expected}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"模型 GUID 恢复失败：{error}", file=sys.stderr)
        raise SystemExit(1)
