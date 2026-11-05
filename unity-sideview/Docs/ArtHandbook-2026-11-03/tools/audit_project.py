"""Read the project's saved scene YAML without opening or modifying Unity scenes.

Only project-specific serialization patterns used by the three current scenes are
supported. This is a snapshot generator, not a general-purpose Unity YAML loader.
"""
from pathlib import Path
import csv, hashlib, json, re, struct

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parents[1]
ART = ROOT / 'Assets/Seabreeze/Resources/Art'

def scalar(text, key, default=None):
    match = re.search(r'^  '+re.escape(key)+r':\s*([^\n]*)$', text, re.M)
    if not match: return default
    value = match.group(1).strip()
    try: return json.loads(value)
    except (ValueError, TypeError):
        try: return float(value)
        except ValueError: return value

def vector(text, key):
    value = scalar(text, key, '{}')
    return {k:float(v) for k,v in re.findall(r'(\w+):\s*([-\d.eE+]+)',value)}

def documents(text):
    return [(int(m[0]),int(m[1]),m[2]) for m in re.findall(
        r'^--- !u!(\d+) &(\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)',text,re.M|re.S)]

def png_info(path):
    raw=path.read_bytes()
    w,h,depth,kind=struct.unpack('>IIBB',raw[16:26])
    return dict(file=path.name,width=w,height=h,bitDepth=depth,colorType=kind,
                bytes=len(raw),sha256=hashlib.sha256(raw).hexdigest())

def scene_info(name):
    path=ROOT/f'Assets/Seabreeze/Scenes/{name}.unity'
    text=path.read_text()
    docs=documents(text)
    stage=next(t for c,i,t in docs if 'Assembly-CSharp::Seabreeze.WorldScene' in t)
    data={key:scalar(stage,key) for key in ['place','width','floorImageFraction',
          'streetSpawn','fromCafe','fromLighthouse','cameraSize','cameraY']}
    data.update(name=name,file=str(path.relative_to(ROOT)),anchors=[],colliders=[])
    go={str(i):scalar(t,'m_Name') for c,i,t in docs if c==1 and 'm_Name:' in t}
    transforms={re.search(r'm_GameObject: \{fileID: (\d+)\}',t)[1]:t
                for c,i,t in docs if c==4 and 'm_GameObject:' in t}
    for c,i,t in docs:
        if c==61:
            go_id=re.search(r'm_GameObject: \{fileID: (\d+)\}',t)[1]
            data['colliders'].append(dict(name=go.get(go_id),position=vector(transforms[go_id],'m_LocalPosition'),
                size=vector(t,'m_Size'),offset=vector(t,'m_Offset'),usedByEffector=scalar(t,'m_UsedByEffector')))
        if c==212:
            go_id=re.search(r'm_GameObject: \{fileID: (\d+)\}',t)[1]
            data['backdrop']=dict(name=go.get(go_id),position=vector(transforms[go_id],'m_LocalPosition'),
                scale=vector(transforms[go_id],'m_LocalScale'),order=scalar(t,'m_SortingOrder'))
        if c!=1001: continue
        props={k:v for k,v in re.findall(r'propertyPath: ([^\n]+)\n      value:([^\n]*)',t)}
        if 'id' not in props: continue
        def get(k, default=''):
            v=props.get(k,str(default)).strip()
            try: return json.loads(v)
            except ValueError: return v
        x=float(get('m_LocalPosition.x',0)); y=float(get('m_LocalPosition.y',0))
        w=data['width']; h=w*941/1672; f=data['floorImageFraction']
        data['anchors'].append(dict(id=get('id'),label=get('label'),worldX=x,worldY=y,
            u=x/w+.5,v=1-f-y/h,pixelX=1672*(x/w+.5),pixelY=941*(1-f-y/h),
            radius=float(get('radius',1.7)),npc=bool(get('npc',0)),door=bool(get('isDoor',0)),
            destination=get('destination',0),promptOffsetY=float(get('promptOffset.y',2.8))))
    w=data['width']; h=w*941/1672; f=data['floorImageFraction']; s=data['cameraSize']; cy=data['cameraY']
    vw=2*s*16/9
    data['geometry']=dict(imageWidth=1672,imageHeight=941,worldHeight=h,
        worldBottom=-h*f,worldTop=h*(1-f),floorPixelY=941*(1-f),scaleAt100PPU=w/16.72,
        cameraWidthAt16by9=vw,cameraHeight=2*s,cameraMinX=min(0,-w/2+vw/2),cameraMaxX=max(0,w/2-vw/2),
        cameraTopPixel=941*(1-f-(cy+s)/h),cameraBottomPixel=941*(1-f-(cy-s)/h),
        referenceCharacterWorldHeight=3.042,referenceCharacterPixelHeight=3.042*1672/w,
        referenceCharacterScreenPixelHeight=1080*3.042/(2*s),
        floorScreenPixelY=1080*(.5+cy/(2*s)))
    data['anchors'].sort(key=lambda a:a['worldX'])
    return data

def main():
    out=dict(date='2026-11-03 Asia/Shanghai',project=str(ROOT),
        scope='Saved files and read-only Editor inspection; not new gameplay acceptance',
        unity='6000.3.27f1',urp='17.3.0',maps=[png_info(p) for p in sorted(ART.glob('*.png')) if p.stem not in ['characters','player_walk']],
        scenes=[scene_info(n) for n in ['Street','Cafe','Lighthouse']])
    refs=[ROOT/'ProjectSettings/ProjectVersion.txt',ROOT/'Packages/manifest.json',ROOT/'ProjectSettings/EditorBuildSettings.asset']
    refs+=sorted((ROOT/'Assets/Seabreeze/Scenes').glob('*.unity'))
    refs+=sorted(ART.glob('*'))
    refs+=sorted((ROOT/'Assets/Seabreeze/Resources/UI').glob('*'))
    refs+=sorted((ROOT/'Assets/Seabreeze/Resources/Prefabs').glob('*'))
    refs+=sorted((ROOT/'Assets/Seabreeze/Scripts').rglob('*.cs'))
    refs+=sorted((ROOT/'Assets/Seabreeze/Editor').glob('*.cs'))
    out['sourceHashes']={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in refs if p.is_file()}
    (OUT/'evidence/project-audit.json').write_text(json.dumps(out,ensure_ascii=False,indent=2))
    with (OUT/'地图锚点.csv').open('w',encoding='utf-8-sig',newline='') as f:
        keys=['scene','id','label','worldX','worldY','u','v','pixelX','pixelY','radius','npc','door','destination','promptOffsetY']
        writer=csv.DictWriter(f,fieldnames=keys);writer.writeheader()
        for scene in out['scenes']:
            for point in scene['anchors']:writer.writerow(dict(scene=scene['name'],**point))
    print(json.dumps([dict(name=s['name'],width=s['width'],anchors=len(s['anchors']),geometry=s['geometry']) for s in out['scenes']],ensure_ascii=False,indent=2))

if __name__=='__main__':main()
