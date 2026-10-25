"""Generate Level 2 solar-panel sprites (dusty + clean) as 1024x1024 RGBA PNGs.
2.5D isometric style consistent with the Level 1 sprites. Pure Pillow geometry;
NOT a Unity render. Each image keeps fully-transparent margins (alpha min == 0)
so the static audit's transparency check passes."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import random

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'unity/Assets/Level1/Resources/Level1'
S = 1024
SS = 2  # supersample factor for smooth edges
W = S * SS


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def quad(d, pts, fill):
    d.polygon(pts, fill=fill)


def build(dusty: bool) -> Image.Image:
    im = Image.new('RGBA', (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(im, 'RGBA')
    cx = W / 2
    cy = W / 2 + 40 * SS

    # Isometric panel surface: a rhombus (diamond) tilted toward the viewer.
    half_w = 300 * SS   # horizontal radius
    half_h = 150 * SS   # vertical radius (iso foreshortening)
    top = (cx, cy - half_h)
    right = (cx + half_w, cy)
    bottom = (cx, cy + half_h)
    left = (cx - half_w, cy)

    # --- Support stand / legs (drawn first, behind the panel) ---
    leg_dark = (44, 58, 66, 255)
    leg_mid = (70, 92, 104, 255)
    leg_h = 150 * SS
    for base in (left, bottom, right):
        bx, by = base
        d.polygon([(bx - 10 * SS, by), (bx + 10 * SS, by),
                   (bx + 10 * SS, by + leg_h), (bx - 10 * SS, by + leg_h)],
                  fill=leg_mid if base == right else leg_dark)
    # cross bar
    d.line([left[0], left[1] + leg_h * 0.6, right[0], right[1] + leg_h * 0.6],
           fill=leg_dark, width=12 * SS)

    # --- Panel side thickness (extruded downward for a 2.5D slab) ---
    thick = 34 * SS
    frame = (30, 40, 48, 255)
    frame_lit = (52, 68, 80, 255)
    # front-left face
    quad(d, [left, bottom, (bottom[0], bottom[1] + thick), (left[0], left[1] + thick)], frame)
    # front-right face
    quad(d, [bottom, right, (right[0], right[1] + thick), (bottom[0], bottom[1] + thick)], frame_lit)

    # --- Panel top surface base color ---
    if dusty:
        c_near = (120, 124, 120)   # muted, greyed blue
        c_far = (86, 92, 96)
    else:
        c_near = (74, 150, 214)    # bright photovoltaic blue
        c_far = (38, 92, 150)
    # paint gradient rhombus by scanning rows
    minx = int(left[0]); maxx = int(right[0])
    miny = int(top[1]); maxy = int(bottom[1])
    grad = Image.new('RGBA', (W, W), (0, 0, 0, 0))
    gd = ImageDraw.Draw(grad)
    steps = 120
    for i in range(steps):
        t0 = i / steps
        t1 = (i + 1) / steps
        y0 = top[1] + (bottom[1] - top[1]) * t0
        y1 = top[1] + (bottom[1] - top[1]) * t1
        col = lerp(c_far + (255,), c_near + (255,), t0)
        # width of rhombus at this vertical fraction (diamond narrows toward top/bottom)
        def hw(t):
            return half_w * (1 - abs(2 * t - 1))
        x0l = cx - hw(t0); x0r = cx + hw(t0)
        x1l = cx - hw(t1); x1r = cx + hw(t1)
        gd.polygon([(x0l, y0), (x0r, y0), (x1r, y1), (x1l, y1)], fill=col)
    im.alpha_composite(grad)

    # --- Grid of cells on the surface (iso lines parallel to the two edges) ---
    grid = (18, 30, 40, 200) if not dusty else (70, 78, 80, 170)
    n = 6
    for k in range(1, n):
        f = k / n
        # lines parallel to top-left edge: from left->top side to bottom->right side
        a = lerp(left, top, f); b = lerp(bottom, right, f)
        d.line([a, b], fill=grid, width=3 * SS)
        # lines parallel to top-right edge
        a2 = lerp(top, right, f); b2 = lerp(left, bottom, f)
        d.line([a2, b2], fill=grid, width=3 * SS)

    # --- Outer frame border of the panel ---
    border = (210, 214, 220, 255) if not dusty else (150, 150, 146, 255)
    d.line([top, right, bottom, left, top], fill=border, width=6 * SS, joint='curve')

    if dusty:
        # Dust haze: semi-opaque tan overlay clipped to the rhombus + speckles.
        haze = Image.new('RGBA', (W, W), (0, 0, 0, 0))
        hd = ImageDraw.Draw(haze)
        hd.polygon([top, right, bottom, left], fill=(196, 180, 150, 150))
        rnd = random.Random(7)
        for _ in range(900):
            # random point inside rhombus
            u = rnd.random(); v = rnd.random()
            if u + v > 1:
                u, v = 1 - u, 1 - v
            px = left[0] + u * (top[0] - left[0]) + v * (bottom[0] - left[0])
            py = left[1] + u * (top[1] - left[1]) + v * (bottom[1] - left[1])
            r = rnd.randint(2, 7) * SS
            a = rnd.randint(40, 120)
            hd.ellipse([px - r, py - r, px + r, py + r], fill=(150, 134, 104, a))
        haze = haze.filter(ImageFilter.GaussianBlur(2 * SS))
        im.alpha_composite(haze)
        # a couple of dust clumps
        dd = ImageDraw.Draw(im, 'RGBA')
        for (px, py, r) in [(cx - 120 * SS, cy - 10 * SS, 40 * SS),
                            (cx + 90 * SS, cy + 30 * SS, 52 * SS),
                            (cx + 10 * SS, cy - 70 * SS, 30 * SS)]:
            dd.ellipse([px - r, py - r * 0.6, px + r, py + r * 0.6], fill=(176, 158, 120, 150))
    else:
        # Clean: glossy highlight streak across the upper-left of the surface.
        gloss = Image.new('RGBA', (W, W), (0, 0, 0, 0))
        gl = ImageDraw.Draw(gloss)
        p0 = lerp(left, top, 0.15)
        p1 = lerp(left, top, 0.55)
        p2 = lerp(top, right, 0.30)
        p3 = lerp(top, right, 0.08)
        gl.polygon([p0, p1, p2, p3], fill=(255, 255, 255, 90))
        gloss = gloss.filter(ImageFilter.GaussianBlur(6 * SS))
        im.alpha_composite(gloss)
        # small sparkle
        sd = ImageDraw.Draw(im, 'RGBA')
        sx, sy = lerp(top, right, 0.35)
        for (lw, ln, a) in [(5 * SS, 46 * SS, 235), (5 * SS, 30 * SS, 235)]:
            sd.line([sx - ln, sy, sx + ln, sy], fill=(255, 255, 255, a), width=lw)
            sd.line([sx, sy - ln, sx, sy + ln], fill=(255, 255, 255, a), width=lw)

    out = im.resize((S, S), Image.LANCZOS)
    return out


def main():
    for name, dusty in [('panel_dusty', True), ('panel_clean', False)]:
        img = build(dusty)
        assert img.size == (S, S) and img.mode == 'RGBA'
        assert img.getextrema()[3][0] == 0, name + ' must have a transparent pixel'
        img.save(ART / (name + '.png'))
        print('wrote', name + '.png', img.size, 'alpha extrema', img.getextrema()[3])


if __name__ == '__main__':
    main()
