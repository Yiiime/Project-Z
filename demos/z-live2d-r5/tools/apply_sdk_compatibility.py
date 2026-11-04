#!/usr/bin/env python3
"""Apply the pinned Live2D Cubism / Unity 6.6 editor compatibility patch.

The script is intentionally explicit: it requires the actual Unity project root
as an argument and refuses to operate on the read-only SDK staging directory.
Each file must be either the pinned official source hash or the pinned patched
hash. Unknown edits are never overwritten.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Any


SCRIPT_PATH = Path(__file__).resolve()
COMPATIBILITY_ROOT = SCRIPT_PATH.parent.parent / "sdk-compatibility"
DEFAULT_MANIFEST = COMPATIBILITY_ROOT / "manifest.json"
PATCH_TIMEOUT_SECONDS = 30


class CompatibilityError(RuntimeError):
    """A source, patch, or postcondition did not match the pinned manifest."""


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def safe_relative_path(value: Any, field: str) -> Path:
    if not isinstance(value, str) or not value or "\\" in value:
        raise CompatibilityError(f"manifest {field} must be a relative POSIX path")
    path = Path(value)
    if path.is_absolute() or ".." in path.parts:
        raise CompatibilityError(f"manifest {field} escapes its root: {value}")
    return path


def load_manifest(path: Path) -> list[dict[str, Any]]:
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise CompatibilityError(f"cannot read manifest {path}: {error}") from error

    if document.get("schema_version") != 1:
        raise CompatibilityError("unsupported SDK compatibility manifest schema")
    entries = document.get("entries")
    if not isinstance(entries, list) or len(entries) != 3:
        raise CompatibilityError("manifest must pin exactly the three SDK source files")
    return entries


def checked_patch_path(manifest_path: Path, entry: dict[str, Any], relative_path: Path) -> Path:
    patch_relative_path = safe_relative_path(entry.get("patch_path"), "patch_path")
    patch_path = (manifest_path.parent / patch_relative_path).resolve()
    try:
        patch_path.relative_to(COMPATIBILITY_ROOT.resolve())
    except ValueError as error:
        raise CompatibilityError(f"patch path escapes compatibility root: {patch_path}") from error
    if not patch_path.is_file():
        raise CompatibilityError(f"patch file does not exist: {patch_path}")

    patch_text = patch_path.read_text(encoding="utf-8")
    headers = re.findall(r"^(?:---|\+\+\+) ([^\t\n]+)", patch_text, flags=re.MULTILINE)
    if not headers or len(headers) % 2 or any(header != relative_path.as_posix() for header in headers):
        raise CompatibilityError(f"patch headers do not match {relative_path}: {patch_path}")
    return patch_path


def run_patch(patch_path: Path, project_root: Path, dry_run: bool) -> None:
    command = [
        "patch",
        "--batch",
        "--forward",
        "--fuzz=0",
        "--dry-run" if dry_run else "--force",
        "-p0",
        "-d",
        str(project_root),
    ]
    try:
        completed = subprocess.run(
            command,
            input=patch_path.read_bytes(),
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
            timeout=PATCH_TIMEOUT_SECONDS,
        )
    except (OSError, subprocess.TimeoutExpired) as error:
        raise CompatibilityError(f"could not run patch for {patch_path}: {error}") from error
    if completed.returncode != 0:
        output = completed.stdout.decode("utf-8", errors="replace").strip()
        raise CompatibilityError(f"patch failed for {patch_path}:\n{output}")


def verify_entry(project_root: Path, manifest_path: Path, entry: dict[str, Any]) -> tuple[Path, Path, str]:
    relative_path = safe_relative_path(entry.get("relative_path"), "relative_path")
    target_path = (project_root / relative_path).resolve()
    try:
        target_path.relative_to(project_root)
    except ValueError as error:
        raise CompatibilityError(f"target path escapes project root: {relative_path}") from error
    if not target_path.is_file():
        raise CompatibilityError(f"target source file does not exist: {target_path}")

    original_hash = entry.get("original_sha256")
    patched_hash = entry.get("patched_sha256")
    if not all(isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) for value in (original_hash, patched_hash)):
        raise CompatibilityError(f"invalid pinned SHA-256 values for {relative_path}")
    current_hash = sha256(target_path)
    if current_hash == original_hash:
        state = "original"
    elif current_hash == patched_hash:
        state = "patched"
    else:
        raise CompatibilityError(
            f"refusing unknown contents for {relative_path}: {current_hash}; "
            f"expected original {original_hash} or patched {patched_hash}"
        )
    patch_path = checked_patch_path(manifest_path, entry, relative_path)
    return target_path, patch_path, state


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "project_root",
        type=Path,
        help="actual Unity project root containing Assets (never the SDK staging directory)",
    )
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument(
        "--check-only",
        action="store_true",
        help="validate hashes and patch applicability without changing files",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    project_root = args.project_root.expanduser().resolve()
    manifest_path = args.manifest.expanduser().resolve()

    if not project_root.is_dir() or not (project_root / "Assets").is_dir():
        print(f"error: not a Unity project root with Assets: {project_root}", file=sys.stderr)
        return 2
    if ".local" in project_root.parts and "sdk-stage" in project_root.parts:
        print("error: refusing to patch the SDK staging directory", file=sys.stderr)
        return 2

    try:
        entries = load_manifest(manifest_path)
        verified = [verify_entry(project_root, manifest_path, entry) for entry in entries]

        pending = [(target, patch, state) for target, patch, state in verified if state == "original"]
        if not pending:
            print(f"SDK compatibility already applied: {project_root}")
            return 0

        # Preflight every file before mutating any of them.
        for _, patch_path, _ in pending:
            run_patch(patch_path, project_root, dry_run=True)

        if args.check_only:
            print(f"SDK compatibility check passed: {project_root}")
            return 0

        for target_path, patch_path, _ in pending:
            # Re-check immediately before applying in case another process edited it.
            entry = next(item for item in entries if safe_relative_path(item.get("relative_path"), "relative_path") == target_path.relative_to(project_root))
            if sha256(target_path) != entry["original_sha256"]:
                raise CompatibilityError(f"target changed during preflight: {target_path}")
            run_patch(patch_path, project_root, dry_run=False)
            if sha256(target_path) != entry["patched_sha256"]:
                raise CompatibilityError(f"patched hash mismatch: {target_path}")

        print(f"SDK compatibility applied: {project_root}")
        return 0
    except CompatibilityError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
