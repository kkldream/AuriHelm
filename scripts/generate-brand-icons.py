"""Render the approved AuriHelm vector geometry offline. Requires Pillow; no network or GUI."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
DEST = ROOT / "src/AudioOutputManager.App/Assets"
SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def render(size, tile):
    scale = 4
    canvas = Image.new("RGBA", (size * scale, size * scale))
    draw = ImageDraw.Draw(canvas)
    unit = size * scale / 64
    def point(x, y):
        return (x * unit, y * unit)
    if tile:
        mask = Image.new("L", canvas.size)
        ImageDraw.Draw(mask).rounded_rectangle((0, 0, canvas.width-1, canvas.height-1), radius=13*unit, fill=255)
        for y in range(canvas.height):
            for x in range(canvas.width):
                t = (x+y)/(2*(canvas.width-1))
                color = tuple(round(a+(b-a)*t) for a,b in zip((25,58,96),(14,38,62)))
                canvas.putpixel((x,y), (*color, mask.getpixel((x,y))))
        draw = ImageDraw.Draw(canvas)
    color = "#26A5ED" if tile else "#FFFFFF"
    # The 16 px optical variant has wider gaps and a slightly heavier ring.
    small = size == 16
    width = (5.6 if small else 4.6)*unit
    radius = 24 if small else 22
    outer = radius + width / (2*unit)
    draw.arc((*point(32-outer,32-outer), *point(32+outer,32+outer)), 0,315,fill=color,width=round(width))
    import math
    for angle in (0,315):
        x,y = point(32+radius*math.cos(math.radians(angle)),32+radius*math.sin(math.radians(angle)))
        draw.ellipse((x-width/2,y-width/2,x+width/2,y+width/2),fill=color)
    def line(coords, fill):
        coords=[point(*p) for p in coords]
        draw.line(coords,fill=fill,width=round(width))
        for x,y in (coords[0],coords[-1]):
            draw.ellipse((x-width/2,y-width/2,x+width/2,y+width/2),fill=fill)
    line([(42.4,21.6),(54.4,9.6)] if small else [(43,21),(52,12)],color)
    waves = [(20,28.8,36.8),(30.4,23.2,41.6),(40.8,28,38)] if small else [(22,29,36),(30,24,40),(38,28,36)]
    for x,y1,y2 in waves:
        line([(x,y1),(x,y2)],"#EFF6FF" if tile else color)
    return canvas.resize((size,size),Image.Resampling.LANCZOS)


if __name__ == "__main__":
    DEST.mkdir(exist_ok=True)
    for name,tile in (("AuriHelm",True),("AuriHelm.Tray",False)):
        frames=[render(s,tile) for s in SIZES]
        frames[-1].save(DEST / (name+".ico"),sizes=[(s,s) for s in SIZES],append_images=frames[:-1])
    render(256,True).save(DEST / "AuriHelm.png")
    (DEST / "AuriHelm.svg").write_text('''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" fill="none">
  <title>AuriHelm：聲波旋鈕</title>
  <defs><linearGradient id="tile" x2="1" y2="1"><stop stop-color="#193A60"/><stop offset="1" stop-color="#0E263E"/></linearGradient></defs>
  <rect width="64" height="64" rx="13" fill="url(#tile)"/>
  <g stroke="#26A5ED" stroke-width="4.6" stroke-linecap="round" stroke-linejoin="round">
    <path d="M47.6 16.4A22 22 0 1 0 54 32M43 21L52 12"/>
    <path stroke="#EFF6FF" d="M22 29V36M30 24V40M38 28V36"/>
  </g>
</svg>
''',encoding="utf-8")
    print("Generated AuriHelm icons:", ", ".join(str(s) for s in SIZES))
