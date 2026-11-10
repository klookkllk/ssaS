#!/usr/bin/env python3
"""Генератор кубических (в стиле Minecraft) моделей врагов-военных США.

Запуск:  python3 generate_soldiers.py   (зависимостей нет, только стандартная библиотека)
Результат: ../soldier_gasmask.glb и ../soldier_balaclava.glb — модель из кубоидов с пиксельной
текстурой (атлас внутри .glb), скелетом из узлов и анимациями idle_loop / walk_loop / shoot / death.

Координаты авторинга — в «пикселях» (1 px = PX метров): +Y вверх, +Z — куда смотрит боец,
+X — его левая рука. Ноги стоят на y = 0.
"""
import json
import math
import os
import struct
import zlib

PX = 0.028          # метров в одном пикселе: тело 64 px ≈ 1.8 м
ATLAS = 512
TORSO_YAW = math.radians(-42)   # стойка стрелка: левое плечо вперёд

# ---------------------------------------------------------------- палитра
TAN = (176, 158, 120)
OLIVE = (104, 110, 72)
BROWN = (92, 74, 52)
PALE = (204, 194, 162)
DGREEN = (68, 78, 54)
COYOTE = (140, 110, 72)
COYOTE_D = (112, 87, 55)
BLACK = (30, 30, 33)
RUBBER = (40, 42, 45)
GUN = (62, 64, 70)
GUN_L = (94, 96, 103)
STEEL = (130, 134, 140)
SKIN = (216, 170, 136)
SKIN_D = (178, 132, 102)
BOOT = (152, 122, 86)
SOLE = (58, 46, 36)
OD = (82, 90, 58)
OD_D = (60, 68, 44)
HOOD = (58, 64, 50)
KNIT = (36, 37, 41)
RED = (178, 34, 44)
WHITE = (236, 236, 232)
BLUE = (44, 54, 110)
AMBER = (255, 168, 40)
FLASH = (255, 214, 96)


# ---------------------------------------------------------------- шум и материалы
def hash3(x, y, z, s=0):
    n = (x * 73856093) ^ (y * 19349663) ^ (z * 83492791) ^ (s * 2654435761)
    n &= 0xFFFFFFFF
    n = ((n ^ (n >> 13)) * 1274126177) & 0xFFFFFFFF
    n ^= n >> 16
    return (n & 0xFFFF) / 65535.0


def vnoise(x, y, z, cell, s):
    fx, fy, fz = x / cell, y / cell, z / cell
    ix, iy, iz = math.floor(fx), math.floor(fy), math.floor(fz)
    tx, ty, tz = fx - ix, fy - iy, fz - iz
    acc = 0.0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (tx if dx else 1 - tx) * (ty if dy else 1 - ty) * (tz if dz else 1 - tz)
                acc += w * hash3(ix + dx, iy + dy, iz + dz, s)
    return acc


def scale(col, k):
    return tuple(max(0, min(255, int(round(c * k)))) for c in col)


def jitter(col, x, y, z, amount):
    return scale(col, 1 + (hash3(x, y, z, 7) - 0.5) * 2 * amount)


def solid(col, amount=0.06):
    return lambda x, y, z: jitter(col, x, y, z, amount)


def camo(x, y, z):
    """Пятнистый камуфляж в духе OCP: сплошная 3D-функция, пятна не рвутся на гранях."""
    col = TAN
    if vnoise(x, y, z, 5.0, 1) > 0.55:
        col = OLIVE
    if vnoise(x, y, z, 3.6, 2) > 0.62:
        col = BROWN
    if vnoise(x, y, z, 2.6, 3) > 0.70:
        col = PALE
    if vnoise(x, y, z, 3.0, 4) > 0.72:
        col = DGREEN
    return jitter(col, x, y, z, 0.05)


def camo_dark(x, y, z):
    return scale(camo(x, y, z), 0.86)


def webbing(col):
    """Ткань со стропами MOLLE: каждая третья строка темнее."""
    def f(x, y, z):
        return jitter(scale(col, 0.84 if y % 3 == 0 else 1.0), x, y, z, 0.05)
    return f


def knit(col):
    def f(x, y, z):
        return jitter(scale(col, 1.12 if (x + y + z) % 2 else 0.94), x, y, z, 0.05)
    return f


def ribbed(col):
    def f(x, y, z):
        return jitter(scale(col, 0.8 if y % 2 == 0 else 1.05), x, y, z, 0.04)
    return f


# ---------------------------------------------------------------- кватернионы (x, y, z, w)
def q_axis(axis, ang):
    s = math.sin(ang / 2)
    return (axis[0] * s, axis[1] * s, axis[2] * s, math.cos(ang / 2))


def qx(deg): return q_axis((1, 0, 0), math.radians(deg))
def qy(deg): return q_axis((0, 1, 0), math.radians(deg))
def qz(deg): return q_axis((0, 0, 1), math.radians(deg))


def q_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def q_rot(q, v):
    p = q_mul(q_mul(q, (v[0], v[1], v[2], 0.0)), (-q[0], -q[1], -q[2], q[3]))
    return (p[0], p[1], p[2])


def q_from_axes(X, Y, Z):
    """Кватернион из ортонормированного базиса (столбцы матрицы поворота)."""
    m00, m10, m20 = X
    m01, m11, m21 = Y
    m02, m12, m22 = Z
    tr = m00 + m11 + m22
    if tr > 0:
        s = math.sqrt(tr + 1) * 2
        return ((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, s / 4)
    if m00 > m11 and m00 > m22:
        s = math.sqrt(1 + m00 - m11 - m22) * 2
        return (s / 4, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s)
    if m11 > m22:
        s = math.sqrt(1 + m11 - m00 - m22) * 2
        return ((m01 + m10) / s, s / 4, (m12 + m21) / s, (m02 - m20) / s)
    s = math.sqrt(1 + m22 - m00 - m11) * 2
    return ((m02 + m20) / s, (m12 + m21) / s, s / 4, (m10 - m01) / s)


def v_add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def v_sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def v_mul(a, k): return (a[0] * k, a[1] * k, a[2] * k)
def v_dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def v_len(a): return math.sqrt(v_dot(a, a))
def v_norm(a): return v_mul(a, 1 / v_len(a))


def v_cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def two_bone_ik(shoulder, target, l1, l2, pole):
    """Поворот плеча и угол локтя (вокруг локальной X), чтобы кисть пришла в target.
    Кости в исходной позе направлены вдоль -Y."""
    d = v_sub(target, shoulder)
    dist = max(abs(l1 - l2) + 0.5, min(v_len(d), (l1 + l2) * 0.985))
    dirn = v_norm(d)
    cos_a = (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist)
    sin_a = math.sqrt(max(0.0, 1 - cos_a * cos_a))
    pn = v_norm(v_sub(pole, v_mul(dirn, v_dot(pole, dirn))))
    u = v_add(v_mul(dirn, cos_a), v_mul(pn, sin_a))
    elbow = v_add(shoulder, v_mul(u, l1))
    f = v_norm(v_sub(v_add(shoulder, v_mul(dirn, dist)), elbow))
    X = v_norm(v_cross(f, u))
    if X[0] < 0:
        X = v_mul(X, -1)
    Y = v_mul(u, -1)
    Z = v_cross(X, Y)
    f_local = (v_dot(f, X), v_dot(f, Y), v_dot(f, Z))
    bend = math.atan2(-f_local[2], -f_local[1])
    return q_from_axes(X, Y, Z), q_axis((1, 0, 0), bend)


# ---------------------------------------------------------------- атлас
class Atlas:
    def __init__(self):
        self.rgb = bytearray(ATLAS * ATLAS * 3)
        self.emis = bytearray(ATLAS * ATLAS * 3)
        self.x = self.y = self.row_h = 0
        self.rects = []

    def alloc(self, w, h):
        # 1 px поля вокруг каждой грани, чтобы соседние грани не подтекали
        if self.x + w + 2 > ATLAS:
            self.x, self.y, self.row_h = 0, self.y + self.row_h, 0
        if self.y + h + 2 > ATLAS:
            raise RuntimeError("atlas overflow")
        rect = (self.x + 1, self.y + 1, w, h)
        self.x += w + 2
        self.row_h = max(self.row_h, h + 2)
        self.rects.append(rect)
        return rect

    def set(self, x, y, col, emis=False):
        o = (y * ATLAS + x) * 3
        self.rgb[o:o + 3] = bytes(col)
        self.emis[o:o + 3] = bytes(col) if emis else b"\0\0\0"

    def get(self, buf, x, y):
        o = (y * ATLAS + x) * 3
        return buf[o:o + 3]

    def finalize(self):
        for buf in (self.rgb, self.emis):
            for (x, y, w, h) in self.rects:
                for i in range(w):
                    self._copy(buf, x + i, y, x + i, y - 1)
                    self._copy(buf, x + i, y + h - 1, x + i, y + h)
                for j in range(-1, h + 1):
                    self._copy(buf, x, y + j, x - 1, y + j)
                    self._copy(buf, x + w - 1, y + j, x + w, y + j)

    def _copy(self, buf, sx, sy, dx, dy):
        o = (dy * ATLAS + dx) * 3
        buf[o:o + 3] = self.get(buf, sx, sy)

    @staticmethod
    def png(buf):
        raw = bytearray()
        stride = ATLAS * 3
        for y in range(ATLAS):
            raw.append(0)
            raw += buf[y * stride:(y + 1) * stride]

        def chunk(tag, data):
            c = struct.pack(">I", len(data)) + tag + data
            return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", ATLAS, ATLAS, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b""))


# ---------------------------------------------------------------- кубоиды и узлы
FACES = ("front", "back", "left", "right", "top", "bottom")
SHADE = {"front": 1.0, "back": 0.9, "left": 0.95, "right": 0.95, "top": 1.08, "bottom": 0.72}
NORMAL = {"front": (0, 0, 1), "back": (0, 0, -1), "left": (1, 0, 0), "right": (-1, 0, 0),
          "top": (0, 1, 0), "bottom": (0, -1, 0)}


class Box:
    """Кубоид. Грани: front=+Z, back=-Z, left=+X, right=-X, top, bottom.
    Координаты на грани: i — вправо, j — вниз, если смотреть на грань снаружи."""

    def __init__(self, atlas, x0, y0, z0, x1, y1, z1, mat, outline=0.84, emis=False):
        self.atlas = atlas
        self.min = (x0, y0, z0)
        self.max = (x1, y1, z1)
        self.emis = emis
        W, H, D = x1 - x0, y1 - y0, z1 - z0
        for v in (W, H, D):
            assert v > 0 and abs(v - round(v)) < 1e-6, "box sizes must be whole pixels"
        W, H, D = int(round(W)), int(round(H)), int(round(D))
        dims = {"front": (W, H), "back": (W, H), "left": (D, H), "right": (D, H), "top": (W, D), "bottom": (W, D)}
        self.rect = {}
        for f in FACES:
            w, h = dims[f]
            ax, ay, _, _ = self.rect[f] = atlas.alloc(w, h)
            edge = outline if (w >= 3 and h >= 3) else 1.0
            for j in range(h):
                for i in range(w):
                    p = self.texel_pos(f, i, j)
                    col = mat(math.floor(p[0]), math.floor(p[1]), math.floor(p[2]))
                    k = SHADE[f] * (edge if (i in (0, w - 1) or j in (0, h - 1)) else 1.0)
                    atlas.set(ax + i, ay + j, scale(col, k), emis)

    def texel_pos(self, f, i, j):
        x0, y0, z0 = self.min
        x1, y1, z1 = self.max
        a, b = i + 0.5, j + 0.5
        return {"front": (x0 + a, y1 - b, z1 - 0.5), "back": (x1 - a, y1 - b, z0 + 0.5),
                "left": (x1 - 0.5, y1 - b, z1 - a), "right": (x0 + 0.5, y1 - b, z0 + a),
                "top": (x0 + a, y1 - 0.5, z0 + b), "bottom": (x0 + a, y0 + 0.5, z1 - b)}[f]

    def corners(self, f):
        """TL, TR, BR, BL — в тех же осях i/j, что и текстура грани."""
        x0, y0, z0 = self.min
        x1, y1, z1 = self.max
        return {"front": ((x0, y1, z1), (x1, y1, z1), (x1, y0, z1), (x0, y0, z1)),
                "back": ((x1, y1, z0), (x0, y1, z0), (x0, y0, z0), (x1, y0, z0)),
                "left": ((x1, y1, z1), (x1, y1, z0), (x1, y0, z0), (x1, y0, z1)),
                "right": ((x0, y1, z0), (x0, y1, z1), (x0, y0, z1), (x0, y0, z0)),
                "top": ((x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)),
                "bottom": ((x0, y0, z1), (x1, y0, z1), (x1, y0, z0), (x0, y0, z0))}[f]

    def px(self, f, i, j, col, emis=None, shade=True):
        ax, ay, w, h = self.rect[f]
        assert 0 <= i < w and 0 <= j < h, f"pixel ({i},{j}) outside {f} face {w}x{h}"
        self.atlas.set(ax + i, ay + j, scale(col, SHADE[f]) if shade else col,
                       self.emis if emis is None else emis)

    def fill(self, f, i, j, w, h, col, emis=None, noise=0.05):
        for b in range(j, j + h):
            for a in range(i, i + w):
                self.px(f, a, b, jitter(col, a, b, i + 31 * j, noise), emis)

    def sprite(self, f, i, j, rows, emis=None, mirror=False):
        for b, row in enumerate(rows):
            for a, col in enumerate(row[::-1] if mirror else row):
                if col is not None:
                    self.px(f, i + a, j + b, col, emis, shade=False)

    def sides(self):
        return ("front", "back", "left", "right")


class Part:
    """Узел glTF. Кубоиды задаются в общих координатах модели, pivot — точка вращения узла."""

    def __init__(self, name, parent=None, pivot=(0, 0, 0), trans=None, rot=(0, 0, 0, 1), scl=(1, 1, 1)):
        self.name, self.parent, self.pivot = name, parent, pivot
        self.trans = trans if trans is not None else v_sub(pivot, parent.pivot if parent else (0, 0, 0))
        self.rot, self.scl = rot, scl
        self.boxes, self.children = [], []
        if parent:
            parent.children.append(self)

    def all(self):
        yield self
        for c in self.children:
            yield from c.all()


class Model:
    def __init__(self, name):
        self.name = name
        self.atlas = Atlas()
        self.root = None
        self.anims = []

    def box(self, part, x0, y0, z0, x1, y1, z1, mat, **kw):
        b = Box(self.atlas, x0, y0, z0, x1, y1, z1, mat, **kw)
        part.boxes.append(b)
        return b


def xr(a, b, side):
    """Диапазон по X для левой (side=+1) или правой (side=-1) стороны тела."""
    return (a, b) if side > 0 else (-b, -a)


def outer(side):
    return "left" if side > 0 else "right"


def flag_rows(w, h, cw, ch):
    rows = []
    for j in range(h):
        row = []
        for i in range(w):
            if i < cw and j < ch:
                row.append(WHITE if (i % 2 == 1 and j % 2 == 1) else BLUE)
            else:
                row.append(RED if j % 2 == 0 else WHITE)
        rows.append(row)
    return rows


# ---------------------------------------------------------------- сборка бойца
def build_soldier(variant):
    gasmask = variant == "gasmask"
    m = Model("USSoldierGasMask" if gasmask else "USSoldierBalaclava")
    box = m.box
    glove = RUBBER if gasmask else COYOTE_D

    hips = Part("Hips", None, (0, 24, 0))
    m.root = hips
    torso = Part("Torso", hips, (0, 24, 0), rot=q_axis((0, 1, 0), TORSO_YAW))
    head = Part("Head", torso, (0, 48, 0), rot=q_axis((0, 1, 0), -TORSO_YAW))

    # --- таз: ремень с пряжкой и подсумками
    belt = box(hips, -9, 22, -5, 9, 26, 5, solid(COYOTE_D))
    belt.fill("front", 7, 1, 4, 2, STEEL)
    belt.fill("front", 8, 1, 2, 2, GUN)
    for i in (2, 13):
        belt.fill("front", i, 0, 3, 4, scale(COYOTE_D, 0.8))
    b = box(hips, -4, 20, -7, 4, 26, -5, webbing(COYOTE))           # аптечка сзади на ремне
    b.fill("back", 3, 2, 2, 2, RED)
    b.px("back", 3, 2, WHITE)

    # --- ноги
    for side, nm in ((1, "L"), (-1, "R")):
        x0, x1 = xr(0, 8, side)
        leg = Part("Leg" + nm, hips, (4 * side, 24, 0))
        shin = Part("Shin" + nm, leg, (4 * side, 12, 0))
        th = box(leg, x0, 12, -4, x1, 24, 4, camo)
        for f in th.sides():                                        # ремень набедренной платформы
            th.fill(f, 0, 4, 8, 1, BLACK)
        sh = box(shin, x0, 0, -4, x1, 12, 4, camo)
        for f in sh.sides():
            sh.fill(f, 0, 5, 8, 1, scale(BROWN, 0.8))                # заправленные штанины
            sh.fill(f, 0, 6, 8, 5, BOOT)
            sh.fill(f, 0, 11, 8, 1, SOLE)
        for j in range(6, 11):                                      # шнуровка
            sh.px("front", 3 + (j % 2), j, SOLE)
            sh.px("front", 4 - (j % 2), j, scale(BOOT, 0.75))
        sh.fill("back", 3, 6, 2, 2, scale(BOOT, 0.7))                # петля на пятке
        sh.fill("bottom", 0, 0, 8, 8, SOLE)
        toe = box(shin, x0, 0, 4, x1, 4, 7, solid(BOOT))
        for f in ("front", "left", "right"):
            w = toe.rect[f][2]
            toe.fill(f, 0, 3, w, 1, SOLE)
        toe.fill("bottom", 0, 0, 8, 3, SOLE)
        toe.fill("top", 2, 0, 4, 1, scale(BOOT, 0.75))
        kx0, kx1 = xr(1, 7, side)
        knee = box(shin, kx0, 8, 4, kx1, 14, 6, solid(GUN))          # наколенник
        knee.fill("front", 1, 1, 4, 3, GUN_L)
        knee.fill("front", 2, 2, 2, 1, STEEL)
        knee.fill("front", 0, 4, 6, 1, BLACK)

    leg_l = next(p for p in hips.all() if p.name == "LegL")
    leg_r = next(p for p in hips.all() if p.name == "LegR")

    # кобура с пистолетом на правом бедре
    hol = box(leg_r, -11, 13, -2, -8, 21, 3, solid(BLACK))
    hol.fill("right", 1, 1, 3, 2, GUN_L)
    hol.fill("right", 0, 5, 5, 1, COYOTE_D)
    pg = box(leg_r, -11, 21, -1, -9, 24, 2, solid(GUN_L))
    pg.fill("right", 0, 0, 3, 1, STEEL)
    pg.px("right", 1, 2, BLACK)

    if gasmask:                                                     # сумка противогаза
        bag = box(leg_l, 8, 12, -4, 12, 22, 3, solid(OD))
        for f in ("left", "front", "back"):
            w = bag.rect[f][2]
            bag.fill(f, 0, 0, w, 3, OD_D)
        bag.fill("left", 3, 2, 1, 2, BLACK)
        bag.fill("left", 1, 5, 5, 3, scale(OD, 1.12))                # трафарет
        bag.px("left", 2, 6, OD_D)
        bag.px("left", 4, 6, OD_D)
    else:                                                           # сумка сброса магазинов
        bag = box(leg_l, 8, 14, -3, 11, 22, 3, webbing(COYOTE))
        bag.fill("left", 0, 0, 6, 2, COYOTE_D)
        bag.fill("top", 0, 0, 3, 6, BLACK)
        fr = box(leg_l, 8, 22, -2, 10, 25, 1, solid(OD_D))           # граната
        fr.fill("left", 1, 0, 1, 1, STEEL)

    # --- торс: китель
    tb = box(torso, -8, 24, -4, 8, 48, 4, camo)
    tb.fill("front", 5, 0, 6, 1, scale(TAN, 0.7))                    # воротник
    tb.fill("front", 7, 1, 2, 2, (120, 104, 78))
    tb.fill("back", 5, 0, 6, 1, scale(TAN, 0.7))
    for j in (21, 23):
        tb.px("front", 8, j, scale(BROWN, 0.7))

    # бронежилет со стропами MOLLE
    vest = box(torso, -9, 28, -5, 9, 45, 5, webbing(COYOTE))
    for f in vest.sides():
        w = vest.rect[f][2]
        vest.fill(f, 0, 13, w, 4, COYOTE_D)                          # камербанд
        vest.fill(f, 0, 12, w, 1, scale(COYOTE_D, 0.75))
    vest.fill("front", 3, 0, 12, 1, scale(COYOTE_D, 0.75))
    for sx in (-1, 1):
        s0, s1 = xr(3, 7, sx)
        st = box(torso, s0, 45, -5, s1, 48, 5, solid(COYOTE_D))      # плечевые лямки
        st.fill("front", 1, 1, 2, 1, BLACK)
        st.fill("back", 1, 1, 2, 1, BLACK)

    # три подсумка с магазинами
    for px0 in (-8, -2, 4):
        p = box(torso, px0, 29, 5, px0 + 4, 37, 7, solid(COYOTE_D))
        p.fill("front", 0, 0, 4, 3, COYOTE)
        p.fill("front", 0, 3, 4, 1, scale(COYOTE_D, 0.7))
        p.px("front", 1, 2, BLACK)
        p.px("front", 2, 2, BLACK)
        mg = box(torso, px0 + 1, 37, 5, px0 + 3, 39, 7, solid(GUN_L), outline=1.0)
        mg.fill("top", 0, 0, 2, 2, GUN)
        mg.px("top", 0, 0, (190, 150, 60))                            # патрон

    # нашивка-флаг на груди
    fl = box(torso, -6, 39, 5, 3, 44, 6, solid(OD_D), outline=1.0)
    fl.sprite("front", 0, 0, flag_rows(9, 5, 4, 3))

    # тангента рации на левой лямке
    ptt = box(torso, 5, 39, 5, 8, 44, 7, solid(BLACK))
    ptt.fill("front", 1, 1, 1, 1, GUN_L)
    ptt.px("front", 1, 3, (200, 40, 40))
    ptt.fill("left", 0, 2, 2, 1, GUN_L)

    # рюкзак, карман, скатка, большой флаг
    pack = box(torso, -6, 29, -10, 6, 45, -5, camo_dark)
    for f in ("left", "right"):
        pack.fill(f, 0, 5, 5, 1, BLACK)
        pack.fill(f, 0, 11, 5, 1, BLACK)
        pack.px(f, 2, 5, STEEL)
        pack.px(f, 2, 11, STEEL)
    pack.fill("top", 5, 1, 2, 3, BLACK)                              # ручка
    pk = box(torso, -4, 30, -12, 4, 37, -10, camo)
    pk.fill("back", 0, 0, 8, 2, scale(OLIVE, 0.8))
    pk.fill("back", 1, 2, 6, 1, BLACK)
    pk.px("back", 6, 2, STEEL)
    for i in (1, 6):
        pk.fill("back", i, 3, 1, 4, scale(BROWN, 0.8))
    bf = box(torso, -5.5, 38, -11, 5.5, 45, -10, solid(OD_D), outline=1.0)
    bf.sprite("back", 0, 0, flag_rows(11, 7, 5, 4))
    roll = box(torso, -7, 45, -13, 7, 49, -9, ribbed(OD))
    for f in ("back", "top", "front", "bottom"):
        h = roll.rect[f][3]
        for i in (2, 11):
            roll.fill(f, i, 0, 1, h, BLACK)
    for f in ("left", "right"):                                     # торец скатки — спираль
        roll.fill(f, 1, 1, 2, 2, OD_D)
        roll.px(f, 1, 1, scale(OD, 1.2))

    # рация с антенной
    rad = box(torso, 6, 33, -11, 9, 43, -7, solid(GUN))
    rad.fill("back", 0, 0, 3, 2, BLACK)
    rad.px("back", 1, 4, (70, 200, 90), emis=True)
    rad.fill("left", 1, 3, 2, 4, GUN_L)
    rad.fill("top", 0, 0, 1, 1, STEEL)
    box(torso, 7, 43, -11, 8, 45, -10, solid(GUN_L), outline=1.0)
    box(torso, 7, 45, -11, 8, 59, -10, solid(BLACK), outline=1.0)

    # --- руки
    arms = {}
    for side, nm in ((1, "L"), (-1, "R")):
        x0, x1 = xr(8, 16, side)
        arm = Part("Arm" + nm, torso, (12 * side, 44, 0))
        fore = Part("Forearm" + nm, arm, (12 * side, 36, 0))
        arms[nm] = (arm, fore)
        up = box(arm, x0, 36, -4, x1, 48, 4, camo)
        up.fill(outer(side), 0, 9, 8, 1, scale(BROWN, 0.8))           # карман на рукаве
        p0, p1 = xr(16, 17, side)
        patch = box(arm, p0, 40, -3.5, p1, 45, 3.5, solid(OD_D), outline=1.0)
        # кантон всегда обращён вперёд, как положено на рукаве
        patch.sprite(outer(side), 0, 0, flag_rows(7, 5, 3, 3), mirror=side < 0)
        fa = box(fore, x0, 24, -4, x1, 36, 4, camo)
        for f in fa.sides():
            fa.fill(f, 0, 7, 8, 5, glove)
            fa.fill(f, 0, 7, 8, 1, scale(glove, 0.7))
        for i in (1, 3, 5):                                         # костяшки
            fa.fill(outer(side), i, 9, 1, 2, scale(glove, 1.3))
        fa.fill("bottom", 0, 0, 8, 8, glove)
        for i in (2, 4):
            fa.fill("bottom", i, 1, 1, 6, scale(glove, 0.7))          # пальцы
        e0, e1 = xr(9, 15, side)
        el = box(fore, e0, 30, -6, e1, 36, -4, solid(GUN))            # налокотник
        el.fill("back", 1, 1, 4, 3, GUN_L)
        el.fill("back", 0, 4, 6, 1, BLACK)
    w = box(arms["L"][1], 16, 29, -1, 17, 31, 1, solid(BLACK), outline=1.0)   # часы
    w.px("left", 0, 0, (90, 220, 120), emis=True)

    # --- голова
    hd = box(head, -8, 48, -8, 8, 64, 8, solid(HOOD, 0.08) if gasmask else knit(KNIT))
    helmet_y = 57
    hm = box(head, -9, helmet_y, -9, 9, 67, 9, camo)
    for f in hm.sides():
        hm.fill(f, 0, 9, 18, 1, scale(BROWN, 0.6))                    # кромка шлема
        hm.fill(f, 0, 5, 18, 1, BLACK)                                # лента / ремень очков
    hm.px("back", 6, 5, (190, 230, 170), emis=True)                   # «кошачьи глаза»
    hm.px("back", 7, 5, (190, 230, 170), emis=True)
    hm.px("back", 10, 5, (190, 230, 170), emis=True)
    hm.px("back", 11, 5, (190, 230, 170), emis=True)
    hm.fill("top", 7, 7, 4, 4, scale(OLIVE, 0.75))                    # велкро на макушке
    hm.sprite("left", 6, 1, [r[:5] for r in flag_rows(5, 3, 2, 2)])
    hm.sprite("right", 7, 1, [r[:5] for r in flag_rows(5, 3, 2, 2)], mirror=True)
    for sx in (-1, 1):
        r0, r1 = xr(9, 10, sx)
        rail = box(head, r0, 58, -4, r1, 60, 5, solid(BLACK), outline=1.0)   # боковые рельсы
        for i in (1, 4, 7):
            rail.px(outer(sx), i, 0, GUN_L)
        # подбородочный ремень
        hd.fill(outer(sx), 7, 7, 2, 9, COYOTE_D)
    hd.fill("bottom", 2, 7, 12, 2, COYOTE_D)

    if gasmask:
        # ремни оголовья противогаза
        for f in ("left", "right"):
            hd.fill(f, 0, 8, 16, 1, BLACK)
            hd.fill(f, 0, 13, 16, 1, BLACK)
        hd.fill("back", 0, 8, 16, 1, BLACK)
        hd.fill("back", 0, 13, 16, 1, BLACK)
        hd.fill("back", 5, 8, 6, 6, RUBBER)
        hd.fill("back", 7, 10, 2, 2, GUN_L)
        # маска
        mask = box(head, -7, 48, 8, 7, 60, 10, solid(RUBBER))
        mask.fill("front", 5, 0, 4, 1, scale(RUBBER, 0.7))
        mask.fill("front", 6, 2, 2, 4, scale(RUBBER, 1.25))           # переносица
        for f in ("left", "right"):
            mask.fill(f, 0, 3, 2, 1, STEEL)                           # пряжки
            mask.fill(f, 0, 8, 2, 1, STEEL)
        ring, glass, hi = GUN_L, AMBER, (255, 226, 150)
        lens = [[None, ring, ring, ring, None],
                [ring, hi, glass, glass, ring],
                [ring, glass, glass, scale(glass, 0.8), ring],
                [ring, glass, scale(glass, 0.8), scale(glass, 0.65), ring],
                [None, ring, ring, ring, None]]
        for lx in (-6, 1):
            lb = box(head, lx, 54, 10, lx + 5, 59, 11, solid(RUBBER), outline=1.0)
            lb.sprite("front", 0, 0, lens)
            for j in range(1, 4):
                for i in range(1, 4):
                    lb.px("front", i, j, lens[j][i], emis=True, shade=False)
        em = box(head, -2, 49, 10, 2, 53, 12, solid(GUN))             # переговорная мембрана
        em.sprite("front", 0, 0, [[GUN, GUN_L, GUN_L, GUN],
                                  [GUN_L, BLACK, STEEL, GUN_L],
                                  [GUN_L, STEEL, BLACK, GUN_L],
                                  [GUN, GUN_L, GUN_L, GUN]])
        can = box(head, 3, 48, 10, 8, 53, 15, ribbed(OD))             # фильтрующая коробка
        can.sprite("front", 0, 0, [[OD_D, OD_D, OD_D, OD_D, OD_D],
                                   [OD_D, BLACK, STEEL, BLACK, OD_D],
                                   [OD_D, STEEL, BLACK, STEEL, OD_D],
                                   [OD_D, BLACK, STEEL, BLACK, OD_D],
                                   [OD_D, OD_D, OD_D, OD_D, OD_D]])
        can.fill("top", 1, 1, 3, 3, scale(OD, 1.15))
        can.px("left", 2, 2, (220, 200, 60))                          # маркировка
        can.px("left", 3, 2, (220, 200, 60))
        # крепление ПНВ и сам прибор, откинутый вверх
        sh = box(head, -2, 60, 9, 2, 64, 10, solid(GUN))
        sh.fill("front", 1, 1, 2, 2, GUN_L)
        box(head, -1, 63, 10, 1, 68, 11, solid(BLACK), outline=1.0)
        for nx in (-4, 1):
            tube = box(head, nx, 67, 6, nx + 3, 70, 12, solid(GUN))
            tube.fill("front", 1, 1, 1, 1, (70, 220, 110), emis=True)
            tube.fill("back", 1, 1, 1, 1, BLACK)
            tube.fill("top", 0, 2, 3, 1, GUN_L)
        box(head, -1, 68, 8, 1, 69, 10, solid(GUN_L), outline=1.0)
        # ИК-маячок на затылке шлема
        sb = box(head, 3, 67, -6, 6, 68, -3, solid(GUN))
        sb.px("top", 1, 1, (230, 60, 50), emis=True)
    else:
        # прорезь балаклавы с глазами
        s, d, wht, iris, h_ = SKIN, (88, 62, 44), (238, 238, 232), (70, 110, 150), SKIN_D
        hd.sprite("front", 3, 8, [[None, s, d, d, s, s, d, d, s, None],
                                  [s, wht, iris, s, h_, h_, s, iris, wht, s],
                                  [None, h_, h_, s, s, s, s, h_, h_, None]])
        hd.fill("front", 6, 13, 4, 1, scale(KNIT, 0.7))               # рот под тканью
        hd.fill("front", 0, 15, 16, 1, scale(KNIT, 1.3))              # резинка ворота
        box(head, -1, 50, 8, 1, 54, 9, knit(KNIT), outline=1.0)       # нос
        # тактические очки на шлеме
        gg = box(head, -7, 59, 9, 7, 64, 11, solid(BLACK))
        lens_c = (214, 132, 44)
        gg.fill("front", 1, 1, 12, 3, lens_c, noise=0.03)
        gg.fill("front", 6, 3, 2, 1, BLACK)
        for i in range(3):                                          # блик
            gg.px("front", 2 + i, 3 - i, (250, 206, 140))
            gg.px("front", 9 + i, 3 - i, (250, 206, 140))
        gg.fill("top", 2, 0, 10, 1, GUN_L)
        # активные наушники с микрофоном
        for sx in (-1, 1):
            c0, c1 = xr(8, 10, sx)
            cup = box(head, c0, 49, -3, c1, 57, 3, solid(OD))
            cup.fill(outer(sx), 1, 1, 4, 5, OD_D)
            cup.fill(outer(sx), 2, 2, 2, 2, BLACK)
            cup.px(outer(sx), 2, 5, STEEL)
        box(head, 10, 51, 3, 11, 52, 10, solid(BLACK), outline=1.0)   # штанга
        mic = box(head, 6, 50, 9, 11, 53, 11, solid(BLACK), outline=1.0)
        mic.fill("front", 0, 0, 3, 3, GUN)
        mic.px("front", 1, 1, GUN_L)
        # фонарик на шлеме
        lt = box(head, -10, 61, 5, -9, 63, 8, solid(GUN), outline=1.0)
        lt.px("front", 0, 0, (240, 240, 220), emis=True)

    # --- автомат (карабин М4): свои координаты, +Z — к дулу, начало — у пистолетной рукояти
    rifle_origin = (-6, 41, 10)                                     # в координатах модели
    inv_yaw = q_axis((0, 1, 0), -TORSO_YAW)
    to_torso = lambda p: q_rot(inv_yaw, v_sub(p, torso.pivot))       # модель -> локальные торса
    rifle = Part("Rifle", torso, trans=to_torso(rifle_origin), rot=inv_yaw)
    g = solid(GUN)
    st = box(rifle, -1, -4, -10, 1, 1, -6, g)                        # приклад
    st.fill("back", 0, 0, 2, 5, RUBBER)
    st.fill("left", 1, 3, 2, 1, BLACK)
    st.fill("right", 1, 3, 2, 1, BLACK)
    box(rifle, -0.5, -1, -6, 0.5, 1, -4, solid(GUN_L), outline=1.0)  # труба буфера
    rc = box(rifle, -1, -3, -4, 1, 2, 5, g)                          # ствольная коробка
    rc.fill("right", 4, 1, 3, 2, GUN_L)                              # окно выброса гильз
    rc.px("right", 5, 2, (190, 150, 60))
    rc.px("left", 6, 3, STEEL)                                       # переводчик огня
    rc.fill("left", 2, 1, 2, 1, GUN_L)
    rc.fill("top", 0, 0, 2, 9, GUN_L)
    for j in range(0, 9, 2):
        rc.fill("top", 0, j, 2, 1, BLACK)
    gr = box(rifle, -1, -8, -4, 1, -3, -2, solid(BLACK))             # рукоять
    gr.fill("back", 0, 1, 2, 3, RUBBER)
    mgz = box(rifle, -1, -9, 1, 1, -3, 4, solid(GUN_L))              # магазин
    for f in ("left", "right"):
        mgz.fill(f, 1, 1, 1, 4, STEEL)
        mgz.fill(f, 0, 5, 3, 1, BLACK)
    hg = box(rifle, -1.5, -2, 5, 1.5, 2, 12, g)                      # цевьё с планками
    for f in ("left", "right", "top", "bottom"):
        w_, h_2 = hg.rect[f][2], hg.rect[f][3]
        if f in ("left", "right"):
            for i in range(1, w_ - 1, 2):
                hg.fill(f, i, 1, 1, 2, BLACK)
        else:
            for j in range(1, h_2 - 1, 2):
                hg.fill(f, 1, j, 1, 1, BLACK)
    box(rifle, -0.5, -0.5, 12, 0.5, 0.5, 15, solid(GUN_L), outline=1.0)   # ствол
    fh = box(rifle, -1, -1, 15, 1, 1, 17, solid(BLACK), outline=1.0)       # пламегаситель
    fh.fill("front", 0, 0, 2, 2, GUN)
    fs = box(rifle, -0.5, 2, 10, 0.5, 5, 11, solid(BLACK), outline=1.0)    # мушка
    fs.px("back", 0, 0, (230, 230, 220))
    box(rifle, -1, 2, -1, 1, 3, 2, solid(BLACK), outline=1.0)        # кронштейн прицела
    op = box(rifle, -1, 3, -2, 1, 6, 3, g)                           # коллиматор
    op.fill("front", 0, 0, 2, 3, BLACK)
    op.fill("front", 0, 1, 2, 1, (170, 46, 36))
    op.fill("back", 0, 0, 2, 3, BLACK)
    op.fill("back", 0, 1, 2, 1, (70, 110, 130))
    op.px("back", 0, 1, (255, 60, 40), emis=True)                    # красная точка
    op.fill("top", 0, 2, 2, 1, GUN_L)
    fg = box(rifle, -1, -6, 8, 1, -2, 10, solid(BLACK))              # передняя рукоять
    fg.fill("front", 0, 3, 2, 1, GUN_L)
    peq = box(rifle, -1, 2, 6, 1, 4, 9, solid(TAN))                  # лазерный целеуказатель
    peq.fill("front", 0, 0, 1, 1, BLACK)
    peq.px("front", 1, 0, (230, 60, 50), emis=True)
    peq.fill("top", 0, 1, 2, 1, COYOTE_D)
    lg = box(rifle, 1.5, -1, 8, 3.5, 1, 12, solid(BLACK))            # фонарь
    lg.fill("front", 0, 0, 2, 2, (236, 236, 214), emis=True)

    # дульная вспышка: спрятана масштабом, показывается анимацией shoot
    flash = Part("MuzzleFlash", rifle, trans=(0, 0, 17), scl=(0.001, 0.001, 0.001))
    fm = solid(FLASH, 0.1)
    box(flash, -1.5, -1.5, 0, 1.5, 1.5, 4, solid((255, 244, 200), 0.03), outline=1.0, emis=True)
    box(flash, -4.5, -0.5, 0, 4.5, 0.5, 2, fm, outline=0.8, emis=True)
    box(flash, -0.5, -4.5, 0, 0.5, 4.5, 2, fm, outline=0.8, emis=True)
    box(flash, -0.5, -0.5, 4, 0.5, 0.5, 8, solid((255, 150, 50), 0.1), outline=1.0, emis=True)

    # --- поза: руки дотягиваются до автомата обратной кинематикой
    L1, L2 = 8, 10                                                  # плечо -> локоть -> центр кисти
    targets = {"R": v_add(rifle_origin, (0, -5.5, -3)),             # пистолетная рукоять
               "L": v_add(rifle_origin, (0, -4, 9))}                # передняя рукоять
    poles = {"R": (-0.8, -1, -0.3), "L": (0.6, -1, 0.1)}
    for nm, sx in (("R", -1), ("L", 1)):
        arm, fore = arms[nm]
        arm.rot, fore.rot = two_bone_ik((12 * sx, 20, 0), to_torso(targets[nm]), L1, L2,
                                        q_rot(inv_yaw, poles[nm]))

    add_animations(m, {p.name: p for p in hips.all()})
    return m


# ---------------------------------------------------------------- анимации
def add_animations(m, n):
    """Ключи: {(узел, 'rotation'|'translation'|'scale'): [(t, значение)]}; перемещения в px."""
    hips, torso, head, rifle, flash = n["Hips"], n["Torso"], n["Head"], n["Rifle"], n["MuzzleFlash"]

    def rot(node, *keys):
        return [(t, q_mul(node.rot, q)) for t, q in keys]

    # idle: дыхание и лёгкий поворот головы
    m.anims.append(("idle_loop", {
        (torso, "rotation"): rot(torso, (0, qx(0)), (1.0, qx(1.6)), (2.0, qx(0))),
        (head, "rotation"): rot(head, (0, qy(0)), (0.5, qy(-5)), (1.5, qy(5)), (2.0, qy(0))),
        (hips, "translation"): [(0, (0, 24, 0)), (1.0, (0, 23.7, 0)), (2.0, (0, 24, 0))],
    }, "LINEAR"))

    # walk: шаг с работой коленей
    T = 0.8
    ph = [0, 0.25, 0.5, 0.75, 1.0]
    swing = [28, 0, -28, 0, 28]
    knee_l = [8, 4, 6, 50, 8]
    knee_r = [6, 50, 8, 4, 6]
    walk = {
        (n["LegL"], "rotation"): [(p * T, qx(a)) for p, a in zip(ph, swing)],
        (n["LegR"], "rotation"): [(p * T, qx(-a)) for p, a in zip(ph, swing)],
        (n["ShinL"], "rotation"): [(p * T, qx(a)) for p, a in zip(ph, knee_l)],
        (n["ShinR"], "rotation"): [(p * T, qx(a)) for p, a in zip(ph, knee_r)],
        (hips, "translation"): [(p * T, (0, y, 0)) for p, y in zip(ph, [23.2, 24, 23.2, 24, 23.2])],
        (torso, "rotation"): rot(torso, *[(p * T, qy(a)) for p, a in zip(ph, [3, 0, -3, 0, 3])]),
    }
    m.anims.append(("walk_loop", walk, "LINEAR"))

    # shoot: отдача + вспышка
    kick = q_rot(rifle.rot, (0, 0, -2))
    m.anims.append(("shoot", {
        (rifle, "translation"): [(0, rifle.trans), (0.04, v_add(rifle.trans, kick)), (0.2, rifle.trans)],
        (torso, "rotation"): rot(torso, (0, qx(0)), (0.05, qx(-3)), (0.2, qx(0))),
        (flash, "scale"): [(0, (1, 1, 1)), (0.03, (1.3, 1.3, 1)), (0.07, (0.001,) * 3), (0.2, (0.001,) * 3)],
    }, "STEP_FLASH"))

    # death: падение на спину
    m.anims.append(("death", {
        (hips, "rotation"): [(0, qx(0)), (0.12, qx(8)), (0.55, qx(-86)), (0.68, qx(-80)), (0.85, qx(-86))],
        (hips, "translation"): [(0, (0, 24, 0)), (0.12, (0, 23, 0)), (0.55, (0, 13, -6)), (0.68, (0, 14.5, -6)),
                                (0.85, (0, 13, -6))],
        (head, "rotation"): rot(head, (0, qx(0)), (0.12, qx(18)), (0.55, qx(-22)), (0.85, qx(-12))),
        (torso, "rotation"): rot(torso, (0, qx(0)), (0.12, qx(10)), (0.55, qx(-6)), (0.85, qx(0))),
        (n["LegL"], "rotation"): [(0, qx(0)), (0.3, qx(-35)), (0.55, qx(10)), (0.85, qx(24))],
        (n["LegR"], "rotation"): [(0, qx(0)), (0.3, qx(-15)), (0.55, qx(22)), (0.85, qx(16))],
        (n["ShinL"], "rotation"): [(0, qx(0)), (0.3, qx(45)), (0.85, qx(6))],
        (n["ShinR"], "rotation"): [(0, qx(0)), (0.3, qx(25)), (0.85, qx(20))],
        (flash, "scale"): [(0, (0.001,) * 3), (0.85, (0.001,) * 3)],
    }, "LINEAR"))


# ---------------------------------------------------------------- запись GLB
def write_glb(m, path):
    m.atlas.finalize()
    blob = bytearray()
    views, accessors = [], []

    def add_view(data, target=None):
        while len(blob) % 4:
            blob.append(0)
        v = {"buffer": 0, "byteOffset": len(blob), "byteLength": len(data)}
        if target:
            v["target"] = target
        blob.extend(data)
        views.append(v)
        return len(views) - 1

    def add_acc(values, ctype, atype, target=None, minmax=False):
        flat = [c for v in values for c in (v if isinstance(v, (tuple, list)) else (v,))]
        fmt = {5126: "f", 5123: "H"}[ctype]
        acc = {"bufferView": add_view(struct.pack("<%d%s" % (len(flat), fmt), *flat), target),
               "componentType": ctype, "count": len(values), "type": atype}
        if minmax:
            if atype == "SCALAR":
                acc["min"], acc["max"] = [min(flat)], [max(flat)]
            else:
                k = len(values[0])
                acc["min"] = [min(v[c] for v in values) for c in range(k)]
                acc["max"] = [max(v[c] for v in values) for c in range(k)]
        accessors.append(acc)
        return len(accessors) - 1

    parts = list(m.root.all())
    index = {id(p): i for i, p in enumerate(parts)}
    nodes, meshes = [], []
    for p in parts:
        node = {"name": p.name, "translation": [c * PX for c in p.trans], "rotation": list(p.rot)}
        if p.scl != (1, 1, 1):
            node["scale"] = list(p.scl)
        if p.children:
            node["children"] = [index[id(c)] for c in p.children]
        if p.boxes:
            pos, nrm, uv, idx = [], [], [], []
            for b in p.boxes:
                for f in FACES:
                    ax, ay, w, h = b.rect[f]
                    base = len(pos)
                    uvs = ((ax, ay), (ax + w, ay), (ax + w, ay + h), (ax, ay + h))
                    for c, t in zip(b.corners(f), uvs):
                        pos.append(tuple((c[k] - p.pivot[k]) * PX for k in range(3)))
                        nrm.append(tuple(float(x) for x in NORMAL[f]))
                        uv.append((t[0] / ATLAS, t[1] / ATLAS))
                    idx += [base, base + 3, base + 2, base, base + 2, base + 1]   # CCW снаружи
            prim = {"attributes": {"POSITION": add_acc(pos, 5126, "VEC3", 34962, True),
                                   "NORMAL": add_acc(nrm, 5126, "VEC3", 34962),
                                   "TEXCOORD_0": add_acc(uv, 5126, "VEC2", 34962)},
                    "indices": add_acc(idx, 5123, "SCALAR", 34963), "material": 0}
            node["mesh"] = len(meshes)
            meshes.append({"name": p.name, "primitives": [prim]})
        nodes.append(node)

    # корневой узел-обёртка с именем модели
    nodes.append({"name": m.name, "children": [0]})

    animations = []
    for name, tracks, mode in m.anims:
        samplers, channels = [], []
        for (node, prop), keys in tracks.items():
            times = [float(t) for t, _ in keys]
            vals = [tuple(c * PX for c in v) if prop == "translation" else tuple(v) for _, v in keys]
            interp = "STEP" if (mode == "STEP_FLASH" and prop == "scale") else "LINEAR"
            samplers.append({"input": add_acc(times, 5126, "SCALAR", None, True),
                             "output": add_acc(vals, 5126, "VEC4" if prop == "rotation" else "VEC3"),
                             "interpolation": interp})
            channels.append({"sampler": len(samplers) - 1, "target": {"node": index[id(node)], "path": prop}})
        animations.append({"name": name, "samplers": samplers, "channels": channels})

    img_color = add_view(Atlas.png(m.atlas.rgb))
    img_emis = add_view(Atlas.png(m.atlas.emis))
    gltf = {
        "asset": {"version": "2.0", "generator": "generate_soldiers.py"},
        "scene": 0, "scenes": [{"name": m.name, "nodes": [len(nodes) - 1]}],
        "nodes": nodes, "meshes": meshes, "animations": animations,
        "materials": [{"name": "soldier_pixel",
                       "pbrMetallicRoughness": {"baseColorTexture": {"index": 0}, "metallicFactor": 0.0,
                                                "roughnessFactor": 0.95},
                       "emissiveTexture": {"index": 1}, "emissiveFactor": [1.0, 1.0, 1.0]}],
        "textures": [{"sampler": 0, "source": 0, "name": "albedo"}, {"sampler": 0, "source": 1, "name": "emission"}],
        "images": [{"bufferView": img_color, "mimeType": "image/png", "name": m.name + "_albedo"},
                   {"bufferView": img_emis, "mimeType": "image/png", "name": m.name + "_emission"}],
        # NEAREST без мипов — чёткие пиксели, как в Minecraft
        "samplers": [{"magFilter": 9728, "minFilter": 9728, "wrapS": 33071, "wrapT": 33071}],
        "accessors": accessors, "bufferViews": views,
    }
    while len(blob) % 4:
        blob.append(0)
    gltf["buffers"] = [{"byteLength": len(blob)}]
    js = json.dumps(gltf, separators=(",", ":")).encode()
    js += b" " * (-len(js) % 4)
    total = 12 + 8 + len(js) + 8 + len(blob)
    with open(path, "wb") as fh:
        fh.write(struct.pack("<4sII", b"glTF", 2, total))
        fh.write(struct.pack("<I4s", len(js), b"JSON") + js)
        fh.write(struct.pack("<I4s", len(blob), b"BIN\0") + bytes(blob))
    boxes = sum(len(p.boxes) for p in parts)
    print(f"{os.path.basename(path)}: {boxes} кубоидов, {boxes * 12} треугольников, {total // 1024} КБ")


if __name__ == "__main__":
    out = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
    for variant in ("gasmask", "balaclava"):
        write_glb(build_soldier(variant), os.path.join(out, f"soldier_{variant}.glb"))
