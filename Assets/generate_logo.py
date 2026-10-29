"""Regenerate Mailcario's raster assets from the same simple vector geometry.

Requires Pillow only while generating assets; the application has no Python dependency.
"""

from pathlib import Path

from PIL import Image, ImageDraw


HERE = Path(__file__).resolve().parent
TEAL = (17, 116, 123, 255)
WHITE = (255, 255, 255, 255)
DESIGN_SIZE = 256


def render(size: int) -> Image.Image:
    # Supersample the curves so even the 16 px Windows icon stays smooth.
    scale = max(4, 2048 // size)
    canvas = Image.new("RGBA", (size * scale, size * scale), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)
    unit = size * scale / DESIGN_SIZE

    def box(coords: tuple[float, float, float, float]) -> tuple[int, int, int, int]:
        return tuple(round(value * unit) for value in coords)

    draw.rounded_rectangle(box((7, 7, 249, 249)), radius=round(61 * unit), fill=TEAL)

    points = [(64, 176), (64, 84), (128, 138), (192, 84), (192, 176)]
    scaled = [(round(x * unit), round(y * unit)) for x, y in points]
    width = round(26 * unit)
    radius = width / 2
    draw.line(scaled, fill=WHITE, width=width, joint="curve")
    for x, y in scaled:
        draw.ellipse((round(x - radius), round(y - radius),
                      round(x + radius), round(y + radius)), fill=WHITE)

    return canvas.resize((size, size), Image.Resampling.LANCZOS)


if __name__ == "__main__":
    render(512).save(HERE / "Mailcario-512.png", optimize=True)
    render(1024).save(HERE / "Mailcario-1024.png", optimize=True)
    render(256).save(HERE / "Mailcario.ico", format="ICO",
                     sizes=[(16, 16), (24, 24), (32, 32), (48, 48),
                            (64, 64), (128, 128), (256, 256)])
