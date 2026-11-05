"""Produce transparent geometric guides at the exact current source canvas size."""
from pathlib import Path
import html,json
BASE=Path(__file__).resolve().parents[1]
audit=json.loads((BASE/'evidence/project-audit.json').read_text())
for s in audit['scenes']:
    g=s['geometry']; w=s['width']; f=g['floorPixelY']
    svg=['<svg xmlns="http://www.w3.org/2000/svg" width="1672" height="941" viewBox="0 0 1672 941">',
         '<title>'+s['name']+' transparent layout guides; NOT runtime artwork</title>',
         '<g font-family="sans-serif" font-size="19">',
         f'<path d="M0 {f}H1672" stroke="#21e0dc" stroke-width="3" stroke-dasharray="14 7"/>']
    cameraWidth=g['cameraWidthAt16by9']/w*1672
    cameraHeight=g['cameraHeight']/g['worldHeight']*941
    for cx,color,label in [(g['cameraMinX'],'#eb9e52','left'),(0,'#fff4c6','center'),(g['cameraMaxX'],'#70c7f3','right')]:
        x=(cx/w+.5)*1672-cameraWidth/2
        svg.append(f'<rect x="{x}" y="{g["cameraTopPixel"]}" width="{cameraWidth}" height="{cameraHeight}" fill="none" stroke="{color}" stroke-width="2" stroke-dasharray="10 5"/>')
        svg.append(f'<text x="{x+12}" y="{g["cameraTopPixel"]+30}" fill="{color}" stroke="#102139" stroke-width=".5">16:9 {label}</text>')
    for i,a in enumerate(s['anchors']):
        x=a['pixelX']; y=f-24-(i%2)*50
        color='#dda02e' if a['door'] else '#a57ad4' if a['npc'] else '#198fbd'
        svg.append(f'<path d="M{x} {y}V{f}" stroke="{color}" stroke-width="3"/><circle cx="{x}" cy="{y}" r="19" fill="{color}" stroke="white" stroke-width="2"/><text x="{x}" y="{y+7}" fill="white" text-anchor="middle">{i+1}</text>')
    ah=g['referenceCharacterPixelHeight']
    svg.append(f'<rect x="820" y="{f-ah}" width="32" height="{ah}" fill="#ffc96f" fill-opacity=".15" stroke="#ffc96f" stroke-width="3"/>')
    svg.append(f'<rect x="8" y="{f+12}" width="670" height="36" fill="#142439" fill-opacity=".95"/><text x="20" y="{f+37}" fill="#c4ffff">{s["name"]} · ground y=0 · pixel y=785.735 · character {ah:.1f}px</text>')
    svg.extend(['</g>','</svg>'])
    (BASE/f'templates/{s["name"]}.guides.svg').write_text('\n'.join(svg))
print('Generated 3 transparent SVG layout guides; no original images were altered.')
