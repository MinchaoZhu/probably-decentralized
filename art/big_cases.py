
"""显卡检视大图叠加层：用于放大镜检视台的各种真假/成色鉴定 case 细节层。
与底图同尺寸（192×128），仅绘制发生变化的像素。
"""

import math
import random
from big import (
    BW,
    BH,
    FANS,
    FR,
    NOTCH_X,
    SH0,
    SH1,
    FING_Y0,
    FING_Y1,
    Canvas,
    text57,
    F57,
)


def _new():
    return Canvas(BW, BH)


# ==============================================================================
# 正面 Case
# ==============================================================================


def fan_dust1():
    """1. 风扇轻度积灰：叶片边缘灰线、轴心轻微积垢、少量蛛丝细线。"""
    c = _new()
    rnd = random.Random(101)

    for cx, cy in FANS:
        # 轴心周围微灰圈
        for a_deg in range(0, 360, 15):
            rad = math.radians(a_deg)
            r = 7.0 + (rnd.random() * 2.5)
            x, y = int(cx + math.cos(rad) * r), int(cy + math.sin(rad) * r)
            c.put(x, y, "m" if rnd.random() < 0.65 else "d")

        # 九片叶片的前缘微量积灰与边角聚灰
        for blade in range(9):
            base_ang = blade * (2 * math.pi / 9)
            for r in range(10, FR - 4, 2):
                ang = base_ang - (r - 7) * 0.055
                x = int(cx + math.cos(ang) * r)
                y = int(cy + math.sin(ang) * r)
                if rnd.random() < 0.40:
                    c.put(x, y, "b" if rnd.random() < 0.6 else "m")
                # 叶尖处积微灰
                if r > FR - 8 and rnd.random() < 0.55:
                    c.put(x + 1, y, "l" if rnd.random() < 0.3 else "m")

        # 2~3 根搭在扇叶与风道外框之间的微细蛛丝
        for p0, p1 in (
            ((cx - 18, cy - 14), (cx - 25, cy - 10)),
            ((cx + 10, cy + 18), (cx + 22, cy + 14)),
        ):
            x0, y0 = p0
            x1, y1 = p1
            steps = max(abs(x1 - x0), abs(y1 - y0))
            for i in range(steps + 1):
                t = i / steps
                sx = int(x0 + (x1 - x0) * t)
                sy = int(y0 + (y1 - y0) * t + math.sin(t * math.pi) * 1.5)
                c.put(sx, sy, "l" if i % 2 == 0 else "b")

    return c


def fan_dust2():
    """1. 风扇重度积灰：轴心厚灰结垢、叶面大团絮状灰斑、风道外圈堵灰、蜘蛛网细丝。"""
    c = _new()
    rnd = random.Random(102)

    for cx, cy in FANS:
        # 轴心及周边重垢厚积
        for dy in range(-12, 13):
            for dx in range(-12, 13):
                r = math.hypot(dx, dy)
                if 5.5 <= r <= 13.0:
                    val = rnd.random()
                    if val < 0.70:
                        c.put(
                            cx + dx,
                            cy + dy,
                            "b" if val < 0.25 else "m" if val < 0.55 else "d",
                        )

        # 扇叶大面积覆灰与灰絮团
        for blade in range(9):
            base_ang = blade * (2 * math.pi / 9)
            for r in range(8, FR - 3):
                ang = base_ang - (r - 7) * 0.055
                x = int(cx + math.cos(ang) * r)
                y = int(cy + math.sin(ang) * r)
                c.put(x, y, "b" if rnd.random() < 0.4 else "m")
                if rnd.random() < 0.35:
                    c.put(x, y + 1, "d")

        # 风道死角与外圈凹陷堵灰
        for a_deg in range(0, 360, 4):
            rad = math.radians(a_deg)
            for dr in (FR - 6, FR - 5, FR - 4):
                if rnd.random() < 0.65:
                    x = int(cx + math.cos(rad) * dr)
                    y = int(cy + math.sin(rad) * dr)
                    c.put(x, y, "d" if (x + y) % 2 else "m")

        # 灰絮团（带厚度高光）
        clumps = (
            (cx - 14, cy - 8),
            (cx + 12, cy - 14),
            (cx - 8, cy + 15),
            (cx + 15, cy + 12),
        )
        for kx, ky in clumps:
            c.rect(kx - 1, ky - 1, kx + 2, ky + 1, "m")
            c.rect(kx - 1, ky - 1, kx + 1, ky - 1, "l")
            c.put(kx + 2, ky + 1, "d")
            c.put(kx - 2, ky, "b")
            c.put(kx + 3, ky, "d")

        # 蛛网连线（从内到外蜘蛛网状蛛丝结构）
        anchors = [
            (cx - 24, cy - 12),
            (cx - 16, cy - 20),
            (cx, cy - 25),
            (cx + 18, cy - 18),
            (cx - 12, cy - 5),
            (cx + 8, cy - 8),
        ]
        edges = [(0, 4), (1, 4), (1, 5), (2, 5), (3, 5), (4, 5)]
        for i0, i1 in edges:
            x0, y0 = anchors[i0]
            x1, y1 = anchors[i1]
            steps = max(abs(x1 - x0), abs(y1 - y0))
            for s in range(steps + 1):
                t = s / steps
                sx = int(x0 + (x1 - x0) * t)
                sy = int(y0 + (y1 - y0) * t)
                c.put(sx, sy, "w" if s % 3 == 0 else "l")

    return c


def fan_chip():
    """2. 风扇叶片缺角：扇叶断裂缺损，断面 jagged 崩缺、露出黑风道底，带裂纹延伸至根部。"""
    c = _new()

    # 左风扇断叶：位于右上方叶片（约 15°~45°）
    cx, cy = FANS[0]
    # 崩缺露出底部风道黑腔
    for r in range(16, FR - 2):
        for deg in range(12, 44):
            rad = math.radians(deg)
            # 形成犬牙交错的不规则裂口形状
            r_edge = 18 + int(3 * math.sin(deg * 0.7) + 2 * math.cos(deg * 1.3))
            if r >= r_edge:
                px = int(cx + math.cos(rad) * r)
                py = int(cy + math.sin(rad) * r)
                c.put(px, py, "n" if (px + py) % 3 else "k")

    # 断裂口侧缘受光白色高光毛刺与背光阴影
    break_edge = [
        (cx + 17, cy + 6),
        (cx + 18, cy + 8),
        (cx + 19, cy + 11),
        (cx + 20, cy + 12),
        (cx + 22, cy + 14),
        (cx + 24, cy + 16),
        (cx + 25, cy + 20),
    ]
    for i, (bx, by) in enumerate(break_edge):
        c.put(bx, by, "h" if i % 2 == 0 else "w")
        c.put(bx + 1, by + 1, "k")

    # 裂纹延伸（Stress crack）自断面一路裂向轴心根部
    crack = [
        (cx + 16, cy + 6),
        (cx + 15, cy + 5),
        (cx + 13, cy + 5),
        (cx + 12, cy + 4),
        (cx + 10, cy + 4),
        (cx + 9, cy + 3),
        (cx + 7, cy + 2),
    ]
    for kx, ky in crack:
        c.put(kx, ky, "n")
        c.put(kx, ky - 1, "l")  # 裂缝边缘受光面

    # 右风扇：叶尖崩掉一个小角
    rx, ry = FANS[1]
    for r in range(FR - 8, FR - 2):
        for deg in range(215, 235):
            rad = math.radians(deg)
            if r > FR - 6 + (deg % 3):
                c.put(
                    int(rx + math.cos(rad) * r),
                    int(ry + math.sin(rad) * r),
                    "n",
                )
    c.put(rx - 21, ry - 14, "h")
    c.put(rx - 22, ry - 13, "w")
    c.put(rx - 20, ry - 13, "k")

    return c


def fan_oil():
    """3. 风扇轴心漏油/褪色：轴承油渍沿离心方向与重力向下渗出浸润、轴心贴纸褪色混浊。"""
    c = _new()

    for cx, cy in FANS:
        # 轴心贴纸油污褪色（盖掉原来的鲜艳色，变暗浊褐灰）
        for dy in range(-4, 5):
            for dx in range(-4, 5):
                r = math.hypot(dx, dy)
                if r <= 4.2:
                    c.put(cx + dx, cy + dy, "e" if r < 2.5 else "d")

        # 轴心缝隙溢出的深色环状油垢
        for deg in range(0, 360, 10):
            rad = math.radians(deg)
            for r in (5.0, 6.0, 7.0):
                ox = int(cx + math.cos(rad) * r)
                oy = int(cy + math.sin(rad) * r)
                c.put(ox, oy, "c" if deg % 20 == 0 else "e")

        # 向下及顺着离心叶片流淌的渗油泪痕（重力向下，沿扇叶根部流淌）
        # 1) 主滴漏条（竖直向下）
        drips = [
            (cx, cy + 7, "c"),
            (cx + 1, cy + 7, "e"),
            (cx, cy + 8, "c"),
            (cx - 1, cy + 8, "q"),
            (cx, cy + 9, "e"),
            (cx, cy + 10, "c"),
            (cx + 1, cy + 10, "h"),  # 油液反光
            (cx, cy + 11, "e"),
            (cx, cy + 12, "q"),
            (cx, cy + 13, "e"),
            (cx, cy + 14, "c"),
            (cx, cy + 15, "h"),
            (cx, cy + 16, "e"),
            (cx + 1, cy + 17, "q"),
        ]
        for dx, dy, col in drips:
            c.put(dx, dy, col)

        # 2) 右下斜向叶片根部浸润油晕
        side_drips = [
            (cx + 6, cy + 5, "e"),
            (cx + 7, cy + 6, "c"),
            (cx + 8, cy + 7, "q"),
            (cx + 9, cy + 7, "h"),
            (cx + 10, cy + 8, "e"),
            (cx + 11, cy + 9, "c"),
            (cx + 12, cy + 10, "e"),
            (cx + 13, cy + 10, "q"),
        ]
        for dx, dy, col in side_drips:
            c.put(dx, dy, col)

        # 3) 左上回渗微斑
        c.put(cx - 6, cy - 4, "e")
        c.put(cx - 7, cy - 5, "q")
        c.put(cx - 8, cy - 5, "c")

    return c


def fin_dim():
    """4. 金手指发暗：镀金磨损发褐，失去高光亮黄，露出发黑氧化的暗铜色和划轨。"""
    c = _new()
    x0, x1 = SH0[0], SH1[0]

    for x in range(x0 + 2, x1 - 2):
        if NOTCH_X - 2 <= x <= NOTCH_X + 3 or (x - x0) % 4 == 3:
            continue
        pin_offset = (x - x0) % 4

        # 顶部斜切高光被暗黄铜/暗灰取代
        c.put(x, FING_Y0, "q" if x % 2 == 0 else "m")

        # 针脚本体：失去 'Y'，变为接触氧化的暗褐 'e'、暗铜 'q' 与焦褐
        for y in range(FING_Y0 + 1, FING_Y1):
            # 模拟 PCIe 插拔划道痕迹（中间有极细竖向磨损深槽）
            if pin_offset == 1 and y in (112, 113, 116, 117, 118, 120):
                c.put(x, y, "c")  # 磨损黑槽
            elif (x + y * 2) % 5 == 0:
                c.put(x, y, "c")
            elif (x + y) % 2 == 0:
                c.put(x, y, "e")
            else:
                c.put(x, y, "q")

        # 底部插拔磨耗端
        c.put(x, FING_Y1, "c" if pin_offset == 0 else "e")

    return c


def fin_oxide():
    """5. 金手指氧化：铜绿由根部与底部向上爬蔓延，斑驳的蓝绿/深绿锈斑（'G'/'g2'/'S'）。"""
    c = _new()
    rnd = random.Random(205)
    x0, x1 = SH0[0], SH1[0]

    # 定义几个主要锈蚀蔓延爆发区（树枝状爬生）
    oxide_zones = [
        (35, 62, 11),  # (x_start, x_end, max_h)
        (78, 105, 8),
        (125, 168, 13),
    ]

    for x in range(x0 + 2, x1 - 2):
        if NOTCH_X - 2 <= x <= NOTCH_X + 3 or (x - x0) % 4 == 3:
            continue

        # 计算当前 x 的铜锈高度
        h_grow = 0
        for z0, z1, max_h in oxide_zones:
            if z0 <= x <= z1:
                mid = (z0 + z1) / 2
                span = (z1 - z0) / 2
                wave = math.cos((x - mid) / span * (math.pi / 2))
                h_grow = max(
                    h_grow,
                    int(
                        wave * max_h
                        + rnd.randint(-1, 2)
                        + (1 if x % 3 == 0 else 0)
                    ),
                )

        if h_grow > 0:
            # 铜锈从底部根部 FING_Y1 往上爬
            for dy in range(h_grow + 1):
                y = FING_Y1 - dy
                if y < FING_Y0:
                    continue
                # 锈壳中心粉绿 'S'，主体 'G'，深根 'g2'
                if dy == h_grow:
                    col = "S" if rnd.random() < 0.4 else "G"
                elif dy > h_grow // 2:
                    col = (
                        "G"
                        if rnd.random() < 0.7
                        else "S"
                        if rnd.random() < 0.5
                        else "g2"
                    )
                else:
                    col = "g2" if rnd.random() < 0.8 else "c"
                c.put(x, y, col)

            # 少量铜锈蔓延到相邻缝隙
            if (x - x0) % 4 == 2 and rnd.random() < 0.65:
                c.put(x + 1, FING_Y1 - (h_grow // 2), "g2")

    return c


def brk_scratch():
    """6. 挡板划痕与凹坑：斜向金属切划痕（亮边+暗槽）与冲压碰伤凹坑。"""
    c = _new()

    # 细划痕 1：贯穿上部金属区（斜向上提）
    scratch1 = [
        (5, 18, "h"),
        (6, 17, "w"),
        (7, 16, "h"),
        (8, 15, "w"),
        (9, 14, "h"),
        (10, 13, "l"),
        (11, 12, "h"),
        (12, 11, "k"),
    ]
    for x, y, col in scratch1:
        c.put(x, y, col)
        c.put(x, y + 1, "k")  # 划痕背光刻沟

    # 细划痕 2：接口 1 与 2 之间的斜割痕
    scratch2 = [
        (7, 31, "w"),
        (8, 32, "h"),
        (9, 32, "k"),
        (10, 33, "h"),
        (11, 33, "w"),
        (12, 34, "h"),
        (13, 35, "k"),
        (14, 35, "h"),
        (15, 36, "w"),
    ]
    for x, y, col in scratch2:
        c.put(x, y, col)
        c.put(x, y - 1, "d")

    # 细划痕 3：接口 2 与 3 之间十字交叉滑刀
    cross1 = [
        (6, 51, "h"),
        (7, 52, "w"),
        (8, 52, "h"),
        (9, 53, "h"),
        (10, 54, "w"),
        (11, 54, "k"),
    ]
    cross2 = [
        (6, 54, "w"),
        (7, 53, "h"),
        (8, 53, "k"),
        (9, 52, "h"),
        (10, 51, "w"),
        (11, 51, "h"),
    ]
    for x, y, col in cross1:
        c.put(x, y, col)
    for x, y, col in cross2:
        c.put(x, y, col)

    # 细划痕 4：底部螺丝附近的起子滑刀伤
    slip = [
        (8, 103, "k"),
        (9, 104, "w"),
        (10, 104, "h"),
        (11, 105, "w"),
        (12, 105, "h"),
        (13, 105, "k"),
        (14, 106, "w"),
        (15, 106, "h"),
    ]
    for x, y, col in slip:
        c.put(x, y, col)
        c.put(x, y + 1, "k")

    # 冲压小凹坑（小落伤，凹陷光影：左上暗槽、右下挤压高光圈）
    pits = ((14, 22), (8, 72), (16, 75))
    for px, py in pits:
        c.put(px, py, "k")
        c.put(px + 1, py, "d")
        c.put(px, py + 1, "d")
        c.put(px + 1, py + 1, "w")
        c.put(px + 2, py + 1, "h")

    return c


def brk_port():
    """7. 挡板接口数量不对：第三个接口被粗糙铁皮焊死/堵死，带焊渣点与开孔被封死。"""
    c = _new()
    y0, y1 = 56, 69

    # 盖上一整块粗糙铁皮盲板封死
    c.rect(5, y0 - 1, 18, y1 + 1, "d")
    c.rect(6, y0, 17, y1, "m")

    # 盲板拉丝与表面凹凸
    for x in range(6, 18):
        for y in range(y0, y1 + 1):
            if (x * 3 + y * 7) % 5 == 0:
                c.put(x, y, "b")
            elif (x + y) % 4 == 0:
                c.put(x, y, "d")

    # 边缘粗糙焊缝与接合处阴影黑线
    for y in range(y0 - 1, y1 + 2):
        c.put(5, y, "n")
        c.put(18, y, "k")
    for x in range(5, 19):
        c.put(x, y0 - 1, "n")
        c.put(x, y1 + 1, "k")

    # 两处粗糙固定冲压铆钉/点焊斑点
    for ry in (y0 + 2, y1 - 3):
        c.rect(10, ry, 13, ry + 2, "k")
        c.put(11, ry + 1, "b")
        c.put(12, ry + 1, "h")
        c.put(10, ry, "d")

    # 焊渣与高温变色痕（铜黄/焦色斑点）
    weld_slag = [
        (5, y0 + 1, "e"),
        (6, y0 + 1, "q"),
        (17, y0 + 4, "e"),
        (18, y0 + 5, "c"),
        (6, y1 - 1, "q"),
        (5, y1, "e"),
        (18, y1 - 2, "c"),
    ]
    for sx, sy, scol in weld_slag:
        c.put(sx, sy, scol)

    return c


def shroud_chip():
    """8. 散热罩磕碰/掉漆：外罩棱角撞击凹瘪、掉漆露出底铝银光与发散暗裂纹。"""
    c = _new()

    # 1) 顶部主磕碰坑（x=80~90 处受剧烈撞击）
    # 撞入深凹坑
    c.rect(81, 14, 88, 16, "k")
    c.rect(82, 14, 87, 15, "n")
    # 露出的底漆生铝金属高光反光
    c.rect(83, 16, 86, 17, "w")
    c.put(82, 16, "h")
    c.put(87, 16, "l")
    # 漆皮外卷毛边
    for kx in range(80, 90):
        c.put(kx, 13, "h" if kx % 2 == 0 else "l")
        c.put(kx, 17, "k" if kx % 2 == 0 else "d")
    # 撞击点向下延伸出的塑料/金属外壳发丝状应力裂纹
    crack1 = [
        (84, 18, "n"),
        (84, 19, "k"),
        (83, 20, "n"),
        (83, 21, "k"),
        (82, 22, "n"),
        (81, 23, "k"),
        (81, 24, "n"),
    ]
    for cx, cy, col in crack1:
        c.put(cx, cy, col)
        c.put(cx + 1, cy, "h")  # 裂缝一侧高光

    # 2) 中筋（两风扇之间）被工具磕掉漆皮
    c.rect(104, 46, 107, 51, "k")
    c.rect(105, 47, 106, 49, "w")  # 露底生铝
    c.put(106, 50, "h")
    c.put(104, 45, "h")
    c.put(107, 52, "d")

    # 3) 右上角倒角处擦碰
    c.rect(179, 17, 183, 20, "k")
    c.put(180, 18, "w")
    c.put(181, 19, "h")
    c.put(182, 20, "l")
    c.put(178, 16, "h")

    # 4) 下部边缘刮蹭掉漆痕
    c.rect(34, 93, 44, 94, "k")
    for gx in range(35, 44, 2):
        c.put(gx, 93, "w")
        c.put(gx + 1, 94, "l")

    return c


# ==============================================================================
# 背面 Case
# ==============================================================================


def print_overlay(model_text, style="normal"):
    """9. 型号丝印：
    - normal: 激光蚀刻（深色凹槽衬底+规整高光金属字）
    - skew: 字歪重印（各字上下抖动、墨色不均、重影移位、边缘溢墨）
    - stamp: 盖章式补印（原字发暗底层+倾斜补印红框印章层覆盖）
    """
    c = _new()
    scale = 2
    # 丝印凹槽居中计算
    total_w = len(model_text) * (5 + 1) * scale
    x_start = max(42, 38 + (136 - total_w) // 2)
    y_start = 31

    if style == "normal":
        # 先打一层蚀刻内凹深色边缘刻痕
        text57(
            c,
            x_start + 1,
            y_start + 1,
            model_text,
            "k",
            scale=scale,
            skew=False,
            gap=1,
        )
        # 精致激光银白字
        text57(
            c,
            x_start,
            y_start,
            model_text,
            "h",
            scale=scale,
            skew=False,
            gap=1,
        )

    elif style == "skew":
        cur_x = x_start
        rnd = random.Random(309)
        jitters = [0, 2, -1, 1, -2, 2, 0, 1]

        # 1) 先打一层失准的底墨重影（偏移）
        for i, ch in enumerate(model_text):
            dy = jitters[i % len(jitters)]
            # 偏色与残墨
            text57(
                c,
                cur_x + 1,
                y_start + dy + 1,
                ch,
                "d",
                scale=scale,
                skew=False,
                gap=1,
            )
            cur_x += (5 + 1) * scale + (1 if i % 3 == 0 else 0)

        # 2) 正式墨迹（每字高度不齐、颜色深浅不同）
        cur_x = x_start
        ink_colors = ["h", "l", "b", "h", "m", "l"]
        for i, ch in enumerate(model_text):
            dy = jitters[i % len(jitters)]
            col = ink_colors[i % len(ink_colors)]
            text57(
                c,
                cur_x,
                y_start + dy,
                ch,
                col,
                scale=scale,
                skew=False,
                gap=1,
            )

            # 边缘溢墨斑点 (Ink bleed)
            if ch != " ":
                for _ in range(4):
                    bx = cur_x + rnd.randint(-1, 10)
                    by = y_start + dy + rnd.randint(-1, 14)
                    if 36 <= bx <= 178 and 26 <= by <= 50:
                        c.put(bx, by, "x" if rnd.random() < 0.5 else "d")

            cur_x += (5 + 1) * scale + (1 if i % 3 == 0 else 0)

    elif style == "stamp":
        # 1) 底层被磨掉/发暗的原型号痕迹（原先低配型号残影）
        orig_text = "RTX 50" if "80" in model_text else "PMD 60"
        text57(c, x_start + 4, y_start + 1, orig_text, "z", scale=scale, gap=1)
        text57(c, x_start + 5, y_start + 2, orig_text, "k", scale=scale, gap=1)

        # 2) 盖章边框（红章边框，微倾斜）
        box_x0 = x_start - 4
        box_y0 = y_start - 3
        box_x1 = min(176, x_start + total_w + 4)
        box_y1 = y_start + 17
        c.rect(box_x0, box_y0, box_x1, box_y0 + 1, "R")
        c.rect(box_x0, box_y1 - 1, box_x1, box_y1, "R")
        c.rect(box_x0, box_y0, box_x0 + 1, box_y1, "R")
        c.rect(box_x1 - 1, box_y0, box_x1, box_y1, "R")

        # 章角溢印与盖印残红
        c.put(box_x0 - 1, box_y0, "r2")
        c.put(box_x1 + 1, box_y1, "r2")
        c.put(box_x0 + 1, box_y1 + 1, "r2")

        # 3) 盖印文字（红色印油，因印泥压力不均带斑驳漏白）
        text57(
            c,
            x_start,
            y_start,
            model_text,
            "R",
            scale=scale,
            skew=False,
            gap=1,
        )
        rnd = random.Random(409)
        for y in range(box_y0, box_y1 + 1):
            for x in range(box_x0, box_x1 + 1):
                if c.px[y][x] == "R" and rnd.random() < 0.22:
                    c.put(x, y, "r2" if rnd.random() < 0.6 else "w")

    return c


def print_skew(model_text="RTX 80Ti"):
    return print_overlay(model_text, "skew")


def print_stamp(model_text="RTX 80Ti"):
    return print_overlay(model_text, "stamp")


def print_normal(model_text="RTX 80Ti"):
    return print_overlay(model_text, "normal")


def sticker_hand():
    """10. 序列号贴纸：白胶条盖住原条码，上面粗劣手写伪造序列号。"""
    c = _new()

    # 白胶带打底覆盖条码区域 (x=116..168, y=68..86)
    c.rect(116, 68, 168, 86, "w")
    # 胶带边缘立体层感与受光边
    c.rect(116, 68, 168, 68, "h")
    c.rect(116, 86, 168, 86, "l")
    c.rect(116, 68, 116, 86, "h")
    c.rect(168, 68, 168, 86, "l")

    # 手写体字符 "SN:8942-X"（手写黑色笔画，带运笔粗细与倾斜波动）
    strokes = [
        # S
        (120, 72),
        (121, 72),
        (122, 72),
        (119, 73),
        (120, 74),
        (121, 75),
        (122, 76),
        (119, 77),
        (120, 78),
        (121, 78),
        # N
        (125, 78),
        (125, 77),
        (125, 76),
        (125, 75),
        (125, 74),
        (125, 73),
        (125, 72),
        (126, 74),
        (127, 75),
        (128, 76),
        (129, 77),
        (129, 72),
        (129, 73),
        (129, 74),
        (129, 75),
        (129, 76),
        (129, 78),
        # :
        (132, 74),
        (132, 77),
        # 8
        (136, 72),
        (137, 72),
        (135, 73),
        (138, 73),
        (136, 75),
        (137, 75),
        (135, 76),
        (138, 76),
        (135, 77),
        (138, 77),
        (136, 78),
        (137, 78),
        # 9
        (142, 72),
        (143, 72),
        (141, 73),
        (144, 73),
        (141, 74),
        (144, 74),
        (142, 75),
        (143, 75),
        (144, 75),
        (144, 76),
        (144, 77),
        (143, 78),
        (142, 78),
        # 4
        (147, 72),
        (147, 73),
        (147, 74),
        (147, 75),
        (148, 75),
        (149, 75),
        (150, 75),
        (150, 72),
        (150, 73),
        (150, 74),
        (150, 76),
        (150, 77),
        (150, 78),
        # 2
        (153, 72),
        (154, 72),
        (155, 73),
        (154, 74),
        (153, 75),
        (153, 76),
        (153, 77),
        (153, 78),
        (154, 78),
        (155, 78),
        (156, 78),
        # -
        (158, 75),
        (159, 75),
        (160, 75),
        # X
        (162, 72),
        (163, 73),
        (164, 74),
        (165, 76),
        (166, 77),
        (166, 78),
        (166, 72),
        (165, 73),
        (164, 75),
        (163, 77),
        (162, 78),
    ]
    for x, y in strokes:
        c.put(x, y, "O")
        c.put(x + 1, y, "O")  # 笔尖圆珠粗细
        c.put(x, y + 1, "k")  # 圆珠笔纸面压痕阴影

    return c


def sticker_scratch():
    """10. 序列号贴纸：条码被硬物反复恶意刮花破坏，白纸撕破、划痕露底。"""
    c = _new()

    # 横向主刮痕（钥匙/一字起深划过，条码截断）
    for x in range(116, 168):
        c.put(x, 75, "w")
        c.put(x, 76, "w")
        c.put(x, 77, "h" if x % 3 == 0 else "l")
        c.put(x, 78, "d" if x % 2 == 0 else "m")

    # 两道斜向猛烈刮叉痕
    slash1 = [
        (120, 70),
        (123, 72),
        (126, 74),
        (130, 77),
        (134, 80),
        (138, 82),
        (142, 84),
        (145, 86),
    ]
    for x, y in slash1:
        c.rect(x - 1, y - 1, x + 1, y, "w")
        c.put(x + 2, y, "d")
        c.put(x - 1, y + 1, "k")

    slash2 = [
        (160, 71),
        (157, 73),
        (153, 76),
        (149, 79),
        (145, 82),
        (142, 84),
        (138, 86),
    ]
    for x, y in slash2:
        c.rect(x - 1, y - 1, x + 1, y, "w")
        c.put(x + 1, y + 1, "k")
        c.put(x - 2, y, "l")

    # 纸屑翻边卷毛 (Torn paper fibers)
    for fx, fy in (
        (128, 74),
        (132, 78),
        (146, 83),
        (152, 75),
        (162, 74),
        (121, 77),
    ):
        c.rect(fx, fy, fx + 1, fy + 1, "h")
        c.put(fx, fy - 1, "w")

    return c


def sticker_torn():
    """10. 序列号贴纸：贴纸被撕掉一大半，留撕破犬牙撕口与发黄恶心残胶。"""
    c = _new()
    rnd = random.Random(510)

    # 撕裂区域：x=120..172, y=64..100（右侧大半被撕走，露出背板石墨底）
    for y in range(64, 101):
        # 锯齿状撕裂边界 x_tear
        x_tear = 120 + int(3 * math.sin(y * 0.45) + (y % 4))
        # 撕除暴露出的背板底色（重现 backplate 拉丝暗底）
        for x in range(x_tear, 173):
            c.put(x, y, "d" if y % 2 else "m" if (x // 3) % 4 else "d")

        # 犬牙差互的白纸撕口边缘 (White torn paper fringe)
        c.put(x_tear - 2, y, "w")
        c.put(x_tear - 1, y, "h")
        c.put(x_tear, y, "l")
        c.put(x_tear + 1, y, "d")

    # 背板上残留的大片发干、粘着灰尘的胶痕（残胶：'e', 'q', 'm', 'd', 'k'）
    glue_patches = [
        (126, 68, 142, 82),
        (148, 72, 168, 92),
        (132, 84, 150, 96),
    ]
    for gx0, gy0, gx1, gy1 in glue_patches:
        for gy in range(gy0, gy1 + 1):
            for gx in range(gx0, gx1 + 1):
                val = rnd.random()
                if val < 0.55:
                    c.put(
                        gx,
                        gy,
                        (
                            "e"
                            if val < 0.20
                            else "q"
                            if val < 0.35
                            else "m"
                            if val < 0.48
                            else "k"
                        ),
                    )

    # 没撕干净的局部纸片孤岛
    c.rect(156, 82, 165, 87, "w")
    c.rect(157, 83, 164, 86, "h")
    c.put(166, 88, "d")

    return c


def seal_missing():
    """11. 封条缺失：防拆封条被撕除，留边缘红碎纸片、底层 honeycomb/VOID 残胶与刮痕。"""
    c = _new()
    # 封条原本位置: x=174..182, y=60..104

    # 露出底板金属
    for y in range(60, 105):
        for x in range(174, 183):
            c.put(x, y, "d" if y % 2 else "m" if (x // 3) % 4 else "d")

    # 顶部和底部断裂未撕净的红色封条碎片 (Torn seal scraps)
    c.rect(174, 60, 182, 63, "R")
    c.put(174, 64, "R")
    c.put(175, 64, "r2")
    c.put(178, 64, "R")
    c.put(181, 64, "r2")

    c.rect(174, 102, 182, 104, "R")
    c.put(176, 101, "R")
    c.put(177, 101, "r2")
    c.put(180, 101, "R")

    # 撕裂防伪胶转移层遗留的网格残胶斑（VOID 格状残留胶质）
    for y in range(65, 101):
        for x in range(174, 183):
            # 规则点阵中带撕损
            if (x + y) % 3 == 0:
                c.put(x, y, "r2" if (x * y) % 2 == 0 else "e")
            elif (x * 2 + y) % 5 == 0:
                c.put(x, y, "m")

    # 指甲抠挖清理残胶的竖划痕
    for y in range(70, 95):
        c.put(177, y, "h" if y % 3 == 0 else "k")
        c.put(180, y, "k" if y % 2 == 0 else "d")

    return c


def seal_reglued():
    """11. 封条被重新粘过：封条贴歪、角落起翘卷边（带投影）、内含明显鼓起气泡与折痕。"""
    c = _new()

    # 封条重贴发生偏斜（整体往左歪 1px，并露出右边缘的旧胶印）
    # 旧胶缝留痕
    for y in range(60, 105):
        c.put(182, y, "e" if y % 2 else "d")

    # 重新贴上的红封条（略微偏斜移位）
    c.rect(173, 62, 181, 103, "R")
    c.rect(173, 62, 174, 103, "r2")
    c.rect(180, 62, 181, 103, "r2")

    # 上端角落起翘卷边 (Curled Dog-ear corner)
    # 起翘角露出白色背胶与内卷高光
    c.put(179, 61, "w")
    c.put(180, 60, "w")
    c.put(181, 60, "h")
    c.put(179, 62, "h")
    c.put(180, 61, "l")
    # 翘起卷角下方投下的深暗阴影
    c.put(177, 62, "n")
    c.put(178, 62, "k")
    c.put(178, 63, "n")

    # 起泡 1：中间圆形隆起气泡 (Air bubble 1)
    bubble1 = [
        (176, 73, "l"),
        (177, 73, "w"),
        (178, 73, "h"),
        (175, 74, "l"),
        (176, 74, "R"),
        (177, 74, "w"),
        (178, 74, "R"),
        (179, 74, "r2"),
        (175, 75, "h"),
        (176, 75, "R"),
        (177, 75, "R"),
        (178, 75, "r2"),
        (179, 75, "k"),
        (176, 76, "r2"),
        (177, 76, "k"),
        (178, 76, "r2"),
    ]
    for x, y, col in bubble1:
        c.put(x, y, col)

    # 起泡 2：下部条状长气泡 (Air bubble 2)
    bubble2 = [
        (175, 87, "w"),
        (176, 87, "l"),
        (175, 88, "h"),
        (176, 88, "R"),
        (177, 88, "r2"),
        (175, 89, "h"),
        (176, 89, "R"),
        (177, 89, "k"),
        (176, 90, "r2"),
    ]
    for x, y, col in bubble2:
        c.put(x, y, col)

    # 被撕下时产生的死褶横折白印 (Crease stress marks)
    for x in range(173, 182):
        c.put(x, 81, "w" if x % 2 == 0 else "h")
        c.put(x, 82, "r2")
        c.put(x, 95, "h" if x % 3 == 0 else "w")
        c.put(x, 96, "r2")

    return c


def screw_opened():
    """12. 背板螺丝被拆过：
    - (174, 94) 右下螺丝十字孔严重拧花/滑丝，溢出金属毛刺，起子滑出刮花背板；
    - (36, 24) 左上螺丝十字槽口豁开变形磨损；
    - (36, 94) 左下螺丝换成了一颗不同款的一字平头生锈旧螺丝。
    """
    c = _new()

    # --------------------------------------------------------------------------
    # 1. 右下螺丝 (174, 94)：十字彻底滑丝拧烂 (Stripped screw) + 滑刀大划痕
    # --------------------------------------------------------------------------
    sx, sy = 174, 94
    # 彻底磨平滑丝的中心烂圆坑
    c.rect(sx - 2, sy - 2, sx + 2, sy + 2, "k")
    c.rect(sx - 1, sy - 1, sx + 1, sy + 1, "n")
    # 被起子强行旋转挤压出来的银白色金属毛刺边缘
    c.put(sx - 2, sy - 1, "w")
    c.put(sx - 1, sy - 2, "w")
    c.put(sx + 2, sy, "h")
    c.put(sx, sy + 2, "h")
    c.put(sx + 1, sy + 1, "w")
    c.put(sx - 2, sy + 2, "l")
    # 螺丝刀猛烈滑出拧花槽、在背板上划出的一道深深弧形刀痕
    driver_slip = [
        (sx - 2, sy - 2, "w"),
        (sx - 3, sy - 3, "h"),
        (sx - 4, sy - 4, "w"),
        (sx - 5, sy - 4, "k"),
        (sx - 6, sy - 5, "w"),
        (sx - 7, sy - 5, "h"),
        (sx - 8, sy - 5, "k"),
        (sx - 9, sy - 6, "h"),
        (sx - 10, sy - 6, "w"),
        (sx - 11, sy - 6, "k"),
    ]
    for px, py, pcol in driver_slip:
        c.put(px, py, pcol)
        c.put(px, py + 1, "k")  # 划痕阴影

    # --------------------------------------------------------------------------
    # 2. 左上螺丝 (36, 24)：起子咬合磨损划口，槽角崩损
    # --------------------------------------------------------------------------
    tx, ty = 36, 24
    c.rect(tx - 1, ty - 1, tx + 1, ty + 1, "k")
    c.put(tx, ty, "n")
    c.put(tx - 2, ty, "w")  # 槽口外翻高光
    c.put(tx + 2, ty, "h")
    c.put(tx, ty - 2, "w")
    c.put(tx, ty + 2, "k")
    c.put(tx - 1, ty - 1, "l")
    c.put(tx + 1, ty + 1, "d")

    # --------------------------------------------------------------------------
    # 3. 左下螺丝 (36, 94)：被换成了一颗规格不一致的一字平头暗铜色旧螺丝
    # --------------------------------------------------------------------------
    lx, ly = 36, 94
    # 重新画一个完全不同的圆头一字锈螺丝（基调为暗铜色 'q'/'e'）
    c.rect(lx - 3, ly - 3, lx + 3, ly + 3, "e")
    c.rect(lx - 3, ly - 3, lx - 1, ly - 3, "q")
    c.rect(lx - 2, ly - 2, lx + 2, ly + 2, "q")
    c.rect(lx + 2, ly + 2, lx + 3, ly + 3, "c")
    # 一字槽（水平横槽，而不是十字孔）
    c.rect(lx - 2, ly, lx + 2, ly, "n")
    c.rect(lx - 2, ly - 1, lx + 2, ly - 1, "h")  # 一字边缘受光
    c.rect(lx - 2, ly + 1, lx + 2, ly + 1, "c")  # 一字边缘阴影

    return c


# ==============================================================================
# 导出字典
# ==============================================================================

CASES = {
    # 正面
    "gpu_big_ov_fan_dust1": fan_dust1,
    "gpu_big_ov_fan_dust2": fan_dust2,
    "gpu_big_ov_fan_chip": fan_chip,
    "gpu_big_ov_fan_oil": fan_oil,
    "gpu_big_ov_fin_dim": fin_dim,
    "gpu_big_ov_fin_oxide": fin_oxide,
    "gpu_big_ov_brk_scratch": brk_scratch,
    "gpu_big_ov_brk_port": brk_port,
    "gpu_big_ov_shroud_chip": shroud_chip,
    # 背面
    "gpu_big_ov_print_skew": print_skew,
    "gpu_big_ov_print_stamp": print_stamp,
    "gpu_big_ov_print_normal": print_normal,
    "gpu_big_ov_sticker_hand": sticker_hand,
    "gpu_big_ov_sticker_scratch": sticker_scratch,
    "gpu_big_ov_sticker_torn": sticker_torn,
    "gpu_big_ov_seal_missing": seal_missing,
    "gpu_big_ov_seal_reglued": seal_reglued,
    "gpu_big_ov_screw_opened": screw_opened,
}

__all__ = [
    "CASES",
    "print_overlay",
    "fan_dust1",
    "fan_dust2",
    "fan_chip",
    "fan_oil",
    "fin_dim",
    "fin_oxide",
    "brk_scratch",
    "brk_port",
    "shroud_chip",
    "print_skew",
    "print_stamp",
    "print_normal",
    "sticker_hand",
    "sticker_scratch",
    "sticker_torn",
    "seal_missing",
    "seal_reglued",
    "screw_opened",
]
