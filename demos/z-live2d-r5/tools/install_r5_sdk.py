#!/usr/bin/env python3
"""Install the pinned Cubism 5-r.5 Unity package into this delivery project.

The project intentionally excludes Live2D SDK files. This installer accepts a
user-provided official .unitypackage, verifies this delivery's recorded
SHA-256, safely extracts only Assets/Live2D, then applies the three pinned
Unity 6 compatibility patches. It refuses to overwrite an existing
Assets/Live2D tree.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys


EXPECTED_SHA256 = "c9ac920b3a7359dc9ebe4ec0e9ad3c50367d615bdaeba3fc028141e90d45a0a1"
EXPECTED_SDK_VERSION = "5-r.5"
DEFAULT_UNITY = Path(
    "/Applications/Unity/Unity-6000.6.7f1/Unity.app/Contents/MacOS/Unity"
)
DELIVERY_ROOT = Path(__file__).resolve().parents[1]


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=Path, default=DEFAULT_UNITY)
    parser.add_argument("--unitypackage", type=Path, required=True)
    parser.add_argument(
        "--project",
        type=Path,
        default=DELIVERY_ROOT,
        help="本交付副本根目录；先复制整个 r5-delivery，再用副本中的脚本运行",
    )
    parser.add_argument(
        "--allow-existing",
        action="store_true",
        help="允许重复运行；仍会校验已安装 SDK 版本和补丁状态",
    )
    return parser.parse_args()


def run(command: list[str], *, cwd: Path, log: Path | None = None) -> None:
    if log is None:
        completed = subprocess.run(command, cwd=cwd, check=False)
    else:
        with log.open("w", encoding="utf-8") as stream:
            completed = subprocess.run(
                command,
                cwd=cwd,
                stdout=stream,
                stderr=subprocess.STDOUT,
                check=False,
            )
    if completed.returncode != 0:
        raise RuntimeError(f"命令失败（{completed.returncode}）：{' '.join(command)}")


def verify_sdk(project: Path) -> Path:
    cubism = project / "Assets" / "Live2D" / "Cubism"
    info = cubism / "cubism-info.yml"
    if not info.is_file():
        raise RuntimeError(f"Unity 导入后找不到 Cubism SDK：{info}")
    version_line = next(
        (line.strip() for line in info.read_text(encoding="utf-8").splitlines() if line.startswith("version:")),
        "",
    )
    if version_line != f"version: {EXPECTED_SDK_VERSION}":
        raise RuntimeError(f"SDK 版本不匹配：{version_line!r}")
    if (cubism / "Rendering" / "Masking").exists():
        raise RuntimeError("检测到 R4 Rendering/Masking，拒绝把旧 SDK 混入 R5 工程")
    renderer = cubism / "Rendering" / "URP" / "CubismURPRenderer.asset"
    if not renderer.is_file():
        raise RuntimeError(f"找不到 R5 URP renderer：{renderer}")
    return cubism


def main() -> int:
    args = parse_args()
    project = args.project.expanduser().resolve()
    unity = args.unity.expanduser().resolve()
    package = args.unitypackage.expanduser().resolve()
    if project != DELIVERY_ROOT.resolve():
        raise RuntimeError("为避免误操作，安装器只允许写入当前 r5-delivery 目录")
    if not (project / "Assets").is_dir() or not (project / "ProjectSettings").is_dir():
        raise RuntimeError(f"不是完整 Unity 工程：{project}")
    if not unity.is_file():
        raise RuntimeError(f"找不到 Unity CLI：{unity}")
    if not package.is_file() or package.suffix != ".unitypackage":
        raise RuntimeError(f"找不到 .unitypackage：{package}")
    actual_sha256 = sha256(package)
    if actual_sha256 != EXPECTED_SHA256:
        raise RuntimeError(
            "SDK 原包 SHA-256 不匹配。\n"
            f"期望：{EXPECTED_SHA256}\n实际：{actual_sha256}"
        )

    live2d = project / "Assets" / "Live2D"
    if live2d.exists() and not args.allow_existing:
        raise RuntimeError(
            f"目标已有 Assets/Live2D，为保护已有 SDK 停止：{live2d}\n"
            "如确认这是本交付目录的同一版本，可加 --allow-existing。"
        )
    if live2d.exists():
        verify_sdk(project)
    else:
        unpacker = project / "tools" / "unpack_unitypackage.py"
        if not unpacker.is_file():
            raise RuntimeError(f"缺少安全解包脚本：{unpacker}")
        # Unity's -importPackage compiles project scripts before importing a
        # package. In this project those scripts reference Live2D, so import
        # first with the path-whitelisted extractor, then let Unity reimport
        # the resulting Assets tree normally.
        run([sys.executable, str(unpacker), str(package), str(project)], cwd=project)
        verify_sdk(project)

    patcher = project / "tools" / "apply_sdk_compatibility.py"
    if not patcher.is_file():
        raise RuntimeError(f"缺少兼容补丁脚本：{patcher}")
    run([sys.executable, str(patcher), str(project)], cwd=project)
    verify_sdk(project)

    # Trigger a normal asset import so generated Cubism assets (including the
    # intentionally untracked Model.asset) are recreated before a build. The
    # editor may still need an extra open/import pass on a new machine.
    import_command = [
        str(unity),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(project),
        "-quit",
        "-logFile",
        str(project / "sdk-import.log"),
    ]
    run(import_command, cwd=project)

    repair = project / "tools" / "repair_model_guid.py"
    if not repair.is_file():
        raise RuntimeError(f"缺少模型 GUID 恢复脚本：{repair}")
    run([sys.executable, str(repair), "--project", str(project)], cwd=project)

    migration_command = [
        str(unity),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(project),
        "-executeMethod",
        "ZDialogueDemo.R5Migration.ZR5Migration.Migrate",
        "-quit",
        "-logFile",
        str(project / "migration.log"),
    ]
    run(migration_command, cwd=project)

    state = {
        "sdk_version": EXPECTED_SDK_VERSION,
        "unity_version": "6000.6.7f1",
        "urp_version": "17.6.0",
        "unitypackage_sha256": actual_sha256,
        "project": str(project),
        "compatibility_manifest": "sdk-compatibility/manifest.json",
        "model_asset_guid": "0fe9b85c5293e4882a9add0f7b43250b",
    }
    (project / "restore-state.json").write_text(
        json.dumps(state, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    print(json.dumps(state, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"R5 SDK 安装失败：{error}", file=sys.stderr)
        raise SystemExit(1)
