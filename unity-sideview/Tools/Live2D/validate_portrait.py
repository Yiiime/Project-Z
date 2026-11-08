"""Check an island dialogue portrait. The roster and Unity assets are the source of truth."""
import argparse
import json
import re
import sys
from pathlib import Path

try:
    import yaml
    from jsonschema import Draft202012Validator
except ImportError:
    sys.exit("Install PyYAML and jsonschema in your Python environment first.")

ROSTER = "Assets/Seabreeze/Resources/Memory/characters.json"
PROFILE_TYPE = "Assets/Seabreeze/Live2D/Runtime/Live2DModelProfile.cs"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def local_path(parent, relative, boundary):
    require(isinstance(relative, str) and relative and not Path(relative).is_absolute()
            and "\\" not in relative,
            f"Expected a relative asset path: {relative!r}")
    result = (parent / relative).resolve()
    require(result.is_relative_to(boundary) and result.is_file(), f"Missing asset: {relative}")
    return result


def guid(asset):
    with Path(str(asset) + ".meta").open(encoding="utf-8") as handle:
        for line in handle:
            value = re.fullmatch(r"guid: ([0-9a-f]{32})\s*", line)
            if value:
                return value[1]
    raise ValueError(f"Missing GUID: {asset}.meta")


def collect_files(refs):
    for key in ("Moc", "Physics", "Pose", "DisplayInfo", "UserData"):
        if key in refs:
            yield refs[key]
    yield from refs["Textures"]
    for entry in refs.get("Expressions", []):
        yield entry["File"]
    for group in refs.get("Motions", {}).values():
        for motion in group:
            yield motion["File"]
            if motion.get("Sound"):
                yield motion["Sound"]


def snapshot(project, actor_id):
    assets = project / "Assets"
    actors = json.loads((project / ROSTER).read_text(encoding="utf-8"))["actors"]
    matches = [a for a in actors if a["id"] == actor_id]
    require(len(matches) == 1, f"Expected exactly one actor named {actor_id}")
    key = matches[0].get("portrait")
    require(isinstance(key, str) and key and ".." not in key.split("/") and not key.startswith("/"),
            f"{actor_id} has no valid portrait resource key")
    profiles = [p for r in assets.rglob("Resources") if r.is_dir()
                for p in [r / (key + ".asset")] if p.is_file()]
    require(len(profiles) == 1, f"Expected one Resources asset for {key}, got {len(profiles)}")
    profile = profiles[0]
    # Unity's text serialization adds a tagged document header before the native YAML.
    data = yaml.safe_load(profile.read_text(encoding="utf-8").split("\nMonoBehaviour:\n", 1)[1])
    require(data["m_Script"]["guid"] == guid(project / PROFILE_TYPE), "Wrong profile script type")
    require(data["characterId"] == actor_id, "Profile characterId differs from actor id")

    index = {}
    for meta in assets.rglob("*.meta"):
        asset = meta.with_suffix("")
        if asset.is_file() and asset.suffix in (".asset", ".prefab", ".controller", ".overrideController"):
            index.setdefault(guid(asset), []).append(asset)

    def asset_ref(native, allowed):
        value = native.get("guid")
        candidates = index.get(value, [])
        require(len(candidates) == 1, f"Missing or ambiguous asset GUID: {value}")
        p = candidates[0]
        require(p.suffix in allowed, f"Unexpected asset type: {p}")
        return {"path": p.relative_to(project).as_posix(), "guid": value}

    def describe(p):
        return {"path": p.relative_to(project).as_posix(), "guid": guid(p)}

    prefab = asset_ref(data["prefab"], (".prefab",))
    folder = (project / prefab["path"]).parent
    candidates = list(folder.glob("*.model3.json"))
    if len(candidates) > 1:
        candidates = [p for p in candidates if prefab["guid"] in Path(str(p) + ".meta").read_text(encoding="utf-8")]
    require(len(candidates) == 1, "Expected one associated .model3.json beside the portrait Prefab")
    model = candidates[0]
    refs = json.loads(model.read_text(encoding="utf-8"))["FileReferences"]
    require(refs.get("Moc") and refs.get("Textures"), "Missing MOC3 or texture references")
    files = []
    for name in dict.fromkeys(collect_files(refs)):
        p = local_path(model.parent, name, assets)
        guid(p)
        with p.open("rb") as handle:
            require(not handle.read(64).startswith(b"version https://git-lfs.github.com/spec/"),
                    f"Download the LFS object: {name}")
        files.append(p.relative_to(project).as_posix())

    expressions = {}
    for row in data.get("expressions") or []:
        name, ref = row["emotion"], row["expression"]
        require(name and name.casefold() not in {s.casefold() for s in expressions},
                f"Duplicate or empty emotion: {name}")
        expressions[name] = asset_ref(ref, (".asset",)) if ref["fileID"] else None
    animator = data.get("animatorController") or {}
    return {"schemaVersion": 1, "actorId": actor_id, "portrait": key,
            "profile": describe(profile), "prefab": prefab, "model3": describe(model),
            "modelFiles": files, "defaultEmotion": data["defaultEmotion"], "expressions": expressions,
            "animator": asset_ref(animator, (".controller", ".overrideController")) if animator.get("fileID") else None,
            "parameters": {name: data.get(name) or None for name in
                           ("mouthParameter", "breathParameter", "leftEyeParameter", "rightEyeParameter")},
            "rendererIndex": data["rendererIndex"], "renderLayer": data["renderLayer"],
            "textureHeight": data["textureHeight"]}


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--project-root", required=True, type=Path, help="Unity project or game repository")
    cli.add_argument("--actor", required=True, help="Existing actor ID in Memory/characters.json")
    cli.add_argument("--schema", type=Path, default=Path(__file__).resolve().parents[2] / "Docs/Live2D/character-portrait.schema.json")
    cli.add_argument("--check-example", type=Path, help="Compare a saved snapshot with today's native assets")
    cli.add_argument("--print-json", action="store_true", help="Print the generated review snapshot")
    args = cli.parse_args()
    project = args.project_root.resolve()
    if not (project / "Assets").is_dir():
        project = project / "unity-sideview"
    record = snapshot(project, args.actor)
    schema = json.loads(args.schema.read_text(encoding="utf-8"))
    Draft202012Validator.check_schema(schema)
    errors = list(Draft202012Validator(schema).iter_errors(record))
    require(not errors, "Schema: " + " / ".join(e.message for e in errors))
    if args.check_example:
        require(record == json.loads(args.check_example.read_text(encoding="utf-8")), "Saved example differs from current Unity assets")
    print(json.dumps(record, indent=2, ensure_ascii=False) if args.print_json else f"PASS: {args.actor} -> {record['portrait']}")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, IndexError, TypeError, yaml.YAMLError) as exc:
        sys.exit(f"FAIL: {exc}")
