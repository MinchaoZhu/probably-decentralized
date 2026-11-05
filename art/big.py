#!/usr/bin/env python3
"""显卡检视大图（192×128）：放大镜检视台里用的细节图，不是小图的放大版。
正面：七叶风扇、鳍片、热管、供电口、PCB 元件与逐根金手指；背面：拉丝背板、丝印凹槽、二维码与条码贴纸、封条。
叠加图与底图同尺寸、坐标一致，只画变化的像素。"""
import math
import random
from sprites import Canvas, C, key, MODELS

BW, BH = 192, 128
# 风扇布局（按风扇数）：中心与半径；FANS / FR 保留为双风扇的值，给叠加图模块当默认
FAN_LAYOUT = {1: ([(105, 55)], 34), 2: ([(68, 51), (142, 51)], 30), 3: ([(52, 51), (96, 51), (140, 51)], 21)}
FANS, FR = FAN_LAYOUT[2][0], FAN_LAYOUT[2][1]
SH0, SH1 = (24, 14), (186, 96)  # 散热罩 左上 / 右下
FING_Y0, FING_Y1 = 109, 122
NOTCH_X = 112

# 5×7 像素字（丝印与品牌字）
F57 = {
    'R': ['11110', '10001', '10001', '11110', '10100', '10010', '10001'],
    'Z': ['11111', '00001', '00010', '00100', '01000', '10000', '11111'],
    'X': ['10001', '10001', '01010', '00100', '01010', '10001', '10001'],
    'P': ['11110', '10001', '10001', '11110', '10000', '10000', '10000'],
    'M': ['10001', '11011', '10101', '10101', '10001', '10001', '10001'],
    'D': ['11110', '10001', '10001', '10001', '10001', '10001', '11110'],
    'T': ['11111', '00100', '00100', '00100', '00100', '00100', '00100'],
    'i': ['00100', '00000', '01100', '00100', '00100', '00100', '01110'],
    'O': ['01110', '10001', '10001', '10001', '10001', '10001', '01110'],
    'B': ['11110', '10001', '10001', '11110', '10001', '10001', '11110'],
    'I': ['01110', '00100', '00100', '00100', '00100', '00100', '01110'],
    'A': ['01110', '10001', '10001', '11111', '10001', '10001', '10001'],
    '0': ['01110', '10001', '10011', '10101', '11001', '10001', '01110'],
    '1': ['00100', '01100', '00100', '00100', '00100', '00100', '01110'],
    '2': ['01110', '10001', '00001', '00110', '01000', '10000', '11111'],
    '3': ['11110', '00001', '00001', '01110', '00001', '00001', '11110'],
    '4': ['00010', '00110', '01010', '10010', '11111', '00010', '00010'],
    '5': ['11111', '10000', '11110', '00001', '00001', '10001', '01110'],
    '6': ['00110', '01000', '10000', '11110', '10001', '10001', '01110'],
    '7': ['11111', '00001', '00010', '00100', '01000', '01000', '01000'],
    '8': ['01110', '10001', '10001', '01110', '10001', '10001', '01110'],
    '9': ['01110', '10001', '10001', '01111', '00001', '00010', '01100'],
}


def text57(c, x, y, s, color, scale=1, skew=False, gap=1):
    i = 0
    for ch in s:
        if ch == ' ':
            x += 3 * scale
            continue
        dy = (i % 2) * scale if skew else 0
        for r, row in enumerate(F57[ch]):
            for k, bit in enumerate(row):
                if bit == '1':
                    c.rect(x + k * scale, y + r * scale + dy, x + k * scale + scale - 1, y + r * scale + scale - 1 + dy, color)
        x += (5 + gap) * scale + (scale if skew and i % 3 == 2 else 0)
        i += 1


def outline(c, col='O'):
    solid = lambda v: v is not None
    get = lambda x, y: c.px[y][x] if 0 <= x < c.w and 0 <= y < c.h else None
    edge = [(x, y) for y in range(c.h) for x in range(c.w)
            if solid(get(x, y)) and any(not solid(get(x + dx, y + dy)) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
    for x, y in edge:
        c.put(x, y, col)


def bracket(c):
    """左端 I/O 挡板：折边、三个接口（HDMI 与两个 DP，内腔与触片）、散热网孔、底部螺丝。"""
    c.rect(2, 8, 22, 112, 'b')
    c.rect(2, 8, 4, 112, 'l'); c.rect(2, 8, 2, 112, 'h')           # 受光折边
    c.rect(19, 8, 22, 112, 'd'); c.rect(21, 8, 22, 112, 'g')       # 背光折边
    for x in range(5, 19):                                         # 拉丝
        for y in range(9, 112, 2):
            c.put(x, y, 'b' if (x + y) % 5 else 'l')
    for i, y in enumerate((16, 36, 56)):                           # 三个接口
        c.rect(6, y, 17, y + 13, 'k')
        c.rect(6, y, 17, y, 'g'); c.rect(6, y, 6, y + 13, 'g')
        c.rect(7, y + 1, 16, y + 12, 'n')
        if i == 0:                                                 # HDMI：两侧斜角
            for k in range(3):
                c.put(7 + k, y + 12 - k, 'k'); c.put(16 - k, y + 12 - k, 'k')
            c.rect(9, y + 4, 14, y + 6, 'z')
        else:                                                      # DP：一边切角
            c.put(16, y + 12, 'k'); c.put(15, y + 12, 'k'); c.put(16, y + 11, 'k')
            c.rect(9, y + 4, 14, y + 5, 'z')
        for x in range(8, 16, 2):
            c.put(x, y + 9, 'Y')
    for y in range(78, 104, 4):                                    # 散热网孔
        for x in range(7, 17, 3):
            c.rect(x, y, x + 1, y + 2, 'k')
            c.put(x, y, 'x')
    c.rect(9, 106, 14, 110, 'm'); c.rect(10, 107, 13, 109, 'g')    # 底部螺丝
    c.put(11, 108, 'k'); c.put(12, 108, 'k'); c.put(11, 107, 'b')
    c.rect(9, 10, 14, 12, 'k')


def fan(c, cx, cy, accent, accent_dark, R=None):
    """风扇：九片弯曲扇叶（前缘高光、叶面渐变、叶尖暗边），外圈带四个固定螺丝，轴心带品牌圆标。"""
    R = R or FR
    for y in range(cy - R - 1, cy + R + 2):
        for x in range(cx - R - 1, cx + R + 2):
            dx, dy = x - cx, y - cy
            r = math.hypot(dx, dy)
            if r > R + 0.5:
                continue
            ang = (math.atan2(dy, dx) / (2 * math.pi)) % 1.0
            light = -(dx + dy) / (1.4 * R)                     # 左上为正
            if r >= R - 3.2:                                   # 外圈
                c.put(x, y, 'h' if light > 0.5 else 'l' if light > 0.1 else 'b' if light > -0.4 else 'm')
            elif r >= R - 5.5:                                 # 风道阴影
                c.put(x, y, 'n' if light < 0.2 else 'k')
            elif r <= 6.5:                                     # 轴心
                if r <= 4.3:
                    c.put(x, y, accent if light < 0.3 else accent_dark if r > 3 else 'w')
                else:
                    c.put(x, y, 'h' if light > 0.2 else 'm' if light > -0.3 else 'd')
            else:
                t = (ang * 9 + (r - 7) * 0.060) % 1.0
                if t < 0.62:
                    if t < 0.06:
                        col = 'h'
                    elif t < 0.40:
                        col = 'l' if light > 0.25 else 'b' if light > -0.2 else 'm'
                    else:
                        col = 'b' if light > 0.2 else 'm' if light > -0.3 else 'g'
                    if r > R - 8:
                        col = 'g' if col in ('m', 'g') else 'm'
                    c.put(x, y, col)
                else:
                    c.put(x, y, 'n' if t < 0.9 else 'k')
    for k in range(4):                                         # 外圈固定螺丝
        a = math.pi / 4 + k * math.pi / 2
        sx, sy = int(cx + math.cos(a) * (R - 1.5)), int(cy + math.sin(a) * (R - 1.5))
        c.rect(sx - 1, sy - 1, sx + 1, sy + 1, 'k')
        c.put(sx, sy, 'g')


def gpu_front(accent, accent_dark, brand_text, tier=1, burnt=False, count=None, pin=None):
    """tier 决定实物特征：风扇数（1/2/3/3）与供电口（6 针 / 8 针 / 8 针 / 双 8 针）。"""
    count = count or (1, 2, 3, 3)[tier]
    pin = (0, 1, 1, 2)[tier] if pin is None else pin
    fans, R = FAN_LAYOUT[count]
    c = Canvas(BW, BH)
    bracket(c)
    x0, y0 = SH0
    x1, y1 = SH1
    # 散热罩：石墨底，顶部受光亮边，左缘高光，右下阴影；右上角切斜
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if x > x1 - 8 and y < y0 + (x - (x1 - 8)):          # 右上切角
                continue
            base = 'z'
            if y < y0 + 3: base = 'h' if x < x1 - 40 else 'l'
            elif y < y0 + 6: base = 'l' if x < x1 - 40 else 'b'
            elif y < y0 + 9: base = 'x'
            elif y > y1 - 4: base = 'n'
            c.put(x, y, base)
    for y in range(y0 + 3, y1):                                # 左缘
        c.put(x0, y, 'l'); c.put(x0 + 1, y, 'b'); c.put(x0 + 2, y, 'x')
    # 右端鳍片
    for x in range(x1 - 18, x1 + 1):
        for y in range(y0 + 10, y1 - 4):
            if x > x1 - 8 and y < y0 + (x - (x1 - 8)) + 2:
                continue
            c.put(x, y, 'x' if (x - (x1 - 18)) % 3 == 0 else 'g' if (x - (x1 - 18)) % 3 == 1 else 'n')
    c.rect(x1 - 20, y0 + 10, x1 - 19, y1 - 4, 'x')
    # 风扇之间的中筋
    for a, b in zip(fans, fans[1:]):
        mx = (a[0] + b[0]) // 2 + 1
        c.rect(mx - 1, y0 + 8, mx, y1 - 5, 'k'); c.rect(mx + 1, y0 + 8, mx + 1, y1 - 5, 'x')
    for cx, cy in fans:
        fan(c, cx, cy, accent, accent_dark, R)
    # 前沿斜切装饰条：品牌色 + 品牌字
    for x in range(x0 + 4, x1 - 20):
        c.rect(x, y1 - 5, x, y1 - 3, accent if x % 11 else accent_dark)
    text57(c, 30 if count == 1 else 112, 84, brand_text, 'h')   # 单风扇的大风扇占了右边，品牌字改放左下
    # 顶沿：四根铜热管端头与 8 针供电口
    for i in range(4):
        x = 38 + i * 9
        c.rect(x, 7, x + 6, y0 - 1, 'p'); c.rect(x, 7, x + 1, y0 - 1, 'h' if i == 0 else 'p')
        c.rect(x + 5, 7, x + 6, y0 - 1, 'q'); c.rect(x, 7, x + 6, 7, 'q')
    # 供电口：低端 6 针；中端、高端 8 针；旗舰两个 8 针
    for left, pins in ((((118, 6),) if pin == 0 else ((112, 8),) if pin == 1 else ((78, 8), (126, 8)))):
        right = left + pins * 5 + 2
        c.rect(left, 5, right, y0 - 1, 'n'); c.rect(left, 5, right, 6, 'z')
        for i in range(pins):
            c.rect(left + 2 + i * 5, 7, left + 4 + i * 5, 9, 'k'); c.put(left + 3 + i * 5, 8, 'Y')
    # PCB：阻焊绿、边缘高光、元件与走线
    for y in range(y1 + 1, FING_Y0):
        for x in range(x0, x1 + 1):
            c.put(x, y, 'G' if y < y1 + 4 else 'g2')
    for x in range(x0, x1 + 1):
        c.put(x, y1 + 1, 'T') if False else None
    random.seed(7)
    for i in range(14):
        sx = x0 + 6 + i * 11
        c.rect(sx, y1 + 3, sx + 3, y1 + 5, 'k' if i % 3 else 'b')
        c.rect(sx, y1 + 3, sx + 3, y1 + 3, 'x')
        for k in range(4):
            c.put(sx + k, y1 + 7, 'Y' if k % 2 == 0 else 'g2')
    # 金手指：逐根（3 像素宽，1 像素缝），PCIe 键位缺口
    for x in range(x0 + 2, x1 - 2):
        if NOTCH_X - 2 <= x <= NOTCH_X + 3 or (x - x0) % 4 == 3:
            continue
        for y in range(FING_Y0, FING_Y1 + 1):
            c.put(x, y, 'h' if y == FING_Y0 else 'Y' if (x - x0) % 4 != 0 else 'p')
        c.put(x, FING_Y1, 'e')
    c.rect(x0, FING_Y0, x1, FING_Y0, 'g2')
    for y in range(FING_Y0 + 1, FING_Y1 + 1):                  # 缝里露出 PCB
        for x in range(x0 + 2, x1 - 2):
            if c.px[y][x] is None and not (NOTCH_X - 2 <= x <= NOTCH_X + 3):
                c.put(x, y, 'g2')
    if burnt:
        for cx, cy in FANS:
            for dx, dy in ((0, 0), (5, -5), (-6, 6), (8, 4), (-9, -3), (2, 10)):
                c.rect(cx + dx - 2, cy + dy - 2, cx + dx + 2, cy + dy + 2, 'c')
        for x in range(x0 + 4, x1 - 20):
            c.rect(x, y1 - 5, x, y1 - 3, 'e')
        for dx, dy in ((30, 70), (90, 40), (150, 80), (60, 30), (120, 88)):
            c.rect(dx, dy, dx + 4, dy + 3, 'c')
    outline(c)
    return c


def screw(c, x, y):
    c.rect(x - 3, y - 3, x + 3, y + 3, 'm')
    c.rect(x - 3, y - 3, x - 1, y - 3, 'h')
    c.rect(x - 2, y - 2, x + 2, y + 2, 'b')
    c.rect(x - 2, y, x + 2, y, 'k'); c.rect(x, y - 2, x, y + 2, 'k')
    c.rect(x + 2, y + 2, x + 3, y + 3, 'd')


def qr(c, x, y, seed):
    rnd = random.Random(seed)
    n = 21
    c.rect(x - 1, y - 1, x + n, y + n, 'w')
    for j in range(n):
        for i in range(n):
            on = rnd.random() < 0.5
            for fx, fy in ((0, 0), (n - 7, 0), (0, n - 7)):
                if fx <= i < fx + 7 and fy <= j < fy + 7:
                    li, lj = i - fx, j - fy
                    on = li in (0, 6) or lj in (0, 6) or (2 <= li <= 4 and 2 <= lj <= 4)
                elif fx - 1 <= i <= fx + 7 and fy - 1 <= j <= fy + 7:
                    on = False
            if on:
                c.put(x + i, y + j, 'n')


def sticker(c, seed):
    """序列号贴纸（右下）：二维码、条码、两行小字。"""
    c.rect(86, 64, 172, 100, 'w')
    c.rect(86, 64, 172, 65, 'h'); c.rect(86, 99, 172, 100, 'l')
    c.rect(86, 64, 87, 100, 'h'); c.rect(171, 64, 172, 100, 'l')
    qr(c, 91, 70, seed)
    for x in range(118, 166):
        w = 1 + ((x * 7 + seed) % 3)
        if (x * 5 + seed) % 4 != 0:
            c.rect(x, 70, x, 84, 'n')


def seal(c):
    """封条（红底白字）：压住贴纸右边缘。"""
    c.rect(174, 60, 182, 104, 'R')
    c.rect(174, 60, 175, 104, 'r2'); c.rect(181, 60, 182, 104, 'r2')
    for y in range(66, 100, 8):
        c.rect(176, y, 180, y + 3, 'w')
        c.put(178, y + 1, 'R')


def gpu_back(accent, accent_dark, burnt=False):
    c = Canvas(BW, BH)
    bracket(c)
    x0, y0 = SH0
    x1, y1 = SH1
    for y in range(y0, y1 + 12):
        for x in range(x0, x1 + 1):
            c.put(x, y, 'd' if y % 2 else 'm' if (x // 3) % 4 else 'd')
    for y in range(y0, y1 + 12):
        c.put(x0, y, 'l'); c.put(x0 + 1, y, 'b'); c.put(x1, y, 'k'); c.put(x1 - 1, y, 'z')
    for x in range(x0, x1 + 1):
        c.put(x, y0, 'h'); c.put(x, y0 + 1, 'l'); c.put(x, y0 + 2, 'b')
    # 丝印凹槽（型号叠加图写在这里）
    c.rect(36, 24, 178, 52, 'z')
    c.rect(36, 24, 178, 25, 'k'); c.rect(36, 24, 37, 52, 'k')
    c.rect(36, 52, 178, 52, 'x'); c.rect(178, 24, 178, 52, 'x')
    # 品牌色斜条
    for x in range(x0 + 4, x1 - 4):
        c.rect(x, 56, x, 59, accent)
        c.put(x, 60, accent_dark)
    # 通风槽（左下）
    for y in range(70, 100, 6):
        c.rect(x0 + 10, y, x0 + 50, y + 3, 'k')
        c.rect(x0 + 10, y + 4, x0 + 50, y + 4, 'x')
    # 四角螺丝
    for sx, sy in ((x0 + 12, y0 + 10), (x1 - 12, y0 + 10), (x0 + 12, y1 - 2), (x1 - 12, y1 - 2)):
        screw(c, sx, sy)
    sticker(c, 5)
    seal(c)
    # 背面金手指（暗一档）：触点之间是 PCB 的深绿，只有键位缺口是空的
    for y in range(y1 + 12, FING_Y1 + 1):
        for x in range(x0, x1 + 1):
            if NOTCH_X - 2 <= x <= NOTCH_X + 3 and y > y1 + 13:
                continue
            c.put(x, y, 'g2')
    for x in range(x0 + 2, x1 - 2):
        if NOTCH_X - 2 <= x <= NOTCH_X + 3 or (x - x0) % 4 == 3:
            continue
        for y in range(y1 + 14, FING_Y1 + 1):
            c.put(x, y, 'Y' if (x - x0) % 4 else 'p')
    if burnt:
        for dx, dy in ((60, 30), (130, 40), (50, 80), (100, 70), (150, 90)):
            c.rect(dx, dy, dx + 6, dy + 4, 'c')
    outline(c)
    return c


def new():
    return Canvas(BW, BH)


# ---------- 叠加图 ----------

def ov_fan(kind):
    c = new()
    rnd = random.Random(11 + kind)
    for cx, cy in FANS:
        if kind == 1:                                          # 积灰：叶片上一层灰点和灰絮
            for _ in range(190):
                a = rnd.random() * math.tau
                r = 8 + rnd.random() * (FR - 12)
                x, y = int(cx + math.cos(a) * r), int(cy + math.sin(a) * r)
                c.put(x, y, 'l' if rnd.random() < 0.6 else 'h')
                if rnd.random() < 0.3:
                    c.put(x + 1, y, 'l')
            for _ in range(10):                                # 絮状长条
                a = rnd.random() * math.tau
                r = 10 + rnd.random() * (FR - 16)
                x, y = int(cx + math.cos(a) * r), int(cy + math.sin(a) * r)
                for k in range(5):
                    c.put(x + k, y + (k // 2), 'w' if k % 2 == 0 else 'l')
        else:                                                  # 缺角：一片叶尖缺一块，露出风道黑底
            a0 = 0.9 if cx < 100 else 3.2
            for r in range(10, FR - 3):
                for da in (-0.20, -0.17, -0.13, -0.10, -0.06, -0.02, 0.02, 0.06, 0.10, 0.14):
                    if r > FR - 14 + (da * 30) and r > 17:
                        c.put(int(cx + math.cos(a0 + da) * r), int(cy + math.sin(a0 + da) * r), 'n')
            for r in range(17, FR - 3):
                c.put(int(cx + math.cos(a0 - 0.22) * r), int(cy + math.sin(a0 - 0.22) * r), 'h')
    return c


def ov_fingers(kind):
    c = new()
    rnd = random.Random(21 + kind)
    x0, x1 = SH0[0], SH1[0]
    for x in range(x0 + 2, x1 - 2):
        if NOTCH_X - 2 <= x <= NOTCH_X + 3 or (x - x0) % 4 == 3:
            continue
        for y in range(FING_Y0 + 1, FING_Y1 + 1):
            if kind == 1:                                      # 发暗：触点变褐
                c.put(x, y, 'e' if (x + y) % 3 else 'q')
            else:                                              # 氧化：发绿的斑块
                if rnd.random() < 0.65:
                    c.put(x, y, 'g2' if rnd.random() < 0.5 else 'G')
    return c


def ov_bracket(kind):
    c = new()
    rnd = random.Random(31)
    if kind == 1:                                              # 划痕
        for _ in range(9):
            x, y = rnd.randint(5, 17), rnd.randint(14, 100)
            for k in range(rnd.randint(5, 12)):
                c.put(x + k // 2, y + k, 'h' if k % 2 else 'k')
    else:                                                      # 接口数不对：第三个接口被板子封死
        c.rect(6, 56, 17, 69, 'b')
        c.rect(6, 56, 17, 56, 'h'); c.rect(6, 56, 6, 69, 'l'); c.rect(17, 56, 17, 69, 'd')
        for x in range(7, 17, 2):
            c.put(x, 62, 'm')
    return c


def ov_text(model, skew):
    c = new()
    text57(c, 46, 31, model, 'l' if skew else 'h', scale=2, skew=skew, gap=1)
    return c


def ov_sticker(kind):
    c = new()
    if kind == 1:                                              # 手写序列号
        c.rect(86, 64, 172, 100, 'w')
        c.rect(86, 64, 172, 65, 'h'); c.rect(86, 99, 172, 100, 'l')
        c.rect(86, 64, 87, 100, 'h'); c.rect(171, 64, 172, 100, 'l')
        rnd = random.Random(41)
        for row in (74, 84, 92):
            x = 92
            while x < 164:
                w = rnd.randint(3, 7)
                for k in range(w):
                    c.put(x + k, row + int(math.sin(k + x) * 1.5), 'O')
                    c.put(x + k, row + 1 + int(math.sin(k + x) * 1.5), 'O')
                x += w + rnd.randint(2, 4)
    else:                                                      # 封条缺失：残留的胶痕与撕口
        c.rect(174, 60, 182, 104, 'd')
        for y in range(60, 105, 3):
            c.rect(174, y, 178 + (y % 5), y + 1, 'm')
        for y in range(62, 104, 6):
            c.rect(179, y, 181, y + 1, 'x')
    return c


BIG = {}
for _b, (_a, _ad, _t) in {'probidia': ('G', 'g2', 'PROBIDIA'), 'pmd': ('R', 'r2', 'PMD')}.items():
    for _tier in range(4):
        BIG['gpu_big_%s_f_t%d' % (_b, _tier)] = (lambda a=_a, ad=_ad, t=_t, k=_tier: gpu_front(a, ad, t, k))
    for _pin in range(3):                                      # 供电口与风扇数各自独立（假卡可以只对不上其中一项）
        for _n in range(1, 4):
            BIG['gpu_big_%s_f_p%dn%d' % (_b, _pin, _n)] = (lambda a=_a, ad=_ad, t=_t, p=_pin, n=_n: gpu_front(a, ad, t, 1, count=n, pin=p))
    BIG['gpu_big_%s_b' % _b] = (lambda a=_a, ad=_ad: gpu_back(a, ad))
BIG['gpu_big_burnt_f'] = lambda: gpu_front('e', 'c', 'PROBIDIA', 1, burnt=True)
BIG['gpu_big_burnt_b'] = lambda: gpu_back('e', 'c', burnt=True)
BIG_OV = {}

if __name__ == '__main__':
    import sys, os
    if len(sys.argv) == 3 and sys.argv[1] == '--preview':
        d = sys.argv[2]
        def stack(base, *ovs):
            b = BIG[base]()
            for o in ovs:
                oc = BIG_OV[o]()
                for y in range(oc.h):
                    for x in range(oc.w):
                        if oc.px[y][x] is not None:
                            b.px[y][x] = oc.px[y][x]
            return b
        bg = (0x6A, 0x72, 0x70)
        for t in range(4):
            stack('gpu_big_probidia_f_t%d' % t).png(os.path.join(d, 'big_f_t%d.png' % t), scale=3, bg=bg)
