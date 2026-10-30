"""Builds docs/demo.gif: a short loop showing DropMe's drag, shake, drop and move.

The shelf and icons are real DropMe windows rendered by tools/screenshots; this script only
moves them around a backdrop with a cursor. Needs Pillow.

Usage: python tools/make_demo_gif.py <sprites folder> <output gif>
"""

import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

WIDTH, HEIGHT = 800, 450
FPS = 25
DURATION = 7.4  # seconds, then loops
ACCENT = (88, 101, 242)

FONTS = Path("C:/Windows/Fonts")


def load_sprite(folder: Path, name: str) -> Image.Image:
    """Sprites are rendered at 2x; bring them back to 1x."""
    image = Image.open(folder / name).convert("RGBA")
    return image.resize((image.width // 2, image.height // 2), Image.LANCZOS)


def backdrop() -> Image.Image:
    top, bottom = (22, 28, 52), (52, 36, 82)
    mask = Image.linear_gradient("L").resize((WIDTH, HEIGHT))
    image = Image.composite(Image.new("RGB", (WIDTH, HEIGHT), bottom), Image.new("RGB", (WIDTH, HEIGHT), top), mask)
    glow = Image.new("L", (WIDTH, HEIGHT), 0)
    ImageDraw.Draw(glow).ellipse((380, -120, 900, 300), fill=90)
    glow = glow.filter(ImageFilter.GaussianBlur(90))
    return Image.composite(Image.new("RGB", (WIDTH, HEIGHT), (96, 84, 170)), image, glow)


def ease(u: float) -> float:
    u = max(0.0, min(1.0, u))
    return 4 * u ** 3 if u < 0.5 else 1 - (-2 * u + 2) ** 3 / 2


def between(t: float, t0: float, t1: float, a, b):
    u = ease((t - t0) / (t1 - t0))
    return (a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * u)


def fade(t: float, t0: float, t1: float) -> float:
    return max(0.0, min(1.0, (t - t0) / (t1 - t0)))


def with_alpha(image: Image.Image, alpha: float) -> Image.Image:
    if alpha >= 1:
        return image
    faded = image.copy()
    faded.putalpha(faded.getchannel("A").point(lambda a: int(a * alpha)))
    return faded


def scaled(image: Image.Image, factor: float) -> Image.Image:
    if abs(factor - 1) < 0.001:
        return image
    return image.resize((max(1, int(image.width * factor)), max(1, int(image.height * factor))), Image.LANCZOS)


def paste_centered(canvas: Image.Image, image: Image.Image, center) -> None:
    canvas.alpha_composite(image, (int(center[0] - image.width / 2), int(center[1] - image.height / 2)))


def cursor_image() -> Image.Image:
    """A Windows-style arrow cursor, drawn at 3x and scaled down for smooth edges."""
    points = [(0, 0), (0, 17), (4, 13), (7, 20), (10, 19), (7, 12), (12, 12)]
    big = Image.new("RGBA", (48, 66), (0, 0, 0, 0))
    draw = ImageDraw.Draw(big)
    draw.polygon([(2 + x * 3, 2 + y * 3) for x, y in points], fill="white", outline="black", width=3)
    return big.resize((16, 22), Image.LANCZOS)


def caption(canvas: Image.Image, step: str, text: str, alpha: float) -> None:
    if alpha <= 0:
        return
    bold = ImageFont.truetype(str(FONTS / "seguisb.ttf"), 17)
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    text_width = draw.textlength(text, font=bold)
    width = int(text_width + 66)
    left, top = (WIDTH - width) // 2, HEIGHT - 66
    draw.rounded_rectangle((left, top, left + width, top + 40), radius=20, fill=(14, 14, 20, 200))
    draw.ellipse((left + 9, top + 8, left + 33, top + 32), fill=ACCENT)
    if step == "done":
        draw.line([(left + 15, top + 20), (left + 19, top + 24), (left + 27, top + 15)], fill="white", width=3)
    else:
        draw.text((left + 21, top + 20), step, font=bold, fill="white", anchor="mm")
    draw.text((left + 44, top + 20), text, font=bold, fill="white", anchor="lm")
    canvas.alpha_composite(with_alpha(layer, alpha))


def main(sprites_dir: str, output: str) -> None:
    sprites = Path(sprites_dir)
    empty = load_sprite(sprites, "shelf-empty.png")
    highlight = load_sprite(sprites, "shelf-highlight.png")
    holding = load_sprite(sprites, "shelf-one.png")
    file_icon = load_sprite(sprites, "icon-file.png")
    folder_icon = load_sprite(sprites, "icon-folder.png")
    # The picture part of the file icon follows the cursor while dragging, like Windows' drag image.
    ghost = with_alpha(scaled(file_icon.crop((21, 8, 105, 92)), 0.8), 0.75)
    cursor = cursor_image()
    background = backdrop().convert("RGBA")

    file_pos, folder_pos = (90, 140), (584, 140)
    file_center = (file_pos[0] + 63, file_pos[1] + 50)
    folder_center = (folder_pos[0] + 63, folder_pos[1] + 50)
    start, drag_spot = (690, 360), (300, 220)
    # Same placement as ShelfWindow.ShowNearCursor: 24 px right of the cursor, vertically centered.
    shelf_left, shelf_top = drag_spot[0] + 24, drag_spot[1] - empty.height // 2
    shelf_center = (shelf_left + empty.width / 2, shelf_top + empty.height / 2 + 6)
    card = (shelf_left + 12, shelf_top + 12, shelf_left + empty.width - 12, shelf_top + empty.height - 12)

    steps = [  # (start, end, number, text)
        (0.0, 1.75, "1", "Drag a file"),
        (1.75, 2.75, "2", "Shake the mouse"),
        (2.75, 4.35, "3", "Drop it on the shelf"),
        (4.35, 5.9, "4", "Drag it where it belongs"),
        (5.9, DURATION, "done", "Drag · Shake · Drop"),
    ]

    frames = []
    for index in range(int(DURATION * FPS)):
        t = index / FPS
        frame = background.copy()

        # Cursor path and what it carries.
        dragging = False
        if t < 0.85:
            pos = between(t, 0.0, 0.85, start, file_center)
        elif t < 1.0:
            pos = file_center
        elif t < 1.8:
            pos, dragging = between(t, 1.0, 1.8, file_center, drag_spot), True
        elif t < 2.75:
            swing = math.sin(2 * math.pi * 3.4 * (t - 1.8)) * min(1.0, (2.75 - t) * 4, (t - 1.8) * 6)
            pos, dragging = (drag_spot[0] + 44 * swing, drag_spot[1] + 3 * swing), True
        elif t < 3.35:
            pos, dragging = between(t, 2.75, 3.35, drag_spot, shelf_center), True
        elif t < 3.45:
            pos, dragging = shelf_center, True
        elif t < 4.6:
            pos = shelf_center
        elif t < 5.55:
            pos, dragging = between(t, 4.6, 5.55, shelf_center, folder_center), True
        elif t < 5.8:
            pos = folder_center
        else:
            pos = between(t, 5.8, 6.6, folder_center, (700, 330))

        # The file leaves the desktop once it has been moved into the folder.
        frame.alpha_composite(with_alpha(file_icon, 1 - fade(t, 5.55, 5.8)), file_pos)

        over_folder = 4.6 <= t < 5.8 and math.dist(pos, folder_center) < 55
        if over_folder:
            # Explorer-style hover: a soft rounded selection behind the drop target.
            selection = Image.new("RGBA", frame.size, (0, 0, 0, 0))
            ImageDraw.Draw(selection).rounded_rectangle(
                (folder_pos[0] + 6, folder_pos[1] + 4, folder_pos[0] + folder_icon.width - 6,
                 folder_pos[1] + folder_icon.height - 4), radius=8, fill=(255, 255, 255, 34))
            frame.alpha_composite(selection)
        frame.alpha_composite(folder_icon, folder_pos)

        # The shelf: pops in after the shake, highlights under the drag, holds the photo, closes when emptied.
        if t >= 2.5:
            over_card = card[0] <= pos[0] <= card[2] and card[1] <= pos[1] <= card[3]
            if t < 3.45:
                sprite = highlight if dragging and over_card else empty
            else:
                sprite = holding
            grow = ease(fade(t, 2.5, 2.68))
            close = ease(fade(t, 5.55, 5.8))
            alpha = grow * (1 - close)
            if alpha > 0:
                factor = (0.85 + 0.15 * grow) * (1 - 0.06 * close)
                paste_centered(frame, with_alpha(scaled(sprite, factor), alpha),
                               (shelf_left + sprite.width / 2, shelf_top + sprite.height / 2))

        if dragging:
            frame.alpha_composite(ghost, (int(pos[0] - ghost.width / 2 + 10), int(pos[1] - ghost.height / 2 + 14)))
        frame.alpha_composite(cursor, (int(pos[0]), int(pos[1])))

        for t0, t1, number, text in steps:
            if t0 <= t < t1:
                caption(frame, number, text, min(fade(t, t0, t0 + 0.2), 1 - fade(t, t1 - 0.15, t1)))

        frames.append(frame.convert("RGB"))

    # One shared palette for every frame avoids colour flicker between frames. The accent colour and
    # the photo cover few pixels, so enlarged swatches of them keep their colours from being merged away.
    samples = frames[:: max(1, len(frames) // 24)]
    sheet = Image.new("RGB", (WIDTH * (len(samples) + 2), HEIGHT))
    for i, sample in enumerate(samples):
        sheet.paste(sample, (i * WIDTH, 0))
    swatch = Image.composite(Image.new("RGB", (WIDTH, HEIGHT), ACCENT), Image.new("RGB", (WIDTH, HEIGHT), (30, 30, 38)),
                             Image.linear_gradient("L").rotate(90).resize((WIDTH, HEIGHT)))
    sheet.paste(swatch, (len(samples) * WIDTH, 0))
    photo = Image.open(sprites / "icon-file.png").convert("RGB").crop((42, 16, 210, 184)).resize((WIDTH, HEIGHT))
    sheet.paste(photo, ((len(samples) + 1) * WIDTH, 0))
    palette = sheet.quantize(colors=255, method=Image.Quantize.MEDIANCUT)
    indexed = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]

    indexed[0].save(output, save_all=True, append_images=indexed[1:], duration=1000 // FPS, loop=0,
                    optimize=False, disposal=1)
    size_kb = Path(output).stat().st_size / 1024
    print(f"{output}: {len(frames)} frames, {WIDTH}x{HEIGHT}, {size_kb:.0f} KB")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
