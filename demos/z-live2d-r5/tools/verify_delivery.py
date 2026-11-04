#!/usr/bin/env python3
"""Static checks for the tracked R5 delivery files.

This does not open Unity. It checks that the source payload is complete, the
13 original model files retain their recorded hashes, generated Model.asset is
kept out of the upload, and no tracked file crosses GitHub's 50 MiB warning
threshold.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
MAX_FILE_BYTES = 50 * 1024 * 1024
MODEL_ROOT = ROOT / "Assets" / "ZDialogueDemo" / "Models" / "Z"
MODEL_NAMES = (
    "Model.model3.json",
    "Model.moc3",
    "Model.cdi3.json",
    "Model.physics3.json",
    "texture_00.png",
    "angry.exp3.json",
    "blush.exp3.json",
    "explain.exp3.json",
    "point.exp3.json",
    "smile.exp3.json",
    "surprise.exp3.json",
    "troubled.exp3.json",
    "wave.exp3.json",
)


def digest(path: Path) -> str:
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(chunk)
    return result.hexdigest()


def main() -> int:
    required = (
        ROOT / "Assets" / "ZDialogueDemo" / "Scenes" / "ZDialogueDemo.unity",
        ROOT / "Assets" / "ZDialogueDemo" / "Models" / "Z" / "Model.prefab",
        ROOT / "Assets" / "ZDialogueDemo" / "Models" / "Z" / "Model.asset.meta",
        ROOT / "Assets" / "ZDialogueDemo" / "Runtime" / "ZSighFaceArt.cs",
        ROOT / "Assets" / "ZDialogueDemo" / "Resources" / "ZSighFace.shader",
        ROOT / "Assets" / "ZR5Migration" / "Editor" / "ZR5Migration.cs",
        ROOT / "Packages" / "manifest.json",
        ROOT / "Packages" / "packages-lock.json",
        ROOT / "ProjectSettings" / "ProjectVersion.txt",
        ROOT / "sdk-compatibility" / "manifest.json",
        ROOT / "tools" / "install_r5_sdk.py",
        ROOT / "tools" / "repair_model_guid.py",
    )
    missing = [str(path.relative_to(ROOT)) for path in required if not path.is_file()]
    if missing:
        raise RuntimeError("缺少交付文件：" + ", ".join(missing))
    if (ROOT / "Assets" / "Live2D").exists():
        raise RuntimeError("交付不应携带 SDK：Assets/Live2D")
    if (MODEL_ROOT / "Model.asset").exists():
        raise RuntimeError("Model.asset 是超过 50 MiB 的生成缓存，不应提交")

    baseline = json.loads(
        (ROOT / "verification" / "model-and-pipeline-integrity.json").read_text(encoding="utf-8")
    )
    expected = baseline.get("raw_model_sha256", {})
    if set(expected) != set(MODEL_NAMES):
        raise RuntimeError("模型基线没有完整列出 13 个原始文件")
    mismatches = []
    for name in MODEL_NAMES:
        path = MODEL_ROOT / name
        if not path.is_file():
            mismatches.append(f"missing:{name}")
        elif digest(path) != expected[name]:
            mismatches.append(f"sha256:{name}")
    if mismatches:
        raise RuntimeError("模型原始文件校验失败：" + ", ".join(mismatches))

    oversized = [
        f"{path.stat().st_size}:{path.relative_to(ROOT)}"
        for path in ROOT.rglob("*")
        if path.is_file() and path.stat().st_size > MAX_FILE_BYTES
    ]
    if oversized:
        raise RuntimeError("存在超过 50 MiB 的交付文件：" + ", ".join(oversized))

    forbidden_fragments = ("/Users/", "/private/", "My project", "moon-z.sqlite")
    # Support-layer docs deliberately mention local database paths and the
    # scanner command itself deliberately contains the patterns. Those files
    # are checked by the support-layer review; this pass targets the Unity
    # payload and installation material.
    scan_roots = (ROOT / "Assets", ROOT / "ProjectSettings", ROOT / "Packages", ROOT / "sdk-compatibility", ROOT / "third-party", ROOT / "tools" / "install_r5_sdk.py")
    text_files = []
    for scan_root in scan_roots:
        if scan_root.is_file():
            text_files.append(scan_root)
        elif scan_root.is_dir():
            text_files.extend(path for path in scan_root.rglob("*") if path.is_file())
    text_files = [path for path in text_files if path.suffix.lower() in {".cs", ".json", ".md", ".py", ".asset", ".unity", ".shader", ".txt"}]
    leaks = []
    for path in text_files:
        text = path.read_text(encoding="utf-8", errors="ignore")
        for fragment in forbidden_fragments:
            if fragment in text:
                leaks.append(f"{path.relative_to(ROOT)}:{fragment}")
    if leaks:
        raise RuntimeError("发现私人路径或存档引用：" + ", ".join(leaks))

    result = {
        "passed": True,
        "model_files_checked": len(MODEL_NAMES),
        "model_asset_regenerated_on_restore": True,
        "max_file_bytes": max(path.stat().st_size for path in ROOT.rglob("*") if path.is_file()),
        "sdk_included": False,
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"R5 交付静态检查失败：{error}", file=sys.stderr)
        raise SystemExit(1)
