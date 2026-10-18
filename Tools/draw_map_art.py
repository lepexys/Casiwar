"""Рисует простой арт, которого нет в листах художника: караван на карте мира и значок горожанина для поля.

    python Tools/draw_map_art.py <папка проекта>

Пишет Assets/Art/Resources/Map/caravan.png и Assets/Art/Resources/Icons/civilian.png (+ .meta как у остальных спрайтов).
Рисуется в 4 раза крупнее и уменьшается — края сглаживаются.
"""
import os
import re
import sys
import uuid

from PIL import Image, ImageDraw

root = sys.argv[1] if len(sys.argv) > 1 else "."
art = os.path.join(root, "Assets", "Art", "Resources")
S = 4  # сглаживание: рисуем крупнее
OUTLINE = (34, 26, 20, 255)


def meta_template():
    """Шаблон .meta — с готового спрайта значка (тот же импорт: Sprite, центр, 100 px/ед.)."""
    text = open(os.path.join(art, "Icons", "militia.png.meta"), encoding="utf-8").read()
    return re.sub(r"^guid: .*$", "guid: {guid}", text, count=1, flags=re.M)


def save(img, folder, name):
    os.makedirs(folder, exist_ok=True)
    folder_meta = folder + ".meta"
    if not os.path.exists(folder_meta):
        with open(folder_meta, "w", encoding="utf-8", newline="\n") as f:
            f.write("fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
                    "  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % uuid.uuid4().hex)
    path = os.path.join(folder, name + ".png")
    img.save(path)
    meta = path + ".meta"
    guid = None
    if os.path.exists(meta):  # гуид сохраняем — ссылки не слетят
        for line in open(meta, encoding="utf-8"):
            if line.startswith("guid:"):
                guid = line.split(":", 1)[1].strip()
    with open(meta, "w", encoding="utf-8", newline="\n") as f:
        f.write(meta_template().replace("{guid}", guid or uuid.uuid4().hex))


def p(points):
    return [(x * S, y * S) for x, y in points]


def ellipse(d, box, fill, width=3):
    x0, y0, x1, y1 = box
    d.ellipse((x0 * S, y0 * S, x1 * S, y1 * S), fill=fill, outline=OUTLINE, width=width * S)


def poly(d, points, fill, width=3):
    d.polygon(p(points), fill=fill, outline=OUTLINE)
    if width:
        d.line(p(points + [points[0]]), fill=OUTLINE, width=width * S, joint="curve")


def caravan():
    """Повозка с тентом и мешками золота, её тянет бык (смотрит вправо, как фигурки)."""
    w, h = 320, 210
    img = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    wood, wood_dark, canvas, canvas_shade = (150, 96, 52, 255), (104, 64, 34, 255), (238, 226, 196, 255), (206, 190, 156, 255)
    ox, ox_dark, horn = (128, 84, 52, 255), (96, 60, 36, 255), (236, 228, 206, 255)

    # Дышло: от повозки к ярму быка
    d.line(p([(180, 150), (236, 142)]), fill=OUTLINE, width=10 * S)
    d.line(p([(180, 150), (236, 142)]), fill=wood_dark, width=5 * S)

    # Бык: задние и передние ноги, туловище, голова с рогами
    for x in (236, 252, 286, 300):
        poly(d, [(x - 5, 150), (x + 5, 150), (x + 4, 186), (x - 4, 186)], ox_dark, 2)
        ellipse(d, (x - 6, 182, x + 6, 190), OUTLINE, 1)
    ellipse(d, (222, 112, 306, 162), ox)
    ellipse(d, (230, 116, 300, 138), (150, 104, 68, 255), 0)  # блик на спине
    poly(d, [(292, 120), (316, 112), (318, 146), (300, 152)], ox, 3)  # шея и морда
    ellipse(d, (300, 132, 318, 150), (176, 128, 100, 255), 2)  # нос
    poly(d, [(296, 112), (286, 96), (300, 108)], horn, 2)  # рога
    poly(d, [(312, 110), (318, 94), (316, 112)], horn, 2)
    ellipse(d, (305, 117, 311, 123), OUTLINE, 0)  # глаз
    d.line(p([(226, 128), (214, 152)]), fill=OUTLINE, width=3 * S)  # хвост
    poly(d, [(236, 112), (244, 104), (252, 112)], wood_dark, 2)  # ярмо

    # Повозка: короб из досок
    poly(d, [(28, 112), (190, 112), (184, 156), (34, 156)], wood)
    for y in (126, 140):
        d.line(p([(32, y), (186, y)]), fill=wood_dark, width=2 * S)
    # Груз сзади: мешки с золотом и тюк
    ellipse(d, (14, 86, 50, 118), (200, 160, 70, 255))
    ellipse(d, (20, 92, 30, 102), (250, 214, 110, 255), 0)
    poly(d, [(24, 86), (40, 86), (36, 78), (28, 78)], (200, 160, 70, 255), 2)
    # Тент на дугах
    d.chord((40 * S, 30 * S, 180 * S, 160 * S), 180, 360, fill=canvas, outline=OUTLINE, width=3 * S)
    for x in (72, 110, 148):
        d.arc(((x - 26) * S, 34 * S, (x + 26) * S, 156 * S), 200, 340, fill=canvas_shade, width=3 * S)
    d.line(p([(40, 95), (180, 95)]), fill=OUTLINE, width=3 * S)
    poly(d, [(36, 95), (184, 95), (190, 112), (28, 112)], canvas_shade, 3)  # край тента

    # Колёса со спицами
    for cx, r in ((62, 30), (156, 30)):
        ellipse(d, (cx - r, 158 - r, cx + r, 158 + r), wood_dark, 4)
        ellipse(d, (cx - r + 7, 158 - r + 7, cx + r - 7, 158 + r - 7), (60, 40, 26, 255), 2)
        for k in range(6):
            import math
            a = math.pi * k / 3
            d.line(p([(cx, 158), (cx + math.cos(a) * (r - 7), 158 + math.sin(a) * (r - 7))]), fill=wood, width=4 * S)
        ellipse(d, (cx - 6, 152, cx + 6, 164), wood, 2)

    return img.resize((w, h), Image.LANCZOS)


def civilian():
    """Горожанин: простая фигурка (голова, плечи, рубаха с поясом) — символ «мирных» на поле армии."""
    n = 140
    img = Image.new("RGBA", (n * S, n * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    shirt, shirt_shade, skin, hair = (146, 132, 116, 255), (118, 104, 90, 255), (232, 196, 160, 255), (110, 76, 46, 255)
    # Туловище: трапеция с округлыми плечами
    poly(d, [(36, 128), (40, 84), (52, 72), (88, 72), (100, 84), (104, 128)], shirt, 4)
    d.line(p([(70, 76), (70, 128)]), fill=shirt_shade, width=3 * S)
    d.line(p([(40, 106), (100, 106)]), fill=(88, 58, 34, 255), width=6 * S)  # пояс
    # Голова и волосы
    ellipse(d, (48, 22, 92, 70), skin, 4)
    d.chord((48 * S, 20 * S, 92 * S, 56 * S), 180, 360, fill=hair, outline=OUTLINE, width=3 * S)
    ellipse(d, (59, 44, 64, 49), OUTLINE, 0)
    ellipse(d, (76, 44, 81, 49), OUTLINE, 0)
    return img.resize((n, n), Image.LANCZOS)


save(caravan(), os.path.join(art, "Map"), "caravan")
save(civilian(), os.path.join(art, "Icons"), "civilian")
print("ok")
