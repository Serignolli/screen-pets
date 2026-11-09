# Gera src/recursos/icone.ico a partir do desenho do gato (personagens/gatos.json), num quadradinho creme.
import json, pathlib
from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent
d = json.loads((ROOT / "personagens" / "gatos.json").read_text(encoding="utf-8"))
frame = d["sprites"]["andar"][0]
cores = d["skins"][0]["cores"]

rows = [r for r in frame if r.strip(".")]
top = frame.index(rows[0])
rows = frame[top:top + len(rows)]
w, h = max(len(r) for r in rows), len(rows)

big = 256
img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)
draw.rounded_rectangle([8, 8, big - 8, big - 8], radius=48, fill=(253, 243, 227, 255), outline=(43, 29, 20, 255), width=12)
px = (big - 64) // max(w, h)
ox, oy = (big - w * px) // 2, (big - h * px) // 2 + 8
for y, r in enumerate(rows):
    for x, ch in enumerate(r):
        if ch in ". ":
            continue
        draw.rectangle([ox + x * px, oy + y * px, ox + (x + 1) * px - 1, oy + (y + 1) * px - 1], fill=cores.get(ch, "#888888"))

out = ROOT / "src" / "recursos" / "icone.ico"
img.save(out, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("ok", out)
