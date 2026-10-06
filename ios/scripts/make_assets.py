"""Build the iOS asset catalog using only Python's standard library."""
from pathlib import Path
import json
import math
import shutil
import struct
import zlib

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "Resources" / "Assets.xcassets"


def metadata(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as output:
        output.write(json.dumps(value, indent=2) + "\n")


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


def app_icon():
    size = 1024
    pixels = bytearray()
    for y in range(size):
        pixels.append(0)
        for x in range(size):
            dx, dy = x - 511.5, y - 511.5
            radius = math.hypot(dx, dy)
            angle = (math.atan2(dy, dx) + math.pi / 2) % (2 * math.pi)
            color = (15, 28, 36)
            for r, width, fraction, foreground in [
                (350, 43, 0.78, (58, 211, 192)),
                (264, 36, 0.58, (174, 135, 246)),
            ]:
                edge = max(0, min(1, width / 2 + 0.5 - abs(radius - r)))
                if edge:
                    ink = foreground if angle < fraction * 2 * math.pi else (42, 53, 66)
                    color = tuple(round(a * (1 - edge) + b * edge) for a, b in zip(color, ink))
            # Three ascending bars identify the app without borrowing a provider mark.
            for cx, top in [(440, 500), (512, 440), (584, 380)]:
                if abs(x - cx) <= 21 and top <= y <= 610:
                    color = (231, 240, 244)
            pixels.extend(color)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(pixels, 9)) + chunk(b"IEND", b""))


def main():
    info = {"author": "xcode", "version": 1}
    metadata(CATALOG / "Contents.json", {"info": info})
    for provider in ["codex", "claude"]:
        folder = CATALOG / f"{provider}.imageset"
        metadata(folder / "Contents.json", {"images": [{"filename": f"{provider}.png", "idiom": "universal"}], "info": info})
        source = ROOT.parent / "android/app/src/main/res/drawable-nodpi" / f"provider_{provider}.png"
        shutil.copyfile(source, folder / f"{provider}.png")
    folder = CATALOG / "AppIcon.appiconset"
    metadata(folder / "Contents.json", {"images": [{"filename": "AppIcon.png", "idiom": "universal", "platform": "ios", "size": "1024x1024"}], "info": info})
    (folder / "AppIcon.png").write_bytes(app_icon())


if __name__ == "__main__":
    main()
