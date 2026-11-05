#!/usr/bin/env python3
"""物品像素图（游戏物品为 100 PPU，一格 16 像素；形状由不透明像素决定）。

用法：python3 art/sprites.py                → 写出 src/Decentralized/ModFiles/Textures/Items/*.png
     python3 art/sprites.py --preview DIR   → 另存放大 6 倍、桌面底色的预览

画法沿用 Probably Automated 对讲机图：轮廓统一用 #292234，光从左上来，颜色取自游戏物品图集的调色板。
"""
import os
import struct
import sys
import zlib

C = {
    'O': (0x29, 0x22, 0x34),  # 轮廓
    'k': (0x37, 0x35, 0x41),  # 最暗
    'd': (0x44, 0x46, 0x4C),  # 暗面
    'm': (0x5F, 0x68, 0x68),  # 中间
    'b': (0x88, 0x94, 0x8A),  # 亮面
    'l': (0xA8, 0xAE, 0xA5),  # 受光
    'h': (0xC4, 0xC4, 0xB0),  # 亮边
    'g': (0x53, 0x58, 0x5B),  # 散热片
    's': (0x27, 0x56, 0x54),  # 屏幕底
    'S': (0x67, 0xAA, 0xA5),  # 屏幕字
    'T': (0x8A, 0xBF, 0xBB),  # 屏幕高光
    'G': (0x5E, 0x9C, 0x4E),  # Probidia 绿
    'g2': (0x3E, 0x6B, 0x36),
    'R': (0xB0, 0x4A, 0x3E),  # PMD 红
    'r2': (0x7A, 0x33, 0x2E),
    'Y': (0xC8, 0xA4, 0x4A),  # 金手指
    'c': (0x2E, 0x2A, 0x2A),  # 焦黑
    'e': (0x6B, 0x4A, 0x3A),  # 焦痕
    'u': (0x4F, 0x86, 0xD0),  # 蓝
    'U': (0x2B, 0x4A, 0x82),  # 深蓝
    'v': (0x9B, 0x76, 0xD6),  # 紫
    'V': (0x5E, 0x45, 0x8F),  # 深紫
    'y': (0xF0, 0xCF, 0x64),  # 亮金
    'p': (0xC0, 0x7A, 0x4A),  # 铜
    'q': (0x8A, 0x4F, 0x34),  # 暗铜
    'w': (0xE6, 0xE2, 0xD0),  # 白
    'n': (0x1E, 0x1B, 0x25),  # 近黑
    'x': (0x4A, 0x4E, 0x58),  # 石墨亮边
    'z': (0x2D, 0x2F, 0x38),  # 石墨暗
    'P': (0xD8, 0xDA, 0xD0),  # 墨水屏
    'K': (0x3C, 0x3F, 0x48),  # 石墨机身
    'E': (0x5A, 0x5E, 0x6A),  # 机身受光边
}


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.px = [[None] * w for _ in range(h)]

    def put(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[y][x] = c

    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.put(x, y, c)

    def box(self, x0, y0, x1, y1, fill, outline='O'):
        self.rect(x0, y0, x1, y1, fill)
        for x in range(x0, x1 + 1):
            self.put(x, y0, outline); self.put(x, y1, outline)
        for y in range(y0, y1 + 1):
            self.put(x0, y, outline); self.put(x1, y, outline)

    def png(self, path, scale=1, bg=None):
        raw = bytearray()
        for row in self.px:
            line = bytearray()
            for c in row:
                r, g, b, a = (0, 0, 0, 0) if c is None else C[c] + (255,)
                if bg is not None:
                    r, g, b = (round(v * a / 255 + w * (255 - a) / 255) for v, w in zip((r, g, b), bg))
                    a = 255
                line += bytes((r, g, b, a)) * scale
            for _ in range(scale):
                raw.append(0); raw += line

        def chunk(tag, data):
            return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)

        data = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', self.w * scale, self.h * scale, 8, 6, 0, 0, 0)) \
            + chunk(b'IDAT', zlib.compress(bytes(raw), 9)) + chunk(b'IEND', b'')
        os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
        with open(path, 'wb') as f:
            f.write(data)


FONT = {  # 3×5 像素字（丝印用）
    'R': '110101110101101', 'Z': '111001010100111', 'X': '101101010101101', 'P': '110101110100100',
    'M': '101111111101101', 'D': '110101101101110', 'T': '111010010010010', 'i': '010000010010010',
    '0': '111101101101111', '1': '010110010010111', '2': '110001010100111', '3': '110001010001110',
    '4': '101101111001001', '5': '111100110001110', '6': '011100111101111', '7': '111001010010010',
    '8': '111101111101111', '9': '111101111001110',
}


def text(c, x, y, s, color, skew=False):
    """在画布上写 3×5 像素字；skew 为重新印过的效果：字一高一低、间距不匀。"""
    i = 0
    for ch in s:
        if ch == ' ':
            x += 2
            continue
        dy = (i % 2) if skew else 0
        for k, bit in enumerate(FONT[ch]):
            if bit == '1':
                c.put(x + k % 3, y + k // 3 + dy, color)
        x += 4 + (1 if skew and i % 3 == 2 else 0)
        i += 1


W, H = 48, 32
TOP0, TOP1 = 4, 22                              # 顶面行范围
L, R = 6, 45                                    # 顶面左右（左边是挡板）
FANS = ((17, 13), (34, 13))                     # 两个风扇中心


def outline(c):
    solid = lambda v: v is not None
    get = lambda x, y: c.px[y][x] if 0 <= x < c.w and 0 <= y < c.h else None
    edge = [(x, y) for y in range(c.h) for x in range(c.w)
            if solid(get(x, y)) and any(not solid(get(x + dx, y + dy)) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
    for x, y in edge:
        c.put(x, y, 'O')


def bracket(c):
    """左端金属挡板：带折边、网孔、三个接口（带高光边与内壁）、底部固定螺丝。"""
    c.rect(1, 2, 5, 27, 'b')
    c.rect(1, 2, 1, 27, 'h'); c.rect(2, 2, 2, 27, 'l'); c.rect(5, 2, 5, 27, 'd'); c.rect(4, 2, 4, 27, 'm')
    for y in (5, 11, 17):                       # 三个接口：外框亮边、内腔深色、中间一条触片
        c.rect(2, y, 4, y + 3, 'k')
        c.rect(2, y, 4, y, 'h'); c.put(2, y + 1, 'g'); c.put(4, y + 1, 'g')
        c.put(3, y + 2, 'd'); c.put(3, y + 3, 'g')
    for y in range(20, 27, 2):                  # 散热网孔
        c.put(2, y, 'k'); c.put(4, y, 'k')
        c.put(3, y + 1, 'k')
    c.put(3, 3, 'k'); c.put(3, 27, 'g')         # 顶部卡扣与底部螺丝
    c.put(2, 27, 'h'); c.put(4, 27, 'd')


def fan(c, cx, cy, accent, burnt=False, R=7):
    """风扇：深色风道里六片弯曲扇叶（前缘高光、叶面按受光渐变），外圈细边，轴心带品牌色点；光从左上来。"""
    import math
    k = 7.0 / R                                   # 按半径折算到 R=7 的画法
    for y in range(cy - R, cy + R + 1):
        for x in range(cx - R, cx + R + 1):
            dx, dy = x - cx, y - cy
            r = math.hypot(dx, dy) * k
            if r > 7.4:
                continue
            ang = (math.atan2(dy, dx) / (2 * math.pi)) % 1.0
            lit = (dx + dy) < 0
            if r >= 6.5:                         # 外圈细边
                c.put(x, y, 'l' if dx + dy < -3 else ('b' if dx + dy < 3 else 'g'))
            elif r >= 5.6:                       # 风道
                c.put(x, y, 'n')
            elif r <= 2.3:                       # 轴心
                c.put(x, y, 'h' if (dx + dy) < -1 else 'b' if dx + dy < 2 else 'm')
            else:
                t = (ang * 6 + (r - 3) * 0.16) % 1.0
                if t < 0.55:
                    c.put(x, y, 'h' if t < 0.09 else ('l' if lit else 'b') if t < 0.30 else ('b' if lit else 'm'))
                else:
                    c.put(x, y, 'n' if t < 0.85 else 'k')
    c.put(cx, cy, accent)
    c.put(cx - 1, cy - 1, 'w')


FAN_LAYOUT_SMALL = {1: ([(26, 13)], 9), 2: ([(17, 13), (34, 13)], 7), 3: ([(14, 13), (26, 13), (38, 13)], 5)}


def gpu_front(accent, accent_dark, burnt=False, tier=1, count=None, pin=None):
    """显卡正面（48×32，3×2 格），略微俯视：石墨色散热罩带斜切角与品牌色斜条，两个七叶风扇，
    右端露出鳍片，顶沿露出铜热管和 8 针供电口，前沿是带元件的 PCB 和逐根画出的金手指。每格中心都不透明。"""
    c = Canvas(W, H)
    bracket(c)
    # 散热罩底色：左上亮、右下暗的渐变，右上角切角
    for y in range(TOP0, TOP1 + 1):
        for x in range(L, R + 1):
            c.put(x, y, 'x' if y < TOP0 + 3 else 'd' if (x - L) < 4 else 'z')
    for x in range(L, R + 1):
        c.put(x, TOP0, 'h' if x < R - 5 else 'l'); c.put(x, TOP0 + 1, 'l' if x < R - 8 else 'b')
    for y in range(TOP0, TOP1 + 1):              # 左缘受光
        c.put(L, y, 'l'); c.put(L + 1, y, 'b')
    # 右端鳍片（竖向细条）
    for x in range(R - 4, R + 1):
        for y in range(TOP0 + 2, TOP1):
            c.put(x, y, 'g' if (x - (R - 4)) % 2 == 0 else 'k')
    c.rect(R - 5, TOP0 + 2, R - 5, TOP1 - 1, 'x')
    count = count or (1, 2, 3, 3)[tier]
    pin = (0, 1, 1, 2)[tier] if pin is None else pin
    fans, fr = FAN_LAYOUT_SMALL[count]
    # 风扇之间的筋条
    for a, b in zip(fans, fans[1:]):
        mx = (a[0] + b[0]) // 2
        for y in range(TOP0 + 3, TOP1 - 2):
            c.put(mx, y, 'k'); c.put(mx + 1, y, 'x')
    # 品牌色斜条（从前沿向左上延伸）与前沿色条
    for x in range(L + 1, R - 5):
        c.put(x, TOP1 - 1, accent if x % 9 else accent_dark)
        c.put(x, TOP1, accent_dark)
    for cx, cy in fans:
        fan(c, cx, cy, accent, R=fr)
        if count < 3:
            for dx, dy in ((-7, -7), (7, -7), (-7, 7), (7, 7)):   # 风扇四角的固定孔
                c.put(cx + dx, cy + dy, 'n' if dx + dy else 'k')
    # 顶沿：铜热管端头 + 8 针供电口（带针脚）
    for x0 in (L + 6, L + 8, L + 10, L + 12):
        c.put(x0, TOP0 - 1, 'p'); c.put(x0 + 1, TOP0 - 1, 'q')
    # 供电口：低端 6 针；中端、高端 8 针；旗舰两个 8 针（每针占两像素）
    for left, pins in (((L + 22, 6),) if pin == 0 else ((L + 18, 8),) if pin == 1 else ((L + 8, 8), (L + 25, 8))):
        for x in range(left, left + pins * 2):
            c.put(x, TOP0 - 1, 'n')
        for x in range(left, left + pins * 2, 2):
            c.put(x, TOP0 - 2, 'k')
        for x in range(left + 1, left + pins * 2, 2):
            c.put(x, TOP0 - 2, 'Y')
    # 前沿：PCB（绿）带元件，之后金手指
    for x in range(L, R + 1):
        c.put(x, TOP1 + 1, 'G' if x % 7 else 'g2'); c.put(x, TOP1 + 2, 'g2')
    for x0 in (L + 4, L + 11, L + 22, L + 29):
        c.put(x0, TOP1 + 1, 'b'); c.put(x0 + 1, TOP1 + 1, 'g')
    for x in range(L, R + 1):                    # 金手指：每一根带深色分隔
        if x in (L + 17, L + 18):
            continue
        c.put(x, TOP1 + 3, 'Y' if x % 2 == 0 else 'e'); c.put(x, TOP1 + 4, 'Y' if x % 2 == 0 else 'q')
        c.put(x, TOP1 + 5, 'e' if x % 2 == 0 else 'k')
    for x in range(L, R + 1):
        for y in range(TOP1 + 1, 28):
            if c.px[y][x] is None:
                c.put(x, y, 'k')
    if burnt:
        for x, y in [(12, 8), (13, 9), (18, 17), (35, 8), (36, 9), (40, 18), (24, 12), (25, 11), (19, 9), (33, 16)]:
            c.put(x, y, 'c')
        for x in range(L + 1, R - 5):
            c.put(x, TOP1 - 1, 'e')
        for cx, cy in fans:
            for dx, dy in ((0, 0), (1, 1), (-1, 1), (2, -2), (-2, -2)):
                c.put(cx + dx, cy + dy, 'c')
    outline(c)
    return c


def screw(c, x, y):
    c.put(x, y, 'b'); c.put(x + 1, y, 'g'); c.put(x, y + 1, 'g'); c.put(x + 1, y + 1, 'k')


def gpu_back(accent, accent_dark, burnt=False):
    """显卡背面（翻面后）：左端仍是挡板；拉丝金属背板，带通风槽、四角螺丝、品牌色斜条；
    中上是一块凹进去的丝印区（型号用叠加图写在这里），右下是带二维码和条码的序列号贴纸，贴纸边上一道封条。"""
    c = Canvas(W, H)
    bracket(c)
    for y in range(TOP0, 28):
        for x in range(L, R + 1):
            c.put(x, y, 'd' if (y % 3) else 'm' if x % 2 else 'd')
        c.put(R, y, 'k'); c.put(R - 1, y, 'z')
    for x in range(L, R + 1):
        c.put(x, TOP0, 'l'); c.put(x, TOP0 + 1, 'b')
    for y in range(TOP0, 28):
        c.put(L, y, 'b'); c.put(L + 1, y, 'x')
    # 丝印凹槽
    c.rect(9, 7, 43, 13, 'z')
    c.rect(9, 7, 43, 7, 'k'); c.rect(9, 7, 9, 13, 'k')
    c.rect(9, 13, 43, 13, 'x')
    # 品牌色斜条与前沿色条
    for x in range(L + 1, R - 1):
        c.put(x, 16, accent)
    c.put(L + 1, 17, accent_dark); c.put(R - 2, 17, accent_dark)
    # 通风槽（左下一组）
    for y in (19, 21, 23, 25):
        for x in range(L + 4, L + 14):
            c.put(x, y, 'k')
        for x in range(L + 4, L + 14):
            c.put(x, y + 1, 'x') if y + 1 < 27 else None
    # 四角螺丝
    for x, y in ((L + 2, TOP0 + 3), (R - 5, TOP0 + 3), (L + 2, 24), (R - 5, 24)):
        screw(c, x, y)
    # 序列号贴纸：左边二维码，右边条码与小字
    c.rect(22, 17, 41, 23, 'w')
    c.rect(22, 17, 41, 17, 'h'); c.rect(22, 23, 41, 23, 'l')
    qr = ['1110101', '1010011', '1011101', '0001010', '1101111', '0100101', '1110011']
    for j, row in enumerate(qr):
        for i, ch in enumerate(row):
            if ch == '1':
                c.put(23 + i, 17 + j - (1 if j == 6 else 0) * 0, 'n') if j < 6 else None
    for x in range(31, 41, 2):
        c.rect(x, 18, x, 20, 'n')
    for x in range(32, 41, 3):
        c.put(x, 18, 'w'); c.put(x, 19, 'w')
    for x in range(31, 41):
        if x % 3 != 0:
            c.put(x, 22, 'm')
    # 封条（红底白字）
    c.rect(43, 16, 44, 24, 'R')
    c.rect(43, 16, 43, 24, 'r2')
    for y in (18, 20, 22):
        c.put(44, y, 'w')
    # 背面金手指一行
    for x in range(L, R + 1):
        if x in (L + 17, L + 18):
            continue
        c.put(x, 27, 'Y' if x % 2 == 0 else 'e')
    if burnt:
        for x, y in [(14, 9), (15, 10), (30, 11), (36, 13), (20, 14), (26, 21), (12, 22)]:
            c.put(x, y, 'c')
    outline(c)
    return c


def overlay():
    return Canvas(W, H)


def ov_fan(kind, count=2):
    """风扇叠加：1 积灰（叶片上一层灰），2 叶片缺角（边缘缺一块并露出黑底）。"""
    c = overlay()
    for cx, cy in FAN_LAYOUT_SMALL[count][0]:
        if kind == 1:
            for dx, dy in ((-4, -2), (-2, -4), (3, -3), (4, 1), (-3, 3), (2, 4), (-5, 0), (0, 5), (1, -5), (-1, 2), (5, -1), (3, 2)):
                c.put(cx + dx, cy + dy, 'l')
            for dx, dy in ((-4, -1), (-1, -4), (4, -2), (2, 3), (-3, 4)):
                c.put(cx + dx, cy + dy, 'h')
        else:
            for dx, dy in ((4, -4), (5, -4), (5, -3), (4, -3), (3, -4), (5, -5), (6, -3)):
                c.put(cx + dx, cy + dy, 'k')
            c.put(cx - 5, cy + 4, 'k'); c.put(cx - 4, cy + 5, 'k'); c.put(cx - 5, cy + 5, 'k')
    return c


def ov_fingers(kind):
    """金手指叠加：1 发暗（触点变褐），2 氧化发绿。"""
    c = overlay()
    for x in range(L, R + 1):
        for y in (TOP1 + 3, TOP1 + 4):
            if x in (L + 17, L + 18):
                continue
            if kind == 1:
                c.put(x, y, 'e' if (x + y) % 3 else 'Y')
            else:
                c.put(x, y, 'g2' if (x + y) % 2 else 'G')
    return c


def ov_bracket(kind):
    """挡板叠加：1 划痕；2 接口数量不对（第三个接口被封成一块平板，只剩两个）。"""
    c = overlay()
    if kind == 1:
        for x, y in ((2, 9), (3, 10), (4, 11), (2, 15), (3, 15), (3, 16), (2, 20), (3, 21), (4, 22), (4, 25), (3, 25)):
            c.put(x, y, 'l' if (x + y) % 2 else 'd')
    else:
        c.rect(2, 17, 4, 20, 'b')
        c.rect(2, 17, 4, 17, 'h')
    return c


def ov_text(model, skew):
    """背面丝印叠加：写型号。skew 为重新印过的版本（字高低不齐）。"""
    c = overlay()
    text(c, 10, 8, model, 'l' if skew else 'h', skew)
    return c


def ov_sticker(kind):
    """贴纸叠加：1 手写序列号（条码被盖住、写了歪歪扭扭的字）；2 封条缺失（只剩撕过的残胶）。"""
    c = overlay()
    if kind == 1:
        c.rect(22, 17, 41, 23, 'w')
        c.rect(22, 17, 41, 17, 'h'); c.rect(22, 23, 41, 23, 'l')
        for x, y in ((24, 20), (25, 19), (26, 20), (27, 19), (29, 20), (30, 19), (30, 21), (32, 19), (33, 20), (34, 19), (35, 21), (36, 20), (37, 19), (38, 20), (39, 21), (24, 22), (26, 21), (28, 22), (31, 22), (35, 22)):
            c.put(x, y, 'O')
    else:
        c.rect(43, 16, 44, 24, 'd')
        for y in (16, 18, 20, 22, 24):
            c.put(44, y, 'm')
        c.put(43, 17, 'm'); c.put(43, 21, 'm'); c.put(43, 23, 'x')
    return c


MODELS = ['RTX 60', 'RTX 70', 'RTX 80', 'RTX 90', 'RX 70', 'RX 80', 'RX 800', 'RX 900']


def rig():
    """矿架（64×64，4×4 格）：和游戏里其他机器一样的略微俯视（3/4 视角）——顶面是上窄下宽的梯形，
    下面是正面，正面里四层显卡位，左下角电源；光从左上来，左亮右暗；轮廓统一深紫、削掉尖角、底下两只脚和一圈阴影。"""
    c = Canvas(64, 64)
    FL, FR, FY, DEP = 3, 60, 36, 31          # 正面左右边、顶面前沿所在行、顶面深度（顶面后沿在 y=6，中心行 y=8 落在顶面上）
    BOT = 60                                  # 正面底边
    inset = lambda v: v // 8                  # 顶面后部只略微收窄：原版机器的顶面也近似矩形，并且要让左右两格的中心落在顶面内

    # 顶面：后部受光、前部稍暗；左缘亮、右缘暗
    for v in range(DEP):
        y = FY - v
        x0, x1 = FL + inset(v), FR - inset(v)
        for x in range(x0, x1 + 1):
            c.put(x, y, 'l' if v >= 4 else 'b')
        c.put(x0, y, 'h'); c.put(x0 + 1, y, 'l')
        c.put(x1, y, 'd'); c.put(x1 - 1, y, 'm')
    for x in range(FL + inset(DEP - 1), FR - inset(DEP - 1) + 1):
        c.put(x, FY - DEP + 1, 'l')
    # 顶面上的散热格栅和一个小指示灯
    for v in range(4, 26, 3):
        for u in range(6, 34):
            c.put(FL + inset(v) + u, FY - v, 'g')
    c.put(FR - 9, FY - 8, 'G'); c.put(FR - 8, FY - 8, 'G')
    # 正面：中间色，左端受光、右端暗；顶部一条暗折线
    for y in range(FY + 1, BOT + 1):
        for x in range(FL, FR + 1):
            c.put(x, y, 'm')
        c.put(FL, y, 'b'); c.put(FL + 1, y, 'l' if y < FY + 5 else 'b')
        for dx in range(4):
            c.put(FR - dx, y, 'd' if dx else 'k')
    for x in range(FL, FR + 1):
        c.put(x, FY + 1, 'k')
        c.put(x, BOT, 'd')
    for x in range(FL + 1, FR):
        c.put(x, FY, 'h')
        c.put(x, FY + 2, 'b')
    for x in range(FL + 1, FR):
        c.put(x, BOT - 1, 'd')
    # 正面里的四层显卡位（深色槽，上沿亮、下沿暗，右端一颗状态灯）
    # 正面四层显卡位（深色槽，右端一颗状态灯）；间隔行用中间色，保证每格中心都不透明
    for i, y in enumerate((FY + 3, FY + 8, FY + 13, FY + 18)):
        for x in range(FL + 3, FR - 4):
            for dy in range(3):
                c.put(x, y + dy, 'k')
            c.put(x, y, 'g')
        for x in range(FL + 6, FR - 6, 3):
            c.put(x, y + 1, 'd')
        c.put(FR - 4, y + 1, 'G' if i % 2 == 0 else 'R')
    # 电源口（左下）
    for x in range(FL + 3, FL + 12):
        for dy in (-3, -2):
            c.put(x, BOT + dy, 'k')
    c.put(FL + 10, BOT - 2, 'S')
    for x in range(FL + 15, FR - 4, 2):
        c.put(x, BOT - 2, 'g')
    # 底部两只脚
    for x in (FL + 2, FL + 3, FR - 3, FR - 2):
        c.put(x, BOT + 1, 'O'); c.put(x, BOT + 2, 'O')

    # 轮廓：实体像素挨着透明的一圈描成轮廓色，再削掉尖角
    solid = lambda v: v is not None
    get = lambda x, y: c.px[y][x] if 0 <= x < c.w and 0 <= y < c.h else None
    edge = [(x, y) for y in range(c.h) for x in range(c.w)
            if solid(get(x, y)) and any(not solid(get(x + dx, y + dy)) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
    for x, y in edge:
        c.put(x, y, 'O')
    for y in range(c.h):
        for x in range(c.w):
            if get(x, y) == 'O' and sum(solid(get(x + dx, y + dy)) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))) <= 1:
                c.px[y][x] = None
    # 圆角：正面底部两角各削一个像素，让轮廓和原版机器一样不是尖方块
    for x, y in ((FL, BOT), (FR, BOT), (FL, BOT + 0), (FR, BOT + 0)):
        c.px[y][x] = None
        c.px[y][x + (1 if x == FL else -1)] = 'O'
    return c


def terminal():
    """交易终端（32×32，2×2 格），略微俯视：显示器是一个带顶面的盒子（正面是屏幕和 K 线），
    下面是键盘的顶面（梯形，露出按键），左亮右暗。每格中心都不透明。"""
    c = Canvas(32, 32)
    # 显示器：顶面（浅色窄条，略收窄）+ 正面
    for v in range(4):
        y = 3 + v
        x0, x1 = 4 + (3 - v) // 3, 27 - (3 - v) // 3
        for x in range(x0, x1 + 1):
            c.put(x, y, 'l' if v else 'h')
        c.put(x1, y, 'd')
    for y in range(7, 21):
        for x in range(4, 28):
            c.put(x, y, 'm')
        c.put(4, y, 'b'); c.put(27, y, 'd'); c.put(26, y, 'd')
    # 屏幕（深青底、K 线）
    for y in range(9, 19):
        for x in range(7, 25):
            c.put(x, y, 's')
    candles = [(9, 13, 16), (12, 11, 15), (15, 12, 14), (18, 9, 13), (21, 10, 12), (23, 9, 11)]
    for x, top, bot in candles:
        for y in range(top, bot + 1):
            c.put(x, y, 'S')
        c.put(x, top - 1 if top > 9 else top, 'T')
    c.put(7, 9, 'T'); c.put(8, 9, 'T')
    # 支架 + 底座（拉通到键盘，保证 y=24 的格子中心不透明）
    c.rect(13, 21, 18, 22, 'd')
    # 键盘顶面：梯形（后窄前宽），按键网格
    for v in range(7):
        y = 24 + (6 - v)                       # v=0 是前沿（y=30）
        w = v // 3
        x0, x1 = 2 + w, 29 - w
        for x in range(x0, x1 + 1):
            c.put(x, y, 'b')
        c.put(x0, y, 'h'); c.put(x1, y, 'd')
    for y in (25, 27, 29):
        for x in range(5, 27, 2):
            c.put(x, y, 'm')
    c.rect(10, 29, 21, 29, 'm')                # 空格键
    # 键盘前沿（正面薄边）
    for x in range(2, 30):
        c.put(x, 31, 'd')
    # 键盘与显示器之间的缝隙用桌面阴影色填实，避免 y=24 行出现透明格
    for y in range(22, 24):
        for x in range(4, 28):
            if c.px[y][x] is None:
                c.put(x, y, 'k')
    solid = lambda v: v is not None
    get = lambda x, y: c.px[y][x] if 0 <= x < c.w and 0 <= y < c.h else None
    edge = [(x, y) for y in range(c.h) for x in range(c.w)
            if solid(get(x, y)) and any(not solid(get(x + dx, y + dy)) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
    for x, y in edge:
        c.put(x, y, 'O')
    return c


def key(model):
    return model.replace(' ', '').lower()


def ui_bank():
    """银行图标（32×32）：带山花的石柱建筑，门前一枚金币。"""
    c = Canvas(32, 32)
    for k in range(0, 14):                          # 山花（三角）
        c.rect(16 - k, 4 + k // 2, 16 + k, 4 + k // 2, 'h' if k < 3 else 'l')
    c.rect(2, 11, 29, 12, 'b'); c.rect(2, 13, 29, 13, 'd')   # 檐口
    for x0 in (4, 10, 16, 22):                      # 四根柱子
        c.rect(x0, 14, x0 + 3, 24, 'l')
        c.rect(x0, 14, x0, 24, 'h'); c.rect(x0 + 3, 14, x0 + 3, 24, 'd')
        c.rect(x0 - 1, 14, x0 + 4, 14, 'b'); c.rect(x0 - 1, 24, x0 + 4, 24, 'b')
    c.rect(1, 25, 30, 26, 'b'); c.rect(0, 27, 31, 28, 'm'); c.rect(0, 29, 31, 29, 'd')
    c.rect(14, 8, 17, 9, 'y'); c.put(15, 8, 'w')     # 山花里的小徽标
    for yy in range(19, 27):                         # 金币
        for xx in range(23, 31):
            d = (xx - 26.5) ** 2 + (yy - 22.5) ** 2
            if d <= 14:
                c.put(xx, yy, 'e' if d > 9 else 'y')
    c.put(25, 21, 'w'); c.put(26, 21, 'w'); c.put(26, 22, 'y')
    outline(c)
    return c


def ui_poinbase():
    """Poinbase 图标（32×32）：蓝色盾牌，中间一枚金色 P 币，两侧一上一下的箭头。"""
    c = Canvas(32, 32)
    for y in range(3, 29):                          # 盾牌
        half = 12 if y < 16 else max(0, 12 - (y - 16) * 12 // 12)
        for x in range(16 - half, 16 + half + 1):
            c.put(x, y, 'u' if x < 16 else 'U')
        c.put(16 - half, y, 'h') if half else None
    for x in range(4, 29):
        c.put(x, 3, 'h'); c.put(x, 4, 'l')
    for y in range(7, 21):                          # 币
        for x in range(9, 24):
            d = (x - 16) ** 2 + (y - 13.5) ** 2
            if d <= 36:
                c.put(x, y, 'e' if d > 28 else 'y')
    c.put(12, 9, 'w'); c.put(13, 8, 'w'); c.put(12, 10, 'w')
    c.rect(13, 9, 14, 18, 'U'); c.rect(15, 9, 18, 10, 'U'); c.rect(18, 11, 19, 13, 'U'); c.rect(15, 14, 18, 15, 'U')   # P
    for k in range(4):                              # 下方两个箭头：买入、卖出
        c.put(9 + k, 24 - k, 'w') if k < 3 else None
    for x in range(8, 13): c.put(x, 22, 'w')
    for x in range(19, 24): c.put(x, 22, 'w')
    c.put(19, 23, 'w'); c.put(20, 24, 'w'); c.put(23, 23, 'w'); c.put(22, 24, 'w')
    outline(c)
    return c


def ui_web3():
    """Web3 图标（32×32）：六边形节点网络，紫色底。"""
    c = Canvas(32, 32)
    nodes = [(16, 7), (7, 14), (25, 14), (11, 24), (21, 24), (16, 16)]
    edges = [(0, 1), (0, 2), (1, 3), (2, 4), (3, 4), (5, 0), (5, 1), (5, 2), (5, 3), (5, 4)]
    for (a, b) in edges:
        (x0, y0), (x1, y1) = nodes[a], nodes[b]
        n = max(abs(x1 - x0), abs(y1 - y0))
        for i in range(n + 1):
            px, py = x0 + (x1 - x0) * i // n, y0 + (y1 - y0) * i // n
            c.put(px, py, 'v'); c.put(px + 1, py, 'v'); c.put(px, py + 1, 'v')
    for i, (x, y) in enumerate(nodes):
        r = 3 if i == 5 else 2
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if dx * dx + dy * dy <= r * r + 1:
                    c.put(x + dx, y + dy, 'V' if (dx + dy) > 0 else 'h' if i != 5 else 'y')
        c.put(x, y, 'w')
    outline(c)
    return c


def guide():
    """鉴定手册（16×16，一格）：竖放的小册子，封面带品牌色条，书脊在左。"""
    c = Canvas(16, 16)
    c.box(2, 1, 13, 14, 'b')
    c.rect(3, 2, 4, 13, 'd')                       # 书脊
    c.rect(5, 2, 12, 2, 'h')
    c.rect(6, 4, 11, 5, 'G')                       # 封面色块
    c.rect(6, 4, 11, 4, 'g2')
    for y in (7, 9, 11):                           # 文字行
        c.rect(6, y, 11 if y != 11 else 9, y, 'm')
    c.put(12, 13, 'd')
    outline(c)
    return c


def asic(model):
    """RatDigg 矿机（48×32，3×2 格）：宽扁的金属机箱，正面两个大风扇，顶上一条铭牌带三个指示灯和型号；
    指示灯与风扇轴心的颜色区分档次（M1 绿、M2 蓝、M3 金）。光从左上来。"""
    c = Canvas(48, 32)
    accent = {'M1': 'G', 'M2': 'u', 'M3': 'y'}[model]
    c.rect(3, 1, 44, 5, 'l')                       # 顶面
    c.rect(3, 1, 44, 1, 'h')
    c.rect(43, 2, 44, 5, 'b')
    c.rect(2, 6, 45, 29, 'm')                      # 正面
    c.rect(2, 6, 2, 29, 'b'); c.rect(45, 6, 45, 29, 'd'); c.rect(2, 29, 45, 29, 'd')
    c.rect(4, 7, 43, 10, 'n')                      # 铭牌带
    for x in (6, 9, 12):
        c.put(x, 8, accent); c.put(x, 9, accent if x == 6 else 'k')
    text(c, 32, 7, model.replace('M', 'M'), 'w')
    for fx in (14, 34):                            # 风扇：方框风道 + 圆扇
        c.rect(fx - 9, 12, fx + 9, 28, 'n')
        c.rect(fx - 9, 12, fx + 9, 12, 'g'); c.rect(fx - 9, 12, fx - 9, 28, 'g')
        fan(c, fx, 20, accent, R=8)
    c.rect(22, 12, 26, 28, 'd')                    # 两扇之间的散热栅
    for y in range(13, 28, 2):
        c.rect(23, y, 25, y, 'k')
    c.rect(4, 30, 9, 31, 'k'); c.rect(38, 30, 43, 31, 'k')   # 脚垫
    c.rect(10, 30, 37, 30, 'd')
    outline(c)
    return c


def hw_wallet():
    """PLEDGER 硬件钱包（16×16，一格）：竖放的石墨卡片，大块浅色墨水屏，右侧一个侧键。"""
    c = Canvas(16, 16)
    c.rect(3, 1, 12, 14, 'K')
    for (x, y) in ((3, 1), (12, 1), (3, 14), (12, 14)):
        c.put(x, y, None)
    c.rect(4, 2, 4, 13, 'E'); c.rect(4, 2, 11, 2, 'E')        # 左上受光
    c.rect(11, 3, 11, 13, 'z'); c.rect(5, 13, 11, 13, 'z')     # 右下背光
    c.rect(5, 3, 10, 11, 'P')                                  # 墨水屏
    c.rect(6, 5, 9, 5, 'm'); c.rect(6, 7, 8, 7, 'm'); c.rect(6, 9, 9, 9, 'm')   # 屏上的字
    c.rect(13, 5, 13, 7, 'x')                                  # 侧键
    outline(c)
    return c


def key_drive():
    """Tumbler 的密钥盘（16×16，一格）：斜放的旧式数据棒，深紫机身、金属接口、一枚红色指示灯和手写标签。"""
    c = Canvas(16, 16)
    c.rect(3, 4, 12, 11, 'V')                                  # 机身
    c.rect(3, 4, 12, 4, 'v'); c.rect(3, 4, 3, 11, 'v')         # 受光边
    c.rect(4, 11, 12, 11, 'n'); c.rect(12, 5, 12, 11, 'n')     # 背光边
    c.rect(13, 6, 15, 9, 'l'); c.rect(13, 9, 15, 9, 'm')       # 金属接口
    c.put(14, 7, 'k'); c.put(14, 8, 'k')
    c.rect(5, 6, 9, 9, 'w')                                    # 手写标签
    c.rect(6, 7, 8, 7, 'k'); c.rect(6, 8, 7, 8, 'k')
    c.put(11, 6, 'R')                                          # 指示灯
    outline(c)
    return c


SPRITES = {
    'gpu_guide': guide,
    'gpu_burnt_f': lambda: gpu_front('e', 'c', burnt=True),
    'gpu_burnt_b': lambda: gpu_back('e', 'c', burnt=True),
    'gpu_probidia_b': lambda: gpu_back('G', 'g2'),
    'gpu_pmd_b': lambda: gpu_back('R', 'r2'),
    'rig': rig,
    'terminal': terminal,
    'hw_wallet': hw_wallet,
    'key_drive': key_drive,
    'asic_m1': lambda: asic('M1'),
    'asic_m2': lambda: asic('M2'),
    'asic_m3': lambda: asic('M3'),
}
for _tier in range(4):
    SPRITES['gpu_probidia_f_t%d' % _tier] = (lambda t=_tier: gpu_front('G', 'g2', tier=t))
    SPRITES['gpu_pmd_f_t%d' % _tier] = (lambda t=_tier: gpu_front('R', 'r2', tier=t))
for _pin in range(3):
    for _n in range(1, 4):
        SPRITES['gpu_probidia_f_p%dn%d' % (_pin, _n)] = (lambda p=_pin, n=_n: gpu_front('G', 'g2', pin=p, count=n))
        SPRITES['gpu_pmd_f_p%dn%d' % (_pin, _n)] = (lambda p=_pin, n=_n: gpu_front('R', 'r2', pin=p, count=n))

def hw_button(symbol):
    """硬件钱包的物理按键（24×24）：石墨外圈、左上受光的金属环，中间凹下去的按键面，刻着 ✓ 或 ✕。"""
    c = Canvas(24, 24)
    for y in range(24):
        for x in range(24):
            d = ((x - 11.5) ** 2 + (y - 11.5) ** 2) ** 0.5
            if d <= 11.5:
                c.put(x, y, 'x' if x + y < 23 else 'z')          # 外圈：左上亮、右下暗
            if d <= 9.5:
                c.put(x, y, 'k' if x + y < 23 else 'n')          # 凹槽：反过来，左上暗、右下亮一点
            if d <= 8.5:
                c.put(x, y, 'z')                                  # 按键面
    mark = 'G' if symbol == 'ok' else 'R'
    if symbol == 'ok':
        pts = [(7, 12), (8, 13), (9, 14), (10, 15), (11, 14), (12, 13), (13, 12), (14, 11), (15, 10), (16, 9)]
    else:
        pts = [(8 + k, 8 + k) for k in range(8)] + [(15 - k, 8 + k) for k in range(8)]
    for (x, y) in pts:
        c.put(x, y, mark); c.put(x, y + 1, mark)
    outline(c)
    return c


def rounded(size, r, fill, light=None, dark=None):
    """圆角矩形（九宫格用）：圆角半径 r，外轮廓 O；可选左上一圈受光、右下一圈背光。"""
    c = Canvas(size, size)
    def inside(x, y, rr):
        cx = min(max(x, rr), size - 1 - rr); cy = min(max(y, rr), size - 1 - rr)
        return (x - cx) ** 2 + (y - cy) ** 2 <= rr * rr + rr * 0.6
    for y in range(size):
        for x in range(size):
            if inside(x, y, r):
                c.put(x, y, fill)
    if light or dark:
        for y in range(size):
            for x in range(size):
                if c.px[y][x] is None:
                    continue
                edge = any(not (0 <= x + dx < size and 0 <= y + dy < size) or c.px[y + dy][x + dx] is None for dx, dy in ((2, 0), (-2, 0), (0, 2), (0, -2)))
                if edge:
                    c.put(x, y, light if x + y < size - 1 else dark)
    outline(c)
    return c


def hw_selected():
    """PLEDGER 屏上选中的按钮（16×16，九宫格边 5）：墨水屏白底、1 像素黑框，四角向外加粗的取景框角标（准星）。"""
    c = Canvas(16, 16)
    c.rect(0, 0, 15, 15, 'P')
    for i in range(16):
        c.put(i, 1, 'n'); c.put(i, 14, 'n'); c.put(1, i, 'n'); c.put(14, i, 'n')
    for (x, y, dx, dy) in ((0, 0, 1, 1), (15, 0, -1, 1), (0, 15, 1, -1), (15, 15, -1, -1)):
        for k in range(4):
            c.put(x + dx * k, y, 'n'); c.put(x, y + dy * k, 'n')
    return c


def hw_groove():
    """PLEDGER 墨水屏输入框凹槽（12×12，九宫格边 2）：浅色底、1 像素深墨边。"""
    c = Canvas(12, 12)
    c.rect(0, 0, 11, 11, 'n')
    c.rect(1, 1, 10, 10, 'P')
    return c


UI_ICONS = {'ui_bank': ui_bank, 'ui_poinbase': ui_poinbase, 'ui_web3': ui_web3}
UI_ICONS['ui_hw_shell'] = lambda: rounded(32, 7, 'K', 'E', 'z')
UI_ICONS['ui_hw_bezel'] = lambda: rounded(16, 4, 'n')
UI_ICONS['ui_hw_paper'] = lambda: rounded(12, 2, 'P')
UI_ICONS['ui_hw_sel'] = hw_selected
def hw_key():
    """触控键底图（九宫格，白色，可着色）：圆角 3 像素，没有描边，颜色由界面按状态着色。"""
    size, r = 12, 3
    c = Canvas(size, size)
    for y in range(size):
        for x in range(size):
            cx = min(max(x, r), size - 1 - r); cy = min(max(y, r), size - 1 - r)
            if (x - cx) ** 2 + (y - cy) ** 2 <= r * r + r * 0.6:
                c.put(x, y, 'w')
    return c


UI_ICONS['ui_hw_key'] = hw_key
UI_ICONS['ui_hw_groove'] = hw_groove
UI_ICONS['ui_hw_ok'] = lambda: hw_button('ok')
UI_ICONS['ui_hw_no'] = lambda: hw_button('no')

# 叠加图：和底图同尺寸，位置 (0,0) 对齐，只画变化的像素，不参与判格
OVERLAYS = {
    'gpu_ov_fin1': lambda: ov_fingers(1), 'gpu_ov_fin2': lambda: ov_fingers(2),
    'gpu_ov_brk1': lambda: ov_bracket(1),
    'gpu_ov_stk2': lambda: ov_sticker(2),
}
for _n in (1, 2, 3):
    OVERLAYS['gpu_ov_fan1_%d' % _n] = (lambda n=_n: ov_fan(1, n))
    OVERLAYS['gpu_ov_fan2_%d' % _n] = (lambda n=_n: ov_fan(2, n))
for _m in MODELS:
    OVERLAYS['gpu_ov_txt_' + key(_m)] = (lambda m=_m: ov_text(m, False))
    OVERLAYS['gpu_ov_txs_' + key(_m)] = (lambda m=_m: ov_text(m, True))


def check_cells(canvas, name):
    """游戏按 mod 物品图每格的中心像素是否不透明来判断占格（行从图的下沿往上数）。有空格就报错。"""
    cols, rows = canvas.w // 16, canvas.h // 16
    bad = [(q, r) for r in range(rows) for q in range(cols) if canvas.px[canvas.h - 1 - (r * 16 + 8)][q * 16 + 8] is None]
    if bad:
        raise SystemExit(f"{name}: 这些格子中心像素透明（游戏会当成空格，别的物品会叠上来）：{bad}")


if __name__ == '__main__':
    here = os.path.dirname(os.path.abspath(__file__))
    if len(sys.argv) == 3 and sys.argv[1] == '--preview':
        for name, make in SPRITES.items():
            canvas = make()
            check_cells(canvas, name)
            canvas.png(os.path.join(sys.argv[2], name + '.png'), scale=6, bg=(0xA0, 0xA8, 0x9C))
        # 预览：把各种叠加图叠在底图上
        def stack(base, *ovs):
            b = SPRITES[base]()
            for o in ovs:
                oc = OVERLAYS[o]()
                for y in range(oc.h):
                    for x in range(oc.w):
                        if oc.px[y][x] is not None:
                            b.px[y][x] = oc.px[y][x]
            return b
        for name, make in UI_ICONS.items():
            make().png(os.path.join(sys.argv[2], name + '.png'), scale=8, bg=(0x2a, 0x2c, 0x34))
        stack('gpu_probidia_f_t1', 'gpu_ov_fan2_2', 'gpu_ov_fin1', 'gpu_ov_brk1').png(os.path.join(sys.argv[2], 'prev_front_worn.png'), scale=6, bg=(0xA0, 0xA8, 0x9C))
        print(sys.argv[2])
    else:
        out = os.path.normpath(os.path.join(here, '..', 'src', 'Decentralized', 'ModFiles', 'Textures', 'Items'))
        for f in os.listdir(out):                       # 清掉旧的显卡图，避免留下不再使用的文件
            if f.startswith('gpu_'):
                os.remove(os.path.join(out, f))
        for name, make in SPRITES.items():
            canvas = make()
            check_cells(canvas, name)
            canvas.png(os.path.join(out, name + '.png'))
        for name, make in OVERLAYS.items():
            make().png(os.path.join(out, name + '.png'))
        for name, make in UI_ICONS.items():
            make().png(os.path.join(out, name + '.png'))
        import big, big_cases
        for name, make in big.BIG.items():
            make().png(os.path.join(out, name + '.png'))
        for name, make in big_cases.CASES.items():
            if name.startswith('gpu_big_ov_fan_'):
                continue
            if not name.startswith('gpu_big_ov_print_') and name in ('gpu_big_ov_fin_dim', 'gpu_big_ov_fin_oxide', 'gpu_big_ov_brk_scratch', 'gpu_big_ov_seal_missing'):
                make().png(os.path.join(out, name + '.png'))
        # 风扇叠加按风扇数各出一套：叠加模块里的风扇位置是模块级变量，换成对应布局后再画
        for count, (centres, radius) in big.FAN_LAYOUT.items():
            big_cases.FANS = list(centres) if len(centres) > 1 else [centres[0], centres[0]]
            big_cases.FR = radius
            for fname, short in (('fan_dust1', 'fan_dust1'), ('fan_dust2', 'fan_dust2'), ('fan_chip', 'fan_chip')):
                getattr(big_cases, fname)().png(os.path.join(out, 'gpu_big_ov_%s_%d.png' % (short, count)))
        for m in MODELS:
            for style in ('normal', 'skew', 'stamp'):
                big_cases.print_overlay(m, style).png(os.path.join(out, 'gpu_big_ov_print_%s_%s.png' % (style, key(m))))
        print(out)
