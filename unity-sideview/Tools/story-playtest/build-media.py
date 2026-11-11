#!/usr/bin/env python3
"""Build local story animatics from the checked-in script and existing world art."""
import argparse,json,subprocess
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--output',required=True);args=p.parse_args()
project=Path(__file__).resolve().parents[2]
output=Path(args.output).resolve();output.mkdir(parents=True,exist_ok=True)
clips=json.loads((project/'Assets/Seabreeze/Resources/StoryPlaytest/stories.json').read_text())['clips']
font='/System/Library/Fonts/Supplemental/Songti.ttc'
for index,clip in enumerate(clips):
 target=output/(clip['id']+'.mp4')
 if target.exists():continue
 work=output/('source-'+clip['id']);work.mkdir(exist_ok=True)
 title=work/'title.txt';title.write_text(clip['title'])
 art='cafe.png' if clip['id'].startswith('liting') else 'lighthouse_day.png' if clip['id'].startswith(('liyuan','axing')) else 'street_day.png'
 source=project/'Assets/Seabreeze/Resources/Art'/art
 duration=len(clip['lines'])*6
 filters=["scale=1440:900:force_original_aspect_ratio=increase","crop=1440:900","zoompan=z='min(zoom+0.0002,1.06)':x='iw/2-iw/zoom/2':y='ih/2-ih/zoom/2':d=1:s=1280x720:fps=24","eq=brightness=-0.10:saturation=0.85","drawbox=x=0:y=0:w=iw:h=130:color=black@0.40:t=fill","drawbox=x=0:y=535:w=iw:h=185:color=black@0.72:t=fill",f"drawtext=fontfile={font}:textfile={title}:fontsize=42:fontcolor=0xf0d5a0:x=64:y=48"]
 for n,line in enumerate(clip['lines']):
  text=work/f'{n}.txt';text.write_text(line)
  filters.append(f"drawtext=fontfile={font}:textfile={text}:fontsize=32:fontcolor=white:x=(w-text_w)/2:y=610:enable='between(t,{n*6},{(n+1)*6})'")
 filters+=['fade=t=in:st=0:d=0.6',f'fade=t=out:st={duration-0.6}:d=0.6','format=yuv420p']
 subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-y','-loop','1','-i',str(source),'-vf',','.join(filters),'-t',str(duration),'-r','24','-an','-c:v','libx264','-preset','veryfast','-crf','27','-movflags','+faststart',str(target)],check=True)
 print(f'{index+1}/{len(clips)} {clip["id"]}: {target.stat().st_size} bytes',flush=True)
print('Media ready:',output,flush=True)
