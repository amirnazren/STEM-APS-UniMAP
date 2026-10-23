"""Composition-only review of shipped sprites and design coordinates. NOT a Unity render/test."""
from pathlib import Path
import json
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'unity/Assets/Level1/Resources/Level1'
DOC=ROOT/'docs/level1'
layout=json.loads((DOC/'level-layout.json').read_text())  # exported by tests from actual C# level data
im=Image.new('RGBA',(1280,720),'#102E3B')
d=ImageDraw.Draw(im)
cream='#FFF3D3';ink='#163F4A'

def text(s,x,y,size=20,color=cream,bold=False):
    f=ImageFont.truetype(str(ART/('InterfaceBold.ttf' if bold else 'Interface.ttf')),size)
    d.text((x,y),s,font=f,fill=color)

def button(s,x,y,w,h,c,size=18):
    d.rectangle((x,y,x+w,y+h),fill=c)
    f=ImageFont.truetype(str(ART/'InterfaceBold.ttf'),size)
    box=d.textbbox((0,0),s,font=f)
    d.text((x+(w-box[2])/2,y+(h-(box[3]-box[1]))/2-box[1]),s,font=f,fill=ink)

def art(name,x,y,w,h):
    asset=Image.open(ART/(name+'.png')).convert('RGBA').resize((round(w),round(h)),Image.Resampling.LANCZOS)
    im.alpha_composite(asset,(round(x),round(y)))

def project(c):return (120+(c[0]+c[1])*67,370+(c[0]-c[1])*33)

art('robot',24,15,72,72)
text('ECO-BOT BUILDERS',106,20,15,'#8DD9C2',True)
text('Tideleaf Island',104,50,32,cream,True)
button('?  Help',828,28,120,48,cream);button('Pause',960,28,120,48,cream);button('Sound: on',1092,28,160,48,cream)
d.rectangle((28,102,1252,104),fill='#3B696E')
text('01  /  SKY-ISLE CLEANUP',32,123,14,'#8DD9C2',True)
text('A little code. A cleaner island.',32,154,27,cream,True)
text(f"Clean tokens  0 / {len(layout['trash'])}    ·    Collect every litter pile",32,195,21,cream,True)
for i in range(8):text('~  ~',45+(i%4)*170,260+(i//4)*250,24,'#265562')
for depth in range(-(layout['height']-1),layout['width']):
    for y in range(layout['height']):
        for x in range(layout['width']):
            if x-y==depth:
                px,py=project((x,y));art('grass',px-75,py-54.3,150,117)
px,py=project(layout['spawn']);text('START',px-22,py+18,11,cream,True)
for c in layout['obstacles']:
    px,py=project(c);art('stone',px-42,py-72,84,84)
for c in layout['trash']:
    px,py=project(c);art('litter',px-32,py-40,64,64)
px,py=project(layout['spawn'])
text('➜',px+25,py+6,35,cream,True)
art('robot',px-50.5,py-89,101,101)
text("Move onto litter, then Clean. Let's help the island!",32,544,18)
text('YOUR BLOCKS · tap to add, or drag into your code',32,597,15,'#B5DAD7',True)
for i,(name,c) in enumerate([('Move','#8DD9C2'),('Turn left','#A2CDEB'),('Turn right','#A2CDEB'),('Clean','#F5CB7D'),('Repeat','#C8B8ED')]):
    button(name,32+i*130,626,120,54,c,17)
d.rectangle((718,120,1252,610),fill=cream)
text('YOUR PROGRAM',738,140,23,ink,True);text('0 / 10 blocks',1006,142,18,ink,True)
text('Adding to: Main program · Main / Body selects destination',738,176,15,ink)
button('+ Main',738,205,126,40,'#CDE3D8',16);button('Hint',878,205,100,40,'#F5CB7D',16)
button('Restart',992,205,120,40,'#E7DCC2',16);text('10 slots',1124,214,16,ink)
d.rectangle((738,258,1228,560),fill='#F4E8CB')
button('+ Add to Main / drop here',738,262,476,44,'#B9DCCB',16)
for i,line in enumerate(['1. Tap Move in the palette.','2. Tap Clean.','3. Press Run to test your idea.','','Look for a pattern you can Repeat!']):text(line,758,324+i*28,21,ink)
text('Build your code, then press Run.',738,574,16,ink)
button('RUN  ▶',718,626,260,54,'#8DD9C2',22);button('Stop / reset',990,626,262,54,'#E7DCC2',20)
text('ART COMPOSITION ONLY — not a Unity screenshot or interaction test',32,693,12,'#8AB6B6')
im.convert('RGB').save(DOC/'art-preview.png')
print('Saved art-preview.png (authored asset composition; not Unity).')
