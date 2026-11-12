#!/usr/bin/env python3
"""Original Project Z instrumental score. No samples, external recordings or copied melodies.

Production source, not a game runtime dependency. Requires Python + numpy + ffmpeg.
Writes lossless FLAC masters here and looping Vorbis copies to the Unity Resources folder.
All oscillator tails and room echoes wrap at the exact musical loop boundary.
"""
from pathlib import Path
import json
import math
import subprocess
import tempfile
import wave
import numpy as np

RATE = 44100
HERE = Path(__file__).resolve().parent
OUT = HERE.parents[2] / "Assets/Seabreeze/Resources/Audio/Original"
RNG = np.random.default_rng(20261112)
TAU = 2 * math.pi


def voice(midi, seconds, kind):
    n = int(RATE * seconds)
    t = np.arange(n, dtype=np.float64) / RATE
    f = 440 * 2 ** ((midi - 69) / 12)
    if kind == "felt":
        s = np.zeros(n)
        for h, gain in enumerate([1, .37, .15, .065, .035, .012], 1):
            # Small inharmonicity and different partial decays soften a piano-like hammer attack.
            s += gain * np.sin(TAU * f * h * (1 + .00008 * h * h) * t) * np.exp(-t * (.92 + h * .55))
        s *= (1 - np.exp(-t * 110)) * np.minimum(1, (seconds - t) / .18)
    elif kind == "rhodes":
        mod = .8 * np.sin(TAU * f * 2 * t) * np.exp(-t * 2.3)
        s = (np.sin(TAU * f * t + mod) + .14 * np.sin(TAU * f * 3 * t)) * np.exp(-t * 1.25)
        s *= (1 - np.exp(-t * 65)) * np.minimum(1, (seconds - t) / .25)
    elif kind == "pluck":
        s = sum(g * np.sin(TAU * f * h * t) * np.exp(-t * (2.8 + h)) for h, g in [(1, 1), (2, .3), (3, .12), (4, .035)])
        s *= (1 - np.exp(-t * 160)) * np.minimum(1, (seconds - t) / .12)
    elif kind == "bass":
        s = (np.sin(TAU * f * t) + .17 * np.sin(TAU * f * 2 * t)) * np.exp(-t * 1.7)
        s *= (1 - np.exp(-t * 80)) * np.minimum(1, (seconds - t) / .2)
    elif kind == "bell":
        s = sum(g * np.sin(TAU * f * h * t) * np.exp(-t * d) for h, g, d in [(1, 1, 1.25), (2, .28, 2.3), (3.01, .035, 4)])
        s *= (1 - np.exp(-t * 95)) * np.minimum(1, (seconds - t) / .25)
    else:
        s = (np.sin(TAU * f * t) + .12 * np.sin(TAU * f * 2 * t) + .04 * np.sin(TAU * f * 3 * t))
        s *= np.minimum(1, t / .55) * np.minimum(1, (seconds - t) / .9)
    return s.astype(np.float32)


def add(buf, mono, at, gain, pan=0):
    pos = int(round(at * RATE)) % len(buf)
    stereo = mono[:, None] * np.array([math.cos((pan + 1) * math.pi / 4), math.sin((pan + 1) * math.pi / 4)], np.float32) * gain
    first = min(len(stereo), len(buf) - pos)
    buf[pos:pos + first] += stereo[:first]
    if first < len(stereo):
        buf[:len(stereo) - first] += stereo[first:]


def perc(kind):
    dur = {"kick": .19, "brush": .14, "hat": .055}[kind]
    t = np.arange(int(dur * RATE)) / RATE
    if kind == "kick":
        phase = TAU * (48 * t + 32 * .021 * (1 - np.exp(-t / .021)))
        s = np.sin(phase) * np.exp(-t * 24) * (1 - np.exp(-t * 600))
    else:
        noise = RNG.standard_normal(len(t))
        if kind == "brush":
            smooth = np.convolve(noise, np.ones(9) / 9, mode="same")
            s = (noise - smooth) * .45 * np.exp(-t * 32) * (1 - np.exp(-t * 450))
        else:
            s = np.diff(noise, prepend=0) * .21 * np.exp(-t * 95) * (1 - np.exp(-t * 800))
    return s.astype(np.float32)


# Voiced ninth/seventh harmony: bass note, then upper tones. Original 8-bar progressions.
HARMONIES = {
    "day": [(48,[60,64,67,71,74]), (45,[60,64,67,71]), (53,[60,64,67,69]), (55,[59,62,65,69]),
            (52,[59,62,67,71]), (57,[60,64,67,71]), (50,[60,64,65,69]), (55,[59,62,67,69])],
    "evening": [(46,[58,62,65,69,72]), (43,[58,62,65,69]), (51,[58,62,65,67]), (53,[57,60,63,67]),
                (50,[57,60,65,69]), (55,[58,62,65,69]), (48,[58,62,63,67]), (53,[57,60,65,67])],
    "home": [(50,[62,66,69,73,76]), (47,[62,66,69,73]), (55,[62,66,69,71]), (57,[61,64,67,71]),
             (54,[61,64,69,73]), (59,[62,66,69,73]), (52,[62,66,67,71]), (57,[61,64,69,71])],
    "credits": [(48,[60,64,67,71,74]), (55,[59,62,67,69]), (57,[60,64,67,71]), (53,[60,64,67,69]),
                (52,[59,62,67,71]), (57,[60,64,67,71]), (50,[60,64,65,69]), (55,[59,62,67,69])],
}

# Beat offset / pitch / gate. Rest and response are intentional; no constant arpeggio lead.
MELODY = [
    [(0.0,76,.8),(1.5,74,.45),(2.25,71,1.25)],
    [(.5,72,.8),(2.0,71,.45),(2.75,67,.8)],
    [(0.0,69,.9),(1.5,72,.55),(2.5,76,1.1)],
    [(.5,74,1.2),(2.5,69,.7)],
    [(0.25,71,.65),(1.25,74,.6),(2.5,79,1.0)],
    [(.0,76,1.2),(2.0,74,.5),(3.0,72,.55)],
    [(.5,69,.8),(2.0,65,.65),(3.0,67,.55)],
    [(.0,71,.85),(1.5,69,.5),(2.5,67,.85)],
]


def render(key, bpm, bars, filename, title):
    beat = 60 / bpm
    duration = bars * 4 * beat
    buf = np.zeros((round(duration * RATE), 2), dtype=np.float32)
    transpose = {"day":0,"evening":-2,"home":2,"credits":0}[key]
    soft = key == "home"
    lead = "rhodes" if key == "evening" else "felt"
    cache = {}
    def note(midi, at, dur, gain, pan=0, kind=lead):
        k = (midi, round(dur,4), kind)
        if k not in cache: cache[k] = voice(midi, dur, kind)
        add(buf, cache[k], at, gain, pan)
    drums = {k:perc(k) for k in ["kick","brush","hat"]}
    for bar in range(bars):
        at = bar * 4 * beat
        bass, chord = HARMONIES[key][bar % 8]
        section = bar // 8
        for j, p in enumerate(chord):
            note(p, at + j * .018, 3.1 * beat, .030 if soft else .038, -.28, "rhodes")
        # Breath-like warm string bed; always subordinate to the piano phrase.
        if bar % 2 == 0:
            for p in chord[:3]: note(p - 12, at, 5.2 * beat, .012, .35, "pad")
        note(bass, at + .012, 1.8 * beat, .11 if not soft else .075, -.05, "bass")
        note(bass + (7 if bar % 2 else 12), at + 2.5 * beat, 1.3 * beat, .055, -.05, "bass")
        # A nylon-string reply in between melody phrases.
        for k in range(4):
            if soft and k % 2: continue
            note(chord[(k + bar) % len(chord)], at + (k + .75) * beat, 1.35 * beat,
                 .024 if soft else .032, .4, "pluck")
        phrase = MELODY[bar % 8]
        for i, (offset, pitch, gate) in enumerate(phrase):
            # The middle phrase is a lower, quieter answer; last phrase returns with a new cadence.
            pitch += transpose
            if section == 1 and bar % 8 in (1,3,5,7): pitch -= 12
            if key == "credits" and section == 2 and bar % 8 in (4,5): pitch += 12
            if key == "home" and bar % 8 in (3,7) and i == 1: continue
            human = RNG.uniform(-.014,.014)
            note(pitch, at + offset * beat + human, (gate + 1.8) * beat,
                 (.105 if soft else .12) * RNG.uniform(.90,1.04), -.10)
        if section > 0 and bar % 4 == 2:
            note(chord[-1] + 12, at + 3.0 * beat, 3.0 * beat, .025, .5, "bell")
        if not soft:
            for offset, kind, gain in [(0,"kick",.075),(2,"kick",.047),(1,"brush",.038),(3,"brush",.032)]:
                add(buf, drums[kind], at + offset * beat + RNG.uniform(-.007,.007), gain, .05)
            for eighth in range(8):
                add(buf, drums["hat"], at + (eighth * .5 + (.055 if eighth % 2 else 0)) * beat,
                    .015 if eighth % 2 else .021, -.45)
    # Circular early reflections make a quiet room around the dry notes and stay sample-loopable.
    dry = buf.copy()
    for delay, gain, swap in [(.073,.12,True),(.137,.095,False),(.233,.075,True),(.371,.052,False),(.499,.038,True),(.683,.024,False)]:
        echo = np.roll(dry, int(delay * RATE), axis=0)
        buf += (echo[:,::-1] if swap else echo) * gain
    # Gentle DC removal, tanh soft saturation; measured peak below -1 dBFS.
    buf -= np.mean(buf,axis=0)
    rms = float(np.sqrt(np.mean(buf ** 2)))
    buf *= .13 / max(rms, .001)
    buf = np.tanh(buf * 1.15) / 1.15
    buf *= min(1, .87 / float(np.max(np.abs(buf))))
    # Blend only 128 samples across the boundary to remove residual non-periodic oscillator click.
    # This is <3 ms and does not fade the musical phrase or remove the last beat.
    edge = 128
    weight = np.linspace(0,1,edge,dtype=np.float32)[:,None]
    delta = (buf[0] - buf[-1]).copy()
    buf[-edge:] += weight * delta * .5
    buf[:edge] -= (1-weight) * delta * .5
    pcm = (np.clip(buf,-1,1)*32767).astype('<i2')
    OUT.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="z-score-render-") as temp:
        wav = Path(temp)/"master.wav"
        with wave.open(str(wav),'wb') as w:
            w.setnchannels(2);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(pcm.tobytes())
        for dest, codec in [(HERE/(filename+'.flac'),['-c:a','flac']), (OUT/(filename+'.ogg'),['-c:a','vorbis','-strict','experimental','-b:a','192k'])]:
            subprocess.run(['ffmpeg','-y','-v','error','-i',str(wav),*codec,'-metadata','title='+title,
                            '-metadata','artist=来自月亮的Z · Original Demo Score',str(dest)],check=True)
    return dict(file=filename,title=title,bpm=bpm,bars=bars,durationSeconds=round(duration,6),
                peakDb=round(20*math.log10(float(np.max(np.abs(buf)))),2),
                rmsDb=round(20*math.log10(float(np.sqrt(np.mean(buf**2)))),2))


if __name__ == '__main__':
    entries = []
    for args in [('day',78,24,'morning_letters','海风来信'),('evening',70,24,'lantern_tide','灯火与潮汐'),
                 ('home',64,24,'a_window_for_you','为你留一扇窗'),('credits',72,24,'after_the_fireworks','烟花之后')]:
        print('Composing',args[3],flush=True)
        entries.append(render(*args))
    (HERE/'render-manifest.json').write_text(json.dumps(entries,ensure_ascii=False,indent=2)+'\n')
