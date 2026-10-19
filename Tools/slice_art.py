"""Режет листы с артом Casiwar на отдельные спрайты + пишет .meta (Sprite, пивот).

python slice_art.py <project_root>
"""
import math
import os
import sys
import uuid
from collections import deque

from PIL import Image, ImageChops, ImageFilter

root = sys.argv[1]
units_out = os.path.join(root, "Assets", "Art", "Resources", "Units")
icons_out = os.path.join(root, "Assets", "Art", "Resources", "Icons")
os.makedirs(units_out, exist_ok=True)
os.makedirs(icons_out, exist_ok=True)

META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: {alignment}
  spritePivot: {{x: 0.5, y: {pivot_y}}}
  spritePixelsToUnits: {ppu}
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: 5e97eb03825dee720800000000000000
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData: casiwar-art-v1
  assetBundleName:
  assetBundleVariant:
"""


def save(img, folder, name, bottom_pivot, ppu=100):
    path = os.path.join(folder, name + ".png")
    img.save(path)
    meta = path + ".meta"
    guid = None
    if os.path.exists(meta):  # гуид сохраняем — ссылки в сцене/ассетах не слетят
        for line in open(meta, encoding="utf-8"):
            if line.startswith("guid:"):
                guid = line.split(":", 1)[1].strip()
    guid = guid or uuid.uuid4().hex
    with open(meta, "w", encoding="utf-8", newline="\n") as f:
        f.write(META.format(guid=guid, alignment=7 if bottom_pivot else 0, pivot_y=0 if bottom_pivot else 0.5, ppu=ppu))


def write_folder_meta(path):
    meta = path + ".meta"
    if os.path.exists(meta):
        return
    with open(meta, "w", encoding="utf-8", newline="\n") as f:
        f.write("fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
                "  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % uuid.uuid4().hex)


for folder in [os.path.join(root, "Assets", "Art"), os.path.join(root, "Assets", "Art", "Resources"), units_out, icons_out]:
    write_folder_meta(folder)

# ---------------------------------------------------------------- юниты (бой, скамейка)
TH = 40


def runs_of(flags, min_gap, min_len):
    runs, start = [], None
    for i, c in enumerate(flags + [False]):
        if c and start is None:
            start = i
        if not c and start is not None:
            runs.append([start, i])
            start = None
    merged = []
    for r in runs:
        if merged and r[0] - merged[-1][1] < min_gap:
            merged[-1][1] = r[1]
        else:
            merged.append(r)
    return [r for r in merged if r[1] - r[0] > min_len]


def sheet_rows(sheet):
    A = sheet.split()[3].load()
    W, H = sheet.size
    return runs_of([any(A[x, y] > TH for x in range(W)) for y in range(H)], 6, 20)


def figures_in_band(sheet, y0, y1, min_area=800):
    """Фигурки ряда: связные куски непрозрачного (8 соседей); мелкие отколотые (искры, кончики посохов)
    приклеиваются к ближайшей крупной по X. Фигурки могут заходить друг на друга по X — режем по пикселям."""
    W = sheet.size[0]
    A = sheet.split()[3].load()
    h = y1 - y0
    label = [[0] * W for _ in range(h)]
    comps = []
    for sy in range(h):
        for sx in range(W):
            if label[sy][sx] or A[sx, y0 + sy] <= TH:
                continue
            n = len(comps) + 1
            label[sy][sx] = n
            stack, pix = [(sx, sy)], []
            while stack:
                cx, cy = stack.pop()
                pix.append((cx, cy))
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        nx, ny = cx + dx, cy + dy
                        if 0 <= nx < W and 0 <= ny < h and not label[ny][nx] and A[nx, y0 + ny] > TH:
                            label[ny][nx] = n
                            stack.append((nx, ny))
            comps.append(pix)
    big = sorted((c for c in comps if len(c) >= min_area), key=lambda c: min(p[0] for p in c))
    spans = [(min(p[0] for p in c), max(p[0] for p in c)) for c in big]
    for c in comps:
        if len(c) >= min_area:
            continue
        mx = sum(p[0] for p in c) / len(c)
        best = min(range(len(big)), key=lambda i: max(spans[i][0] - mx, mx - spans[i][1], 0))
        big[best].extend(c)
    return big


def slice_units(sheet, rows, names_by_row, ppu):
    """names_by_row: имена фигурок слева направо (None — фигурку пропустить)."""
    W, H = sheet.size
    assert len(rows) == len(names_by_row), len(rows)
    for (y0, y1), names in zip(rows, names_by_row):
        if all(name is None for name in names):
            continue
        y0 = max(0, y0 - 6)
        y1 = min(H, y1 + 6)
        figures = figures_in_band(sheet, y0, y1)
        assert len(figures) == len(names), (y0, len(figures))
        band = sheet.crop((0, y0, W, y1))
        band_alpha = band.split()[3].point(lambda v: 0 if v < 10 else v)
        for pixels, name in zip(figures, names):
            if name is None:
                continue
            # только пиксели этой фигурки (+2 px мягкого края), без кусков соседей
            mask = Image.new("L", band.size, 0)
            mp = mask.load()
            for x, y in pixels:
                mp[x, y] = 255
            mask = mask.filter(ImageFilter.MaxFilter(5))
            crop = band.copy()
            crop.putalpha(ImageChops.multiply(band_alpha, mask))
            xs_all = [p[0] for p in pixels]
            ys_all = [p[1] for p in pixels]
            fig = crop.crop((min(xs_all), min(ys_all), max(xs_all) + 1, max(ys_all) + 1))
            fw, fh = fig.size
            fa = fig.split()[3].load()
            # центр тела — медиана по X непрозрачных пикселей нижней четверти (ноги), а не рамки с оружием
            xs = sorted(x for y in range(int(fh * 0.72), fh) for x in range(fw) if fa[x, y] > 128)
            cx = xs[len(xs) // 2] if xs else fw // 2
            half = max(cx, fw - cx) + 4
            out = Image.new("RGBA", (half * 2, fh + 4), (0, 0, 0, 0))
            out.paste(fig, (half - cx, 2))
            save(out, units_out, name, bottom_pivot=True, ppu=ppu)


# Основной лист: всё, кроме воинов (их берём из листа Gemini)
proper = Image.open(os.path.join(root, "Assets", "UnitAssetsProper.png")).convert("RGBA")
proper_rows = sheet_rows(proper)
slice_units(proper, proper_rows, [
    ["militia_1", "militia_2", "militia_3", None, None, None, "archer_1", "archer_2", "archer_3"],
    ["crossbow_1", "crossbow_2", "crossbow_3", "priest_1", "priest_2", "priest_3", "monk_1", "monk_2", "monk_3"],
    ["mage_1", "mage_2", "mage_3", "bomber_1", "bomber_2", "bomber_3", "hero_1", "hero_2", "hero_3"],
    ["knight", "ranger", "sorcerer", "rogue", "priestess", "darkmage", "necromancer"],
], 100)

# Лист Gemini: воины (легионеры с круглым щитом) и нижний ряд — облики богатыря.
# Он мельче — px/ед. подбираем так, чтобы фигурки в мире были того же роста, что и с основного листа.
gemini = Image.open(os.path.join(root, "Assets", "UnitsAssetsGemini.png")).convert("RGBA")
gemini_rows = sheet_rows(gemini)
row_height = lambda rows: sum(r[1] - r[0] for r in rows) / len(rows)
gemini_ppu = round(100 * row_height(gemini_rows) / row_height(proper_rows))
slice_units(gemini, gemini_rows, [
    [None, None, None, "warrior_1", "warrior_2", "warrior_3", None, None, None],
    [None] * 9,
    [None] * 9,
    ["hero_knight", "hero_redknight", "hero_monk", "hero_priest", "hero_elf", "hero_dwarf",
     "hero_priestess", "hero_paladin", "hero_assassin", "hero_veteran"],
], gemini_ppu)
print("gemini ppu", gemini_ppu)

# ---------------------------------------------------------------- иконки (символы поля)
icons = Image.open(os.path.join(root, "Assets", "UnitsIcons.png")).convert("RGBA")
IW, IH = icons.size
P = icons.load()
BG = (80, 80, 80)


def bg_like(p):
    # фон — ровный серый ~80; внутри лука он чуть желтит (b до 70), а дерево лука уже бурое
    return abs(p[0] - 80) <= 8 and abs(p[1] - 80) <= 8 and abs(p[2] - 80) <= 11 and max(p[:3]) - min(p[:3]) <= 8


# фон — заливкой от краёв (допуск 14); закрытые «дыры» фона (между луком и тетивой) — по точному цвету
transparent = [[False] * IH for _ in range(IW)]
queue = deque()
for x in range(IW):
    for y in (0, IH - 1):
        queue.append((x, y))
for y in range(IH):
    for x in (0, IW - 1):
        queue.append((x, y))
while queue:
    x, y = queue.popleft()
    if transparent[x][y] or not bg_like(P[x, y]):
        continue
    transparent[x][y] = True
    for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
        if 0 <= nx < IW and 0 <= ny < IH and not transparent[nx][ny]:
            queue.append((nx, ny))

seen = [[False] * IH for _ in range(IW)]
for sx in range(IW):
    for sy in range(IH):
        if transparent[sx][sy] or seen[sx][sy] or not bg_like(P[sx, sy]):
            continue
        comp, queue = [], deque([(sx, sy)])
        seen[sx][sy] = True
        while queue:
            x, y = queue.popleft()
            comp.append((x, y))
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if 0 <= nx < IW and 0 <= ny < IH and not seen[nx][ny] and not transparent[nx][ny] and bg_like(P[nx, ny]):
                    seen[nx][ny] = True
                    queue.append((nx, ny))
        if len(comp) >= 40:
            for x, y in comp:
                transparent[x][y] = True

for _ in range(2):  # одиночные серые пиксели по краю фигур
    for x in range(1, IW - 1):
        for y in range(1, IH - 1):
            if not transparent[x][y] and bg_like(P[x, y]) and sum(transparent[nx][ny] for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1))) >= 2:
                transparent[x][y] = True
for x in range(IW):
    for y in range(IH):
        if transparent[x][y]:
            r, g, b, _ = P[x, y]
            P[x, y] = (r, g, b, 0)

ia = icons.split()[3].load()
cols = [any(ia[x, y] > 0 for y in range(IH)) for x in range(IW)]
runs, start = [], None
for x, c in enumerate(cols + [False]):
    if c and start is None:
        start = x
    if not c and start is not None:
        runs.append([start, x])
        start = None
merged = []
for r in runs:
    if merged and r[0] - merged[-1][1] < 10:
        merged[-1][1] = r[1]
    else:
        merged.append(r)
merged = [r for r in merged if r[1] - r[0] > 12]
icon_names = ["militia", "warrior", "archer", "priest", "monk", "mage", "bomber", "hero"]
assert len(merged) == len(icon_names), len(merged)
for (x0, x1), name in zip(merged, icon_names):
    crop = icons.crop((x0, 0, x1, IH))
    bx0, by0, bx1, by1 = crop.split()[3].getbbox()
    fig = crop.crop((bx0, by0, bx1, by1))
    side = max(fig.size) + 6
    out = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    out.paste(fig, ((side - fig.size[0]) // 2, (side - fig.size[1]) // 2))
    save(out, icons_out, name, bottom_pivot=False)

# ---------------------------------------------------------------- местность (клетки карты мира)
# Атлас 4×2 по 256 px: внутри 224 px рисунка + 16 px продлённого края (чтобы мип-уровни не тянули соседей).
# Порядок слотов — как в TerrainLayer.Tile (поменяете — поменяйте и там).
# Клетки суши — как нарисованы (без поворотов). Вода — сплошная бесшовная текстура из середины пруда
# (озеро — синяя как есть, река — бирюзовая). Песок — бесшовная текстура с пляжа: из неё прямоугольные полоски берега.
TILE, PAD = 224, 16
SLOT = TILE + PAD * 2
terrain_out = os.path.join(root, "Assets", "Art", "Resources", "Terrain")
os.makedirs(terrain_out, exist_ok=True)
write_folder_meta(terrain_out)

terrain = Image.open(os.path.join(root, "Assets", "TerrainAssets.png")).convert("RGBA")
TA = terrain.split()[3].load()


def opaque_square(box):
    """Внутренний квадрат клетки без рваного прозрачного края."""
    x0, y0, x1, y1 = box
    def full_row(y, a, b):
        return all(TA[x, y] > 250 for x in range(a, b))
    def full_col(x, a, b):
        return all(TA[x, y] > 250 for y in range(a, b))
    changed = True
    while changed:
        changed = False
        if not full_row(y0, x0, x1): y0 += 1; changed = True
        if not full_row(y1 - 1, x0, x1): y1 -= 1; changed = True
        if not full_col(x0, y0, y1): x0 += 1; changed = True
        if not full_col(x1 - 1, y0, y1): x1 -= 1; changed = True
    side = min(x1 - x0, y1 - y0)
    cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    return (cx - side // 2, cy - side // 2, cx - side // 2 + side, cy - side // 2 + side)


def seamless_tile(box, copies_x=2, copies_y=2, hue_shift=0, brighten=1.0):
    """Кусок текстуры → бесшовная клетка: сдвиг на полкуска и плавное смешивание швов, потом штампы copies_x × copies_y."""
    patch = terrain.crop(box).convert("RGB")
    w, h = patch.size
    shifted = ImageChops.offset(patch, w // 2, h // 2)
    mask = Image.new("L", (w, h))
    mp = mask.load()
    for y in range(h):
        for x in range(w):
            fx = math.sin(math.pi * (x + 0.5) / w)
            fy = math.sin(math.pi * (y + 0.5) / h)
            mp[x, y] = int(255 * min(1.0, (fx * fy) * 1.6))
    patch = Image.composite(patch, shifted, mask)
    if hue_shift or brighten != 1.0:
        hsv = patch.convert("HSV")
        hch, sch, vch = hsv.split()
        hch = hch.point(lambda v: (v + hue_shift) % 256)
        vch = vch.point(lambda v: min(255, int(v * brighten)))
        patch = Image.merge("HSV", (hch, sch, vch)).convert("RGB")
    big = Image.new("RGB", (w * 3, h * 3))
    for i in range(3):
        for j in range(3):
            big.paste(patch, (i * w, j * h))
    step_x, step_y = TILE // copies_x, TILE // copies_y
    big = big.resize((step_x * 3, step_y * 3), Image.LANCZOS)
    return big.crop((step_x, step_y, step_x + TILE, step_y + TILE))


WATER_BOX = (1254, 214, 1358, 318)  # самый большой кусок чистой воды в середине пруда
tiles = [
    ("grass", terrain.crop(opaque_square((415, 84, 715, 389)))),
    ("forest", terrain.crop(opaque_square((57, 79, 362, 390)))),
    ("rocks", terrain.crop(opaque_square((59, 426, 362, 718)))),
    ("mountain", terrain.crop(opaque_square((411, 418, 710, 717)))),
    ("spring", terrain.crop(opaque_square((759, 432, 1033, 721)))),
    ("water_lake", seamless_tile(WATER_BOX)),
    ("water_river", seamless_tile(WATER_BOX, hue_shift=-11, brighten=0.97)),  # бирюзовее
    ("sand", seamless_tile((1195, 574, 1335, 644), 1, 2)),  # песок пляжа без камней
]

atlas = Image.new("RGBA", (SLOT * 4, SLOT * 2), (0, 0, 0, 0))
for i, (name, img) in enumerate(tiles):
    sx, sy = (i % 4) * SLOT, (i // 4) * SLOT
    img = img.convert("RGB").resize((TILE, TILE), Image.LANCZOS)
    padded = Image.new("RGB", (SLOT, SLOT))
    padded.paste(img, (PAD, PAD))
    padded.paste(img.crop((0, 0, TILE, 1)).resize((TILE, PAD)), (PAD, 0))
    padded.paste(img.crop((0, TILE - 1, TILE, TILE)).resize((TILE, PAD)), (PAD, PAD + TILE))
    padded.paste(padded.crop((PAD, 0, PAD + 1, SLOT)).resize((PAD, SLOT)), (0, 0))
    padded.paste(padded.crop((PAD + TILE - 1, 0, PAD + TILE, SLOT)).resize((PAD, SLOT)), (PAD + TILE, 0))
    atlas.paste(padded.convert("RGBA"), (sx, sy))
atlas_path = os.path.join(terrain_out, "terrain_atlas.png")
atlas.save(atlas_path)
meta = atlas_path + ".meta"
guid = None
if os.path.exists(meta):
    for line in open(meta, encoding="utf-8"):
        if line.startswith("guid:"):
            guid = line.split(":", 1)[1].strip()
with open(meta, "w", encoding="utf-8", newline="\n") as f:
    # обычная текстура (не спрайт): её целиком берёт меш карты
    f.write(META.format(guid=guid or uuid.uuid4().hex, alignment=0, pivot_y=0.5, ppu=100)
            .replace("  textureType: 8\n", "  textureType: 0\n")
            .replace("  spriteMode: 1\n", "  spriteMode: 0\n"))
print("terrain atlas", atlas.size)

# ---------------------------------------------------------------- снаряжение (трофеи, значки на скамейке)
# GearAssets.png: сетка 6×3 на сером фоне (108), под каждым рядом — полоса с подписью.
# Ряды: воины, лучники, жрецы; колонки: оружие I–III, броня I–III → Gear/<класс>_<weapon|armor>_<1..3>.
gear_out = os.path.join(root, "Assets", "Art", "Resources", "Gear")
os.makedirs(gear_out, exist_ok=True)
write_folder_meta(gear_out)

gear = Image.open(os.path.join(root, "Assets", "GearAssets.png")).convert("RGB")
GW, GH = gear.size
GEAR_BG = 108.0
gear_rows = [(2, 169), (194, 359), (386, 547)]  # над полосами подписей
gear_classes = ["warrior", "archer", "priest"]


def unblend(c):
    """«Цвет → прозрачность» относительно серого фона: свечение становится полупрозрачным цветом."""
    alpha = 0.0
    for v in c:
        alpha = max(alpha, (v - GEAR_BG) / (255.0 - GEAR_BG) if v > GEAR_BG else (GEAR_BG - v) / GEAR_BG)
    if alpha <= 0.02:
        return (0, 0, 0, 0)
    rgb = tuple(int(max(0, min(255, (v - (1 - alpha) * GEAR_BG) / alpha))) for v in c)
    return rgb + (int(alpha * 255),)


for row, (y0, y1) in enumerate(gear_rows):
    for col in range(6):
        x0 = round(col * GW / 6) + 4
        x1 = round((col + 1) * GW / 6) - 4
        cell = gear.crop((x0, y0, x1, y1))
        cw, ch = cell.size
        cp = cell.load()
        # снаружи тёмного контура — фон и свечение: заливка от краёв по всему, что не контур
        outside = [[False] * ch for _ in range(cw)]
        queue = deque((x, y) for x in range(cw) for y in (0, ch - 1))
        queue.extend((x, y) for y in range(ch) for x in (0, cw - 1))
        while queue:
            x, y = queue.popleft()
            if outside[x][y]:
                continue
            c = cp[x, y]
            if max(c) < 70:  # контур
                continue
            outside[x][y] = True
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if 0 <= nx < cw and 0 <= ny < ch and not outside[nx][ny]:
                    queue.append((nx, ny))
        out = Image.new("RGBA", (cw, ch))
        op = out.load()
        for x in range(cw):
            for y in range(ch):
                op[x, y] = unblend(cp[x, y]) if outside[x][y] else cp[x, y] + (255,)
        bbox = out.split()[3].point(lambda v: 255 if v > 24 else 0).getbbox()
        fig = out.crop(bbox)
        side = max(fig.size) + 8
        canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        canvas.paste(fig, ((side - fig.size[0]) // 2, (side - fig.size[1]) // 2))
        slot = "weapon" if col < 3 else "armor"
        save(canvas, gear_out, f"{gear_classes[row]}_{slot}_{col % 3 + 1}", bottom_pivot=False)
print("gear icons", 18)

# ---------------------------------------------------------------- здания (карта мира)
# HouseAssets.png: 7×3 зданий на нарисованной «шахматке» (серые 102/153) с подписями снизу.
# Порядок — как в промпте; имена файлов — как BuildingType в CityManager → Buildings/<Type>.
buildings_out = os.path.join(root, "Assets", "Art", "Resources", "Buildings")
os.makedirs(buildings_out, exist_ok=True)
write_folder_meta(buildings_out)

houses = Image.open(os.path.join(root, "Assets", "HouseAssets.png")).convert("RGB")
HW, HH = houses.size
house_rows = [(0, 152), (176, 336), (360, 531)]  # над подписями
house_names = [
    ["TownHall", "House", "Bazaar", "Granary", "Workshop", "School", "Barracks"],
    ["ArcheryRange", "Sanctuary", "Smelter", "Sawmill", "Temple", "Monastery", "MageTower"],
    ["Stonehenge", "Pyramids", "GreatWall", "Tiltyard", "Colossus", "GreatLibrary", "TerracottaArmy"],
]


def checker_like(c):
    """Клетки «шахматки» и их размытые стыки — ровный серый; тёмный контур здания (< 50) заливку останавливает."""
    return max(c) - min(c) <= 8 and 50 <= c[0] <= 170


for row, (y0, y1) in enumerate(house_rows):
    for col, name in enumerate(house_names[row]):
        x0 = int(col * HW / 7) + 1
        x1 = int((col + 1) * HW / 7) - 1
        cell = houses.crop((x0, y0, x1, y1))
        cw, ch = cell.size
        cp = cell.load()
        outside = [[False] * ch for _ in range(cw)]
        queue = deque((x, y) for x in range(cw) for y in (0, ch - 1))
        queue.extend((x, y) for y in range(ch) for x in (0, cw - 1))
        while queue:
            x, y = queue.popleft()
            if outside[x][y] or not checker_like(cp[x, y]):
                continue
            outside[x][y] = True
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if 0 <= nx < cw and 0 <= ny < ch and not outside[nx][ny]:
                    queue.append((nx, ny))
        # Замкнутые куски «шахматки» (внутри круга Стоунхенджа и т. п.): большие пятна ровно её двух серых
        # у фигур шахматка бывает чуть подкрашена (на 10–15 в красный) — допуск шире, но только для замкнутых пятен
        def soft_checker(c):
            return max(c) - min(c) <= 18 and 50 <= sum(c) / 3 <= 175
        def checker_tone(c):
            v = sum(c) / 3
            return max(c) - min(c) <= 18 and (abs(v - 103) <= 12 or abs(v - 153) <= 12)
        seen = [[False] * ch for _ in range(cw)]
        for sx in range(cw):
            for sy in range(ch):
                if outside[sx][sy] or seen[sx][sy] or not soft_checker(cp[sx, sy]):
                    continue
                comp, stack = [], [(sx, sy)]
                seen[sx][sy] = True
                while stack:
                    x, y = stack.pop()
                    comp.append((x, y))
                    for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                        if 0 <= nx < cw and 0 <= ny < ch and not seen[nx][ny] and not outside[nx][ny] and soft_checker(cp[nx, ny]):
                            seen[nx][ny] = True
                            stack.append((nx, ny))
                dark = sum(1 for x, y in comp if checker_tone(cp[x, y]) and sum(cp[x, y]) < 384)
                light = sum(1 for x, y in comp if checker_tone(cp[x, y]) and sum(cp[x, y]) >= 384)
                if len(comp) >= 60 and dark >= len(comp) * 0.2 and light >= len(comp) * 0.2:
                    for x, y in comp:
                        outside[x][y] = True
        out = Image.new("RGBA", (cw, ch))
        op = out.load()
        for x in range(cw):
            for y in range(ch):
                op[x, y] = (0, 0, 0, 0) if outside[x][y] else cp[x, y] + (255,)
        fig = out.crop(out.split()[3].getbbox())
        side = max(fig.size) + 6
        canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        # здание стоит на нижнем краю квадрата (как на клетке карты)
        canvas.paste(fig, ((side - fig.size[0]) // 2, side - fig.size[1] - 3))
        save(canvas, buildings_out, name, bottom_pivot=False)
print("buildings", sum(len(r) for r in house_names))

# ---------------------------------------------------------------- символы, значки интерфейса, объекты карты
# GeneredIcons.png: 6×4 на нарисованной светлой «шахматке» (белый ~254 / серый ~228), подписи под рядами.
# Ряды: ресурсы; трофеи и поле боя; бой и интерфейс; карта мира. Значки → Symbols/<имя>, карта → Map/<имя>,
# горожанин → Icons/civilian (раньше их рисовал Tools/draw_map_art.py).
# Фон — нейтральный, поэтому прозрачность свечения считается по насыщенности цвета: она не зависит от того,
# белая под пикселем клетка или серая (сетка «шахматки» в картинке неровная — по ней не угадать).
symbols_out = os.path.join(root, "Assets", "Art", "Resources", "Symbols")
map_out = os.path.join(root, "Assets", "Art", "Resources", "Map")
for folder in (symbols_out, map_out):
    os.makedirs(folder, exist_ok=True)
    write_folder_meta(folder)

gen = Image.open(os.path.join(root, "Assets", "GeneredIcons.png")).convert("RGB")
GNW, GNH = gen.size
GP = gen.load()
gen_cols = [(0, 260), (260, 514), (514, 758), (758, 1033), (1033, 1280), (1280, GNW)]
gen_rows = [(0, 230), (268, 484), (520, 735), (768, 966)]  # без подписей
gen_names = [
    [("Symbols", "gold"), ("Symbols", "production"), ("Symbols", "food"), ("Symbols", "knowledge"), ("Symbols", "population"), ("Symbols", "star")],
    [("Symbols", "loot_weapons"), ("Symbols", "loot_armor"), ("Symbols", "attack"), ("Symbols", "march"), ("Symbols", "banner"), ("Symbols", "multiplier")],
    [("Symbols", "candle"), ("Symbols", "fire"), ("Symbols", "health"), ("Symbols", "turns"), ("Symbols", "death"), ("Symbols", "walls")],
    [("Map", "barbarian_camp"), ("Map", "trade_city"), ("Map", "hostile_city"), ("Map", "caravan"), ("Icons", "civilian"), ("Symbols", "spin")],
]
gen_bottom = {"barbarian_camp", "trade_city", "hostile_city"}  # стоят на клетке карты, как здания
gen_tight = {"caravan"}  # по контуру, не квадратом: метка каравана на карте — широкая
# Белая вспышка на скрещенных мечах перекрывает их контур: вокруг центра заливка не идёт (мечи не «протекают»)
gen_keep = {"attack": ((630, 360), 21)}
GEN_BG = 241.0  # средний цвет «шахматки»


def chroma(c):
    return max(c) - min(c)


def gen_background(c):
    """Чистая «шахматка» (и белая вспышка — её от белой клетки не отличить)."""
    return chroma(c) <= 10 and min(c) >= 200


def gen_floodable(c):
    """Что вокруг рисунка: фон, тёплое свечение и сглаженный край контура (голубая сталь клинков — нет)."""
    if min(c) < 140:
        return False
    return chroma(c) <= 10 or (c[0] >= c[2] + 8 and c[0] >= c[1] - 6)


def gen_alpha(c, glow_chroma):
    """Свечение — прозрачность по насыщенности, цвет — сам оттенок (самый яркий канал 255); тёмный край — по яркости."""
    lum_v = sum(c) / 3.0
    a_glow = min(1.0, chroma(c) / glow_chroma)
    a_dark = max(0.0, (GEN_BG - 6 - lum_v) / (GEN_BG - 6))
    if max(a_glow, a_dark) <= 0.06:
        return (0, 0, 0, 0)
    if a_dark > a_glow:
        a = a_dark
        rgb = tuple(int(max(0, min(255, (v - (1 - a) * GEN_BG) / a))) for v in c)
    else:
        a = a_glow
        low = min(c)
        rgb = tuple(int(max(0, min(255, (v - low) / a + 255 - glow_chroma))) for v in c)
    return rgb + (int(a * 255),)


for row, (y0, y1) in enumerate(gen_rows):
    for col, (x0, x1) in enumerate(gen_cols):
        folder_name, name = gen_names[row][col]
        cw, ch = x1 - x0, y1 - y0
        keep = gen_keep.get(name)

        def blocked(x, y):
            return keep is not None and (x0 + x - keep[0][0]) ** 2 + (y0 + y - keep[0][1]) ** 2 <= keep[1] ** 2

        outside = [[False] * ch for _ in range(cw)]
        queue = deque((x, y) for x in range(cw) for y in (0, ch - 1))
        queue.extend((x, y) for y in range(ch) for x in (0, cw - 1))
        while queue:
            x, y = queue.popleft()
            if outside[x][y] or blocked(x, y) or not gen_floodable(GP[x0 + x, y0 + y]):
                continue
            outside[x][y] = True
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if 0 <= nx < cw and 0 <= ny < ch and not outside[nx][ny]:
                    queue.append((nx, ny))
        # Насыщенность самого яркого свечения вокруг рисунка (нет свечения — обычная)
        glow = sorted(chroma(GP[x0 + x, y0 + y]) for x in range(cw) for y in range(ch)
                      if outside[x][y] and sum(GP[x0 + x, y0 + y]) >= 600 and not gen_background(GP[x0 + x, y0 + y]))
        glow_chroma = max(80.0, glow[int(len(glow) * 0.98)] if len(glow) >= 50 else 120.0)
        out = Image.new("RGBA", (cw, ch))
        op = out.load()
        for x in range(cw):
            for y in range(ch):
                c = GP[x0 + x, y0 + y]
                if not outside[x][y]:
                    op[x, y] = c + (255,)
                elif gen_background(c):
                    op[x, y] = (0, 0, 0, 0)
                else:
                    op[x, y] = gen_alpha(c, glow_chroma)
        bbox = out.split()[3].point(lambda v: 255 if v > 24 else 0).getbbox()
        fig = out.crop(bbox)
        side = max(fig.size) + 8
        canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        if name in gen_tight:
            canvas = Image.new("RGBA", (fig.size[0] + 8, fig.size[1] + 8), (0, 0, 0, 0))
            canvas.paste(fig, (4, 4))
        elif name in gen_bottom:
            canvas.paste(fig, ((side - fig.size[0]) // 2, side - fig.size[1] - 4))
        else:
            canvas.paste(fig, ((side - fig.size[0]) // 2, (side - fig.size[1]) // 2))
        folder = {"Symbols": symbols_out, "Map": map_out, "Icons": icons_out}[folder_name]
        save(canvas, folder, name, bottom_pivot=False)
print("generated icons", sum(len(r) for r in gen_names))

print("ok")
