using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Decentralized.Domain;
using UnityEngine;

namespace Decentralized
{
    /// <summary>
    /// 日 K 图：运行时画到一张小 Texture2D 上（一根蜡烛 5 像素宽、间隔 1），点采样放大 2 倍显示，和游戏的像素风格一致。
    /// 上面是价格（涨绿跌红、影线、7 日均线、可选阈值虚线），下面是成交量（玩家青色叠在大户灰色上）。纹理按用途缓存复用。
    /// </summary>
    public static class CandleChart
    {
        /// <summary>画图的币种：全部可交易资产。</summary>
        public static readonly string[] ChartAssets = { Assets.PSC, Assets.SPARK, Assets.CRT, Assets.CRA, Assets.CRC, Assets.Ponero };

        private static double VolShare(string a) =>
            a == Assets.PSC ? Tuning.ChartVolPsc : a == Assets.CRC ? Tuning.ChartVolCrc : a == Assets.CRT ? Tuning.ChartVolCrt :
            a == Assets.CRA ? Tuning.ChartVolCra : a == Assets.SPARK ? Tuning.ChartVolSpark : Tuning.ChartVolPonero;

        /// <summary>开门：每个币开一根蜡烛（开盘接昨天收盘，夜里的价格变化算进来）。</summary>
        public static void Morning(SaveData d, int day)
        {
            foreach (var a in ChartAssets) d.Candles.Open(a, day, Session.Price(a));
        }

        /// <summary>白天价格变了（不计成交）。</summary>
        public static void Touch(SaveData d, int day, params string[] assets)
        {
            foreach (var a in assets) d.Candles.Tick(a, day, Session.Price(a));
        }

        /// <summary>玩家的一笔成交：按信用点计进这个币当天的成交量。</summary>
        public static void Trade(string asset, double credits)
        {
            var d = Session.Data;
            if (d == null || Array.IndexOf(ChartAssets, asset) < 0 || credits <= 0) return;
            d.Candles.Tick(asset, StoreStation.GetDayCounter(), Session.Price(asset), credits, true);
        }

        /// <summary>夜间结算：补上当天 NPC 的成交量，收盘。在价格的夜间更新之前调用。</summary>
        public static void Close(SaveData d, int day)
        {
            double depth = Tuning.PoolDepth(day);
            foreach (var a in ChartAssets)
            {
                double p = Session.Price(a);
                var c = d.Candles.Today(a, day, p);
                double move = c.O > 0 ? Math.Abs(p / c.O - 1) + (c.H - c.L) / c.O : 0;
                d.Candles.Tick(a, day, p, CandleBook.NpcVolume(depth, VolShare(a), move, d.Rng));
                d.Candles.Close(a, day, p);
            }
        }

        private static readonly Dictionary<string, string> Picked = new Dictionary<string, string>();

        public const int Days = 30, Step = 6, Body = 5, PriceH = 70, Gap = 3, VolH = 16, Pad = 2, AxisW = 22, DateH = 7;
        public const int W = Days * Step + Pad * 2 + AxisW, H = PriceH + Gap + VolH + Pad * 2 + DateH;
        public static float Scale => TerminalWindow.ChartScale;

        private static readonly Color32 Bg = new Color32(30, 31, 34, 255), Grid = new Color32(48, 50, 54, 255);
        private static readonly Color32 Up = new Color32(108, 190, 112, 255), Down = new Color32(214, 94, 88, 255);
        private static readonly Color32 Ma = new Color32(226, 196, 92, 255), Thr = new Color32(150, 170, 230, 255);
        private static readonly Color32 VolNpc = new Color32(92, 96, 104, 255), VolMe = new Color32(96, 200, 210, 255);
        private static readonly Color32 Txt = new Color32(150, 154, 160, 255), TagBg = new Color32(226, 196, 92, 255), TagTxt = new Color32(30, 31, 34, 255);

        /// <summary>3×5 像素数字字体（每行 3 位，自上而下）。</summary>
        private static readonly Dictionary<char, string[]> Glyph = new Dictionary<char, string[]>
        {
            ['0'] = new[] { "111", "101", "101", "101", "111" }, ['1'] = new[] { "010", "110", "010", "010", "111" },
            ['2'] = new[] { "111", "001", "111", "100", "111" }, ['3'] = new[] { "111", "001", "011", "001", "111" },
            ['4'] = new[] { "101", "101", "111", "001", "001" }, ['5'] = new[] { "111", "100", "111", "001", "111" },
            ['6'] = new[] { "111", "100", "111", "101", "111" }, ['7'] = new[] { "111", "001", "010", "010", "010" },
            ['8'] = new[] { "111", "101", "111", "101", "111" }, ['9'] = new[] { "111", "101", "111", "001", "111" },
            ['.'] = new[] { "0", "0", "0", "0", "1" }, ['-'] = new[] { "000", "000", "111", "000", "000" },
            ['k'] = new[] { "100", "101", "110", "101", "101" },
        };

        private static int TextW(string s) => s.Sum(ch => Glyph.TryGetValue(ch, out var g) ? g[0].Length + 1 : 0) - 1;

        /// <summary>在 (x, y) 处写字，y 是字的底边。</summary>
        private static void Text(Color32[] px, int x, int y, string s, Color32 c)
        {
            foreach (char ch in s)
            {
                if (!Glyph.TryGetValue(ch, out var g)) continue;
                for (int r = 0; r < 5; r++)
                    for (int k = 0; k < g[r].Length; k++)
                        if (g[r][k] == '1') Set(px, x + k, y + 4 - r, c);
                x += g[0].Length + 1;
            }
        }

        /// <summary>刻度上的价格：大于 100 不带小数，越小小数越多，最多 5 个字符左右。</summary>
        private static string Num(double p)
        {
            if (p >= 10000) return (p / 1000).ToString("0", CultureInfo.InvariantCulture) + "k";
            if (p >= 100) return p.ToString("0", CultureInfo.InvariantCulture);
            if (p >= 10) return p.ToString("0.0", CultureInfo.InvariantCulture);
            if (p >= 1) return p.ToString("0.00", CultureInfo.InvariantCulture);
            return p.ToString("0.000", CultureInfo.InvariantCulture).Substring(1);
        }

        private sealed class Slot { public Texture2D Tex; public Sprite Sprite; public Color32[] Px; }
        private static readonly Dictionary<string, Slot> Slots = new Dictionary<string, Slot>();

        /// <summary>画一张图，返回可以交给 AddImage 的 Sprite。threshold &gt; 0 时画一条水平虚线（预测盘的阈值）。</summary>
        public static Sprite Render(string key, IList<Candle> all, double threshold = 0)
        {
            if (!Slots.TryGetValue(key, out var s) || s.Tex == null)
            {
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                s = Slots[key] = new Slot { Tex = tex, Px = new Color32[W * H], Sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f) };
            }
            var px = s.Px;
            for (int i = 0; i < px.Length; i++) px[i] = Bg;
            var c = all.Skip(Math.Max(0, all.Count - Days)).ToList();
            int volY0 = Pad + DateH, priceY0 = volY0 + VolH + Gap, plotR = Pad + Days * Step, axisX = plotR + 2;
            // 网格：价格区四等分
            for (int g = 1; g < 4; g++) HLine(px, Pad, plotR - 1, priceY0 + PriceH * g / 4, Grid);
            VLine(px, plotR, volY0, priceY0 + PriceH - 1, Grid);
            if (c.Count > 0)
            {
                var ma = CandleBook.Ma(all, 7).Skip(Math.Max(0, all.Count - Days)).ToList();
                double lo = Math.Min(c.Min(x => x.L), ma.Min()), hi = Math.Max(c.Max(x => x.H), ma.Max());
                if (threshold > 0) { lo = Math.Min(lo, threshold); hi = Math.Max(hi, threshold); }
                double span = Math.Max(hi - lo, hi * 0.02);
                lo -= span * 0.05; hi += span * 0.05; span = hi - lo;
                Func<double, int> y = p => priceY0 + (int)Math.Round((p - lo) / span * (PriceH - 1));
                double vmax = Math.Max(1, c.Max(x => x.VolPlayer + x.VolNpc));
                int x0 = Pad + (Days - c.Count) * Step;   // 不足 30 根时靠右
                // 均线先画，蜡烛盖在上面（贴着锚的稳定币不会被均线遮住）
                for (int i = 1; i < ma.Count; i++) Line(px, x0 + (i - 1) * Step + Body / 2, y(ma[i - 1]), x0 + i * Step + Body / 2, y(ma[i]), Ma);
                for (int i = 0; i < c.Count; i++)
                {
                    var k = c[i];
                    int left = x0 + i * Step, mid = left + Body / 2;
                    var col = k.C >= k.O ? Up : Down;
                    VLine(px, mid, y(k.L), y(k.H), col);
                    int b0 = y(Math.Min(k.O, k.C)), b1 = Math.Max(b0, y(Math.Max(k.O, k.C)));
                    for (int xx = left; xx < left + Body; xx++)
                    {
                        // 当天还没收盘的蜡烛画成空心
                        if (!k.Closed && xx > left && xx < left + Body - 1 && b1 - b0 > 1) { Set(px, xx, b0, col); Set(px, xx, b1, col); continue; }
                        VLine(px, xx, b0, b1, col);
                    }
                    int hn = (int)Math.Round(k.VolNpc / vmax * VolH), hm = (int)Math.Round(k.VolPlayer / vmax * VolH);
                    for (int xx = left; xx < left + Body; xx++)
                    {
                        if (hn > 0) VLine(px, xx, volY0, volY0 + hn - 1, VolNpc);
                        if (hm > 0) VLine(px, xx, volY0 + hn, volY0 + hn + hm - 1, VolMe);
                    }
                }
                if (threshold > 0) { int ty = y(threshold); for (int xx = Pad; xx < plotR; xx++) if ((xx / 3) % 2 == 0) Set(px, xx, ty, Thr); }
                // 右侧刻度：三条网格线的价格；最新收盘价标成黄底的价签，和网格刻度挤在一起时让开
                int lastY = y(c[c.Count - 1].C);
                for (int g = 1; g < 4; g++)
                {
                    int gy = priceY0 + PriceH * g / 4;
                    if (Math.Abs(gy - lastY) < 7) continue;
                    double gp = lo + (gy - priceY0) / (double)(PriceH - 1) * span;
                    Text(px, axisX, gy - 2, Num(gp), Txt);
                }
                if (threshold > 0) { int ty = y(threshold); if (Math.Abs(ty - lastY) >= 7) Text(px, axisX, ty - 2, Num(threshold), Thr); }
                string tag = Num(c[c.Count - 1].C);
                int ty0 = Math.Max(priceY0, Math.Min(priceY0 + PriceH - 7, lastY - 3));
                for (int yy = ty0; yy < ty0 + 7; yy++) HLine(px, axisX - 1, Math.Min(W - 1, axisX + TextW(tag)), yy, TagBg);
                Text(px, axisX, ty0 + 1, tag, TagTxt);
                // 底部日期：天数是 5 的倍数的蜡烛下面写天数
                for (int i = 0; i < c.Count; i++)
                {
                    if (c[i].Day % 5 != 0) continue;
                    string ds = c[i].Day.ToString(CultureInfo.InvariantCulture);
                    int cx = x0 + i * Step + Body / 2;
                    VLine(px, cx, volY0 - 1, volY0 - 1, Txt);
                    Text(px, Math.Max(0, Math.Min(plotR - TextW(ds), cx - TextW(ds) / 2)), Pad - 1, ds, Txt);
                }
                // 成交量区右上：这一屏最大的量
                Text(px, axisX, volY0 + VolH - 5, Num(vmax), VolNpc);
            }
            s.Tex.SetPixels32(px);
            s.Tex.Apply(false);
            return s.Sprite;
        }

        /// <summary>图上方的一行说明：最高、最低、最新收盘，涨跌。</summary>
        public static string Caption(string asset, IList<Candle> all)
        {
            var c = all.Skip(Math.Max(0, all.Count - Days)).ToList();
            if (c.Count == 0) return L.T("chart.empty", asset);
            var last = c[c.Count - 1];
            double prev = c.Count > 1 ? c[c.Count - 2].C : last.O, chg = prev > 0 ? last.C / prev - 1 : 0;
            return L.T("chart.caption", asset, c.Count, Fmt.Num(c.Max(x => x.H)), Fmt.Num(c.Min(x => x.L)), Fmt.Num(last.C), (chg * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture));
        }

        private static void Set(Color32[] px, int x, int y, Color32 c) { if (x >= 0 && x < W && y >= 0 && y < H) px[y * W + x] = c; }
        private static void HLine(Color32[] px, int x0, int x1, int y, Color32 c) { for (int x = x0; x <= x1; x++) Set(px, x, y, c); }
        private static void VLine(Color32[] px, int x, int y0, int y1, Color32 c) { if (y1 < y0) (y0, y1) = (y1, y0); for (int y = y0; y <= y1; y++) Set(px, x, y, c); }

        private static void Line(Color32[] px, int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            while (true)
            {
                Set(px, x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>把一个币的图放进界面：说明一行、黑框包裹的图、图例一行。</summary>
        public static void Add(CustomUIBuilder b, string key, string asset, double threshold = 0)
        {
            var all = Session.Data.Candles.Of(asset);
            b.AddLabel(Caption(asset, all), "chart_caption_dim");
            b.BeginPanel(W * Scale + 4f, H * Scale + 4f, TextAnchor.MiddleCenter, 2f, 0f, "chart_bezel");
            PledgerSkin.Slice(b.Last.image, "ui_hw_bezel", 5);
            b.AddImage(Render(key, all, threshold), W * Scale, H * Scale);
            b.End();
            b.AddLabel(L.T(threshold > 0 ? "chart.legend.thr" : "chart.legend"), "chart_legend_dim");
        }

        /// <summary>带币种切换的图：上面一排币种按钮（选中项带准星角标），切换后重画。</summary>
        public static void AddPicker(CustomUIBuilder b, string key, string fallback, Action refresh, ICollection<GameObject> picked = null)
        {
            var list = ChartAssets.Where(a => a != Assets.Ponero || Unlock.BlackUnlocked).ToList();
            if (!Picked.TryGetValue(key, out var cur) || !list.Contains(cur)) cur = fallback;
            b.BeginRow(4f);
            foreach (var a in list)
            {
                string asset = a;
                PledgerSkin.Pick(b, asset, asset == cur, () => { Picked[key] = asset; refresh(); }, picked);
            }
            b.End();
            Add(b, key, cur);
        }
    }
}
