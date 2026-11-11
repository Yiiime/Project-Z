"""Publish the user-approved, colour-matched idle without regenerating artwork.

The PNG already contains the reviewed matte, uniform scale and translation.
Only its bytes and provenance are installed; video frames and playback tuning
are preserved. Requires Pillow for input validation.
"""
from pathlib import Path
import hashlib,json,shutil
from PIL import Image

ROOT=Path(__file__).resolve().parent.parent
SOURCE=ROOT/'source~'
approved=json.loads((SOURCE/'approved-idle.json').read_text())
image_path=SOURCE/approved['file']
sha=lambda path:hashlib.sha256(path.read_bytes()).hexdigest()
assert sha(image_path)==approved['sha256'], 'Approved idle bytes changed.'
image=Image.open(image_path)
assert image.mode=='RGBA' and list(image.size)==approved['canvas'], 'Use the reviewed RGBA canvas.'
before=Image.open(SOURCE/'idle-before-colour.png')
assert before.size==image.size and image.getchannel('A').tobytes()==before.getchannel('A').tobytes(), 'Colour correction must not change the matte.'
alpha=image.getchannel('A')
assert alpha.getextrema()==(0,255)
w,h=image.size
for box in [(0,0,w,1),(0,h-1,w,h),(0,0,1,h),(w-1,0,w,h)]:
    assert alpha.crop(box).getextrema()[1]==0, 'Character must remain inside its canvas.'
motion=json.loads((ROOT/'motion.json').read_text())
asset=next(a for a in motion['assets'] if a['file']=='idle.png')
assert [asset['width'],asset['height']]==approved['canvas']
assert all(abs(asset['pivot'][axis]-approved['pivotBottomLeft'][axis])<1e-7 for axis in ['x','y'])
walk_hashes={name:sha(ROOT/(name+'.png')) for name in motion['walk']}
shutil.copy2(image_path,ROOT/'idle.png')
asset['sha256']=approved['sha256']
motion['idleStatus']='user-approved colour-matched static image (2026-11-10); reviewed canvas, scale and foot pivot preserved'
motion['idleApprovedDate']=approved['approvedDate']
(ROOT/'motion.json').write_text(json.dumps(motion,ensure_ascii=False,indent=2)+'\n')
processing={k:approved[k] for k in ['sourceImageSize','sourceImageSha256','wholeImageScale','offset','targetVisibleHeight','sourceBeltCenter','targetBeltCenter','canvas','footAnchorTopLeft','pivotBottomLeft','method','redrawing','assembledParts']}
processing['approvedImageSha256']=approved['sha256']
processing['alignmentAlreadyBaked']=True
processing['colourMatchParameters']='idle-colour-match.json'
(SOURCE/'idle-processing.json').write_text(json.dumps(processing,ensure_ascii=False,indent=2)+'\n')
assert all(sha(ROOT/(name+'.png'))==value for name,value in walk_hashes.items())
print(json.dumps({'publishedIdleSha256':approved['sha256'],'canvas':approved['canvas'],'pivot':asset['pivot'],'pixelsPerUnit':asset['pixelsPerUnit'],'walkFramesPreserved':len(walk_hashes)}))
