#!/usr/bin/env python3
"""将官方 unitypackage 的 Cubism 文件解包到暂存目录，不运行包内代码。"""
import argparse
import hashlib
import json
import tarfile
from pathlib import Path, PurePosixPath


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('package')
    parser.add_argument('destination')
    args = parser.parse_args()
    package = Path(args.package).resolve()
    destination = Path(args.destination).resolve()
    files = []
    with tarfile.open(package, 'r:gz') as archive:
        members = {m.name: m for m in archive.getmembers()}
        for name, member in sorted(members.items()):
            if not name.endswith('/pathname') or not member.isfile():
                continue
            raw = archive.extractfile(member).read().decode('utf-8').strip()
            path = PurePosixPath(raw)
            if path.is_absolute() or '..' in path.parts:
                raise SystemExit('包内路径不安全')
            if not (raw in ('Assets/Live2D', 'Assets/Live2D/Cubism') or raw.startswith('Assets/Live2D/Cubism/')):
                continue
            prefix = name.rsplit('/', 1)[0]
            for suffix, output_name in [('asset', raw), ('asset.meta', raw + '.meta')]:
                item = members.get(prefix + '/' + suffix)
                if item is None or not item.isfile():
                    continue
                target = destination / output_name
                if not target.resolve().is_relative_to(destination):
                    raise SystemExit('解包路径越界')
                data = archive.extractfile(item).read()
                if target.exists() and target.read_bytes() != data:
                    raise SystemExit('暂存目录已有不同内容：' + output_name)
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
                files.append({'path': output_name, 'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data)})
    if not any(x['path'].endswith('CubismModel.cs') for x in files):
        raise SystemExit('未找到预期 Cubism 源文件')
    manifest = {'package': package.name, 'sha256': hashlib.sha256(package.read_bytes()).hexdigest(), 'files': files}
    destination.mkdir(parents=True, exist_ok=True)
    (destination / 'sdk-manifest.json').write_text(json.dumps(manifest, indent=2))
    print(json.dumps({'package': package.name, 'files': len(files), 'sha256': manifest['sha256']}))


if __name__ == '__main__':
    main()
