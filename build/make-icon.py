"""Writes the SpectraCollector icon: an Ink rounded square with six spectrum-coloured bars.

Run on the host with ``python3 -I build/make-icon.py``. The output,
``src/SpectraCollector.App/Assets/SpectraCollector.ico``, holds 16, 32, 48 and 256 px
images, each stored as a PNG. Bars are snapped to whole pixels at every size and the
rounded square is anti-aliased with 4x4 supersampling.
"""

import struct
import zlib
from collections.abc import Sequence
from dataclasses import dataclass
from pathlib import Path
from typing import Final

SIZES: Final[tuple[int, ...]] = (16, 32, 48, 256)
SUPERSAMPLE: Final[int] = 4
CORNER_RADIUS: Final[float] = 0.18
INSET: Final[float] = 0.2
MIN_SIDE_INSET: Final[float] = 0.15
BAR_WIDTH: Final[float] = 0.064
BAR_GAP: Final[float] = 0.043
BAR_HEIGHTS: Final[tuple[float, ...]] = (0.45, 0.75, 0.55, 0.90, 0.60, 0.35)
OUTPUT: Final[Path] = (
    Path(__file__).resolve().parent.parent / "src" / "SpectraCollector.App" / "Assets" / "SpectraCollector.ico"
)


@dataclass(frozen=True, slots=True)
class Rgba:
    r: int
    g: int
    b: int
    a: int

    @staticmethod
    def opaque(hex_colour: str) -> "Rgba":
        value = int(hex_colour.removeprefix("#"), 16)
        return Rgba((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF, 0xFF)


TRANSPARENT: Final[Rgba] = Rgba(0, 0, 0, 0)
INK: Final[Rgba] = Rgba.opaque("#1C2630")
BAND: Final[tuple[Rgba, ...]] = tuple(
    Rgba.opaque(c) for c in ("#6A3FB5", "#2D6CDF", "#1E9E6A", "#E0B000", "#E0661B", "#DA291C")
)


@dataclass(frozen=True, slots=True)
class Bar:
    left: int
    right: int
    top: int
    bottom: int
    colour: Rgba

    def contains(self, x: float, y: float) -> bool:
        return self.left <= x < self.right and self.top <= y < self.bottom


@dataclass(frozen=True, slots=True)
class BarSpacing:
    width: int
    gap: int

    @property
    def span(self) -> int:
        return len(BAND) * self.width + (len(BAND) - 1) * self.gap


@dataclass(frozen=True, slots=True)
class IconImage:
    size: int
    png: bytes


def round_half_up(value: float) -> int:
    return int(value + 0.5)


def bar_spacing(size: int) -> BarSpacing:
    spacing = BarSpacing(max(1, round_half_up(size * BAR_WIDTH)), max(1, round_half_up(size * BAR_GAP)))
    wider = BarSpacing(spacing.width, spacing.gap + 1)
    centred = (size - spacing.span) % 2 == 0
    if not centred and wider.span <= size * (1 - 2 * MIN_SIDE_INSET):
        return wider
    return spacing


def bars(size: int) -> tuple[Bar, ...]:
    spacing = bar_spacing(size)
    left = (size - spacing.span) // 2
    inset = round_half_up(size * INSET)
    bottom = size - inset
    inner_height = bottom - inset
    return tuple(
        Bar(
            left=left + i * (spacing.width + spacing.gap),
            right=left + i * (spacing.width + spacing.gap) + spacing.width,
            top=bottom - round_half_up(height * inner_height),
            bottom=bottom,
            colour=colour,
        )
        for i, (height, colour) in enumerate(zip(BAR_HEIGHTS, BAND, strict=True))
    )


def in_rounded_square(x: float, y: float, size: int) -> bool:
    radius = size * CORNER_RADIUS
    nearest_x = min(max(x, radius), size - radius)
    nearest_y = min(max(y, radius), size - radius)
    return (x - nearest_x) ** 2 + (y - nearest_y) ** 2 <= radius**2


def colour_at(x: float, y: float, size: int, icon_bars: Sequence[Bar]) -> Rgba:
    if not in_rounded_square(x, y, size):
        return TRANSPARENT
    for bar in icon_bars:
        if bar.contains(x, y):
            return bar.colour
    return INK


def pixel(px: int, py: int, size: int, icon_bars: Sequence[Bar]) -> bytes:
    step = 1 / SUPERSAMPLE
    samples = [
        colour_at(px + (i + 0.5) * step, py + (j + 0.5) * step, size, icon_bars)
        for j in range(SUPERSAMPLE)
        for i in range(SUPERSAMPLE)
    ]
    alpha = sum(s.a for s in samples)
    if alpha == 0:
        return bytes(4)

    def channel(pick: int) -> int:
        weighted = sum((s.r, s.g, s.b)[pick] * s.a for s in samples)
        return round_half_up(weighted / alpha)

    return bytes((channel(0), channel(1), channel(2), round_half_up(alpha / len(samples))))


def render(size: int) -> list[bytes]:
    icon_bars = bars(size)
    return [b"".join(pixel(px, py, size, icon_bars) for px in range(size)) for py in range(size)]


def png_chunk(tag: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data))


def encode_png(size: int, rows: Sequence[bytes]) -> bytes:
    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    raw = b"".join(b"\x00" + row for row in rows)
    return (
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", header)
        + png_chunk(b"IDAT", zlib.compress(raw, 9))
        + png_chunk(b"IEND", b"")
    )


def encode_ico(images: Sequence[IconImage]) -> bytes:
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = len(header) + 16 * len(images)
    entries: list[bytes] = []
    for image in images:
        dimension = 0 if image.size >= 256 else image.size
        entries.append(struct.pack("<BBBBHHII", dimension, dimension, 0, 0, 1, 32, len(image.png), offset))
        offset += len(image.png)
    return header + b"".join(entries) + b"".join(image.png for image in images)


def main() -> None:
    images = [IconImage(size, encode_png(size, render(size))) for size in SIZES]
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_bytes(encode_ico(images))
    print(f"{OUTPUT}: {', '.join(str(s) for s in SIZES)} px, {OUTPUT.stat().st_size} bytes")


if __name__ == "__main__":
    main()
