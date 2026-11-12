#!/usr/bin/env python3
"""Reproducible limited-animation cinematics. Python stdlib + ffmpeg only.

Existing painted backgrounds remain untouched. Camera motion, typography,
ballistic sparks and audio are authored in this file; this is not character video.
Run from any directory: python3 ArtSource/FirstDay/build_films.py
"""
import os
import math
import random
import subprocess
import tempfile
import wave
from array import array
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path(__file__).parent
OUTPUT = ROOT / 'Assets/StreamingAssets/FirstDay'
FONT = ROOT / 'Assets/Seabreeze/Resources/Fonts/NotoSansSC-Regular.ttf'
ART = ROOT / 'Assets/Seabreeze/Art/Island'
W, H, FPS = 1920, 1080, 30

def run(args):
    subprocess.run([os.environ.get('FFMPEG','ffmpeg'),'-hide_banner','-loglevel','warning','-y',*map(str,args)],check=True)

def text(value,size,x,y,start,end,color='0xf2eee3',alpha=None):
    # These authored strings intentionally contain no filter delimiter characters.
    fade = alpha or f'min(1,(t-{start})/0.6)*min(1,({end}-t)/0.7)'
    extra = ':box=1:boxcolor=0x07101c@0.38:boxborderw=13' if size >= 23 and len(value) > 3 else ''
    return f"drawtext=fontfile='{FONT}':text='{value}':fontsize={size}:fontcolor={color}:x='{x}':y='{y}':alpha='{fade}':enable='between(t,{start},{end})':shadowcolor=0x07101c@0.7:shadowx=1:shadowy=2{extra}"

def soundtrack(path,duration,bursts=()):
    rng=random.Random(1126); rate=44100; samples=array('h'); smooth=0.0
    for i in range(int(duration*rate)):
        t=i/rate; smooth=.985*smooth+.015*rng.uniform(-1,1)
        wave_level=.020*(.7+.3*math.sin(t*.72))
        value=smooth*wave_level
        # Original distant soft ferry horn for arrival; no licensed samples.
        if not bursts and 2.5<t<4.4:
            envelope=math.sin(math.pi*(t-2.5)/1.9)**2
            value+=.013*envelope*(math.sin(t*2*math.pi*146.83)+.4*math.sin(t*2*math.pi*220))
        for burst in bursts:
            dt=t-burst-.22
            if 0<=dt<1.4:
                env=math.exp(-dt*6)*(1-math.exp(-dt*180))
                value+=env*(.13*math.sin(dt*2*math.pi*(58-12*dt))+.055*rng.uniform(-1,1))
        fade=min(1,t/1.1,(duration-t)/1.3)
        v=max(-1,min(1,value*fade)); samples.append(int(v*32767)); samples.append(int(v*32767))
    with wave.open(str(path),'wb') as f:
        f.setnchannels(2);f.setsampwidth(2);f.setframerate(rate);f.writeframes(samples.tobytes())

def shot(path,dest,duration,filters):
    run(['-loop','1','-framerate',FPS,'-i',path,'-t',duration,'-vf',','.join(filters),'-an','-r',FPS,'-c:v','libx264','-preset','fast','-crf','19','-pix_fmt','yuv420p',dest])

def arrival(temp):
    shots=[]
    spec=[
      ('Harbor.png',10,[
        "scale=-2:1160,crop=1920:1080:x='150+42*t':y=10",
        text('一封来自海风的邀请',44,'(w-tw)/2',880,1.3,8.6),
        text('从一块小小的屏幕  到一片真实的海',24,'(w-tw)/2',950,2.5,8.6)]),
      ('Boardwalk.png',8,[
        "scale=2048:1152,crop=1920:1080:x='50+4*t':y=30",
        text('在这里  没有人催你赶路',36,'(w-tw)/2',900,.7,6.9),
        text('一杯热饮  一句问候  也值得被记住',23,'(w-tw)/2',960,1.5,6.9)]),
      ('Harbor.png',8,[
        "scale=-2:1100,crop=1920:1080:x='1460+12*t':y=0",
        text('月亮岛',60,'(w-tw)/2',410,1,6.7),
        text('Z 已经在码头等你了',27,'(w-tw)/2',500,1.7,6.7)])]
    for index,(name,duration,filters) in enumerate(spec):
        dest=temp/f'arrival_{index}.mp4';shots.append(dest)
        shot(ART/name,dest,duration,filters+[f'fade=t=in:st=0:d=0.7',f'fade=t=out:st={duration-.7}:d=0.7'])
    playlist=temp/'arrival.txt'; playlist.write_text(''.join(f"file '{p}'\n" for p in shots))
    sound=temp/'arrival.wav';soundtrack(sound,26)
    run(['-f','concat','-safe','0','-i',playlist,'-i',sound,'-map','0:v','-map','1:a','-c:v','copy','-c:a','aac','-b:a','128k','-movflags','+faststart','-shortest',OUTPUT/'arrival.mp4'])

def fireworks(temp):
    rng=random.Random(18); duration=25
    bursts=[(2.6,1390,250,'0xffdb9b'),(5.0,1620,310,'0xa7e5ff'),(7.8,1290,210,'0xffc6dd'),(10.5,1510,220,'0xffdc9a'),(13.0,1690,260,'0xc1d7ff'),(15.0,1320,190,'0xffdba7'),(16.4,1570,280,'0xffc7cf'),(18.1,1440,180,'0xf8e8b5')]
    filters=['scale=1920:1080','format=yuv420p']
    for start,cx,cy,color in bursts:
        launch=start-.8
        filters.append(text('•',7,cx,f'530-(530-{cy})*(t-{launch})/.8',launch,start,color,'.85'))
        # Ballistic arcs: quick radial expansion, drag, gravity, and warm fading tails.
        for i in range(64):
            angle=2*math.pi*i/64+rng.uniform(-.018,.018);speed=rng.uniform(115,195)
            vx=math.cos(angle)*speed;vy=math.sin(angle)*speed
            d=f'(t-{start})'; radius=f'(1-exp(-{d}*.8))/.8'
            x=f'{cx}+{vx:.4f}*{radius}'; y=f'{cy}+{vy:.4f}*{radius}+23*{d}*{d}'
            # Dense luminous trails avoid the pinprick-only look of point emitters.
            for trail in range(4,-1,-1):
                lag=.026*trail;dt=f'max(0,{d}-{lag})'; tailr=f'(1-exp(-{dt}*.8))/.8'
                strength=1-trail*.13
                filters.append(text('•',14-trail,f'{cx}+{vx:.4f}*{tailr}',f'{cy}+{vy:.4f}*{tailr}+23*{dt}*{dt}',start,start+2.9,color,f'max(0,{strength}-{d}*{strength}/2.9)'))
            filters.append(text('•',12,x,y,start,start+1.6,'0xffffed',f'max(0,1-{d}/1.6)'))
    filters.extend([
        text('第一夜  ·  古树广场',26,65,65,.6,4.8),
        text('「欢迎来到小岛。」',35,'(w-tw)/2',935,3.5,8.0),
        text('这一次  屏幕里的人就在身边',29,'(w-tw)/2',935,10.0,14.5),
        text('愿这些平凡的相遇  都有回声',30,'(w-tw)/2',935,18.7,23.6),
        'fade=t=in:st=0:d=1.1','fade=t=out:st=23.8:d=1.2'])
    # A filter file avoids shell limits and keeps the production recipe reproducible.
    recipe=temp/'fireworks.filters';recipe.write_text(','.join(filters))
    sound=temp/'fireworks.wav';soundtrack(sound,duration,[x[0] for x in bursts])
    run(['-loop','1','-framerate',FPS,'-i',SOURCE/'square_night_plate.png','-i',sound,'-/filter:v',recipe,'-t',duration,'-r',FPS,'-c:v','libx264','-preset','fast','-crf','18','-pix_fmt','yuv420p','-c:a','aac','-b:a','128k','-movflags','+faststart',OUTPUT/'fireworks.mp4'])

if __name__=='__main__':
    OUTPUT.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='z-film-production-') as work:
        temp=Path(work)
        import sys
        if '--fireworks-only' not in sys.argv: arrival(temp)
        fireworks(temp)
    print('Built arrival.mp4 (26s) and fireworks.mp4 (25s), 1920x1080 / 30fps H.264 + AAC')
