#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p .downloads .build
fetch_sdk() {
 local name="$1" url="$2" sha="$3"
 if [[ ! -f ".downloads/$name.zip" ]]; then curl --fail --location "$url" -o ".downloads/$name.zip"; fi
 [[ "$(shasum -a 256 ".downloads/$name.zip" | awk '{print $1}')" == "$sha" ]] || { echo 'RTC SDK checksum mismatch' >&2; exit 1; }
 if [[ ! -d ".downloads/$name" ]]; then unzip -q ".downloads/$name.zip" -d ".downloads/$name"; fi
}
fetch_sdk arm 'https://portal.volccdn.com/obj/volcfe/cloud-universal-doc/upload_0f374afc00c7e9f100c952838e7d6231.zip' 2728e006ad3c733a66c0cacca58022d7fdfcef4ab127c71566cdee3470bde330
fetch_sdk x86 'https://portal.volccdn.com/obj/volcfe/cloud-universal-doc/upload_af458d3883decf4187c14699ac85d4ff.zip' e54c69ce74956f54c725322caf3d8e79c055a55330c9a5ceed00c86d247995de
arm="$PWD/.downloads/arm/VolcEngineRTC_3.60.107.1200"
intel="$PWD/.downloads/x86/VolcEngineRTC_3.60.107.1300"
for arch in arm64 x86_64; do
 sdk="$arm"; [[ "$arch" != x86_64 ]] || sdk="$intel"
 clang++ -std=c++17 -dynamiclib -arch "$arch" -mmacosx-version-min=11.0 \
  -I "$sdk/VolcEngineRTC.framework/Headers/native" -F "$sdk" -framework VolcEngineRTC \
  -Wl,-rpath,@loader_path ZLiveRtc.cpp -o ".build/libZLiveRtc-$arch.dylib"
done
ditto "$arm" .build/runtime
# The vendor distributes separate architectures. Combine matching Mach-O files;
# keep its framework resources and privacy declarations unchanged.
python3 - "$arm" "$intel" "$PWD/.build/runtime" <<'PY'
import pathlib,subprocess,sys,tempfile
arm,intel,out=map(pathlib.Path,sys.argv[1:])
for p in arm.rglob('*'):
 if not p.is_file() or p.is_symlink(): continue
 if 'Mach-O' not in subprocess.check_output(['file','-b',str(p)],text=True): continue
 rel=p.relative_to(arm); other=intel/rel
 if not other.is_file(): raise SystemExit('Missing Intel SDK binary: '+str(rel))
 with tempfile.TemporaryDirectory() as temp:
  parts=[]
  for arch,source in [('arm64',p),('x86_64',other)]:
   target=str(pathlib.Path(temp)/arch)
   arches=subprocess.check_output(['lipo','-archs',str(source)],text=True).split()
   if arch not in arches: raise SystemExit('Missing '+arch+': '+str(source))
   if len(arches)>1: subprocess.run(['lipo',str(source),'-thin',arch,'-output',target],check=True)
   else: target=str(source)
   parts.append(target)
  subprocess.run(['lipo','-create',*parts,'-output',str(out/rel)],check=True)
 subprocess.run(['codesign','--force','--sign','-',str(out/rel)],check=True)
PY
lipo -create .build/libZLiveRtc-arm64.dylib .build/libZLiveRtc-x86_64.dylib -output .build/runtime/libZLiveRtc.dylib
codesign --force --sign - .build/runtime/libZLiveRtc.dylib
echo 'Built macOS Universal RTC bridge in Tools/native-rtc/.build/runtime'
