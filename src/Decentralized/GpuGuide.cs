using System;
using System.Collections.Generic;
using System.Linq;
using Decentralized.Domain;
using UnityEngine;

namespace Decentralized
{
    /// <summary>
    /// 显卡鉴定指南：游戏的纸质指南（PaperUI），版式和原版鉴定指南一致——笔记本背景、委员会页眉、居中标题、
    /// 插图加图注、“价格不变 / -n%”的折价标注、末页的鉴定示例。第一次打开时按当前语言生成并登记，语言变了重新生成。
    /// 坐标是设计单位（314×402，屏幕上放大 2 倍）。
    /// </summary>
    public static class GpuGuide
    {
        public const string BookId = "gpu_guide";
        private const float X = 35f, W = 253f, Top = 80f, Bottom = 368f;
        private const string Ink = "#221C1A", Good = "#3F7D4B", Bad = "#9E1F1A";

        private static string _locale;
        private static LoadedMod _mod;

        public static void OnAssetsLoaded(LoadedMod mod)
        {
            if (mod?.Manifest?.ID == ModInfo.Id) _mod = mod;
        }

        /// <summary>原版背景、按钮和页眉图按名字找不到时（纸张素材目录没打包），把内存里同名的原版精灵登记给本 mod。</summary>
        private static void ProvideMissingPaperSprites()
        {
            if (_mod == null) return;
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                string n = sprite.name;
                if (n != "notebook_manual" && n != "guide_logo" && !n.StartsWith("paper_buttons")) continue;
                if (_mod.PaperSprites.ContainsKey(n) || PaperAssets.FindInCatalog(n) != null) continue;
                _mod.PaperSprites[n] = sprite;
            }
        }

        public static void Open(int page = 0)
        {
            try
            {
                if (_locale != L.Code || !ModHelper.GetPaperBookIds(ModInfo.Id).Contains(BookId)) Register();
                var ui = ModHelper.OpenPaper(ModInfo.Id, BookId);
                if (ui == null) { Log.Warn("[GpuGuide] 打不开鉴定指南"); return; }
                ui.FlipTo(page);
                RaiseAboveModWindows(ui);
            }
            catch (Exception ex) { Log.Error("[GpuGuide] " + ex); }
        }

        /// <summary>纸质画布在 mod 窗口所在的层下面：从终端里打开时会被盖住，打开期间调到上面，关上时恢复。</summary>
        private static void RaiseAboveModWindows(PaperUI ui)
        {
            var canvas = ui.Root != null ? ui.Root.GetComponentInParent<Canvas>()?.rootCanvas : null;
            if (canvas == null || canvas.sortingOrder >= 950) return;
            int old = canvas.sortingOrder;
            canvas.sortingOrder = 950;
            void Restore(PaperUI u) { u.Closed -= Restore; if (canvas != null) canvas.sortingOrder = old; }
            ui.Closed += Restore;
        }

        // ================= 生成 =================

        private static void Register()
        {
            ProvideMissingPaperSprites();
            ModHelper.RemovePaperBooks(ModInfo.Id);
            var b = ModHelper.CreatePaper(ModInfo.Id, BookId, "notebook_manual");
            b.SetSize(628f, 804f);
            b.SetPosition(-450f, 0f);
            b.SetChrome();
            Page1(b); Page2(b); Page3(b); Page4(b); Page5(b); Page6(b); Page7(b); Page8(b); Page9(b);
            ModHelper.RegisterPaperBook(ModInfo.Id, b.Data);
            _locale = L.Code;
            Log.Info($"[GpuGuide] 已登记显卡鉴定指南（{b.Data.pages.Count} 页，{_locale}）");
        }

        private static string T(string key, params object[] a) => TuningText.Render(L.T(key, a));

        /// <summary>新的一页：原版的页眉（委员会印章字）和标题。</summary>
        private static float NewPage(PaperUI b)
        {
            b.AddPage(null, "guide_page");
            b.SetSlot("header", L.T("gg.header"));
            b.SetSlot("title", L.T("item.guide.name"));
            b.SetSlot("body", " ");
            return Top;
        }

        private static string Img(string name) => ModInfo.Id + ":" + name;

        private static TMPro.TextMeshProUGUI _measurer;

        private static float Measure(string text, string style, float size, float width)
        {
            if (_measurer == null)
            {
                var go = new GameObject("pd_gpu_guide_measure", typeof(RectTransform));
                UnityEngine.Object.DontDestroyOnLoad(go);
                _measurer = go.AddComponent<TMPro.TextMeshProUGUI>();
                _measurer.enableWordWrapping = true;
                _measurer.richText = true;
            }
            var st = PaperTextStyle.Get(style);
            var font = st.Font();
            if (font != null) _measurer.font = font;
            _measurer.fontSize = size > 0 ? size : st.size;
            float h = _measurer.GetPreferredValues(text, width, 0).y;
            return h > 1 ? h + 1.5f : (size > 0 ? size : st.size) * 1.5f * (1 + text.Split('\n').Length);
        }

        /// <summary>一段文字，返回下一块的 y。</summary>
        private static float Text(PaperUI b, float y, string text, string style = "body", string color = Ink, string align = "left", float gap = 5f, float x = X, float w = W)
        {
            float h = Measure(text, style, 0, w);
            b.AddText(x, y, w, h, text, style, align: align);
            b.Last.color = color;
            if (y + h > Bottom) Log.Warn($"[GpuGuide] 内容超出纸面 overflow：{y + h:0} > {Bottom}（{text.Substring(0, Math.Min(18, text.Length))}）");
            return y + h + gap;
        }

        /// <summary>小节标题（原版的蓝灰色粗体小标题）；标题太长折行时按折行后的高度往下推。</summary>
        private static float Section(PaperUI b, float y, string text)
        {
            float h = Mathf.Max(13f, Measure(text, "label", 8f, W));
            b.AddText(X, y, W, h, text, "label", fontSize: 8f);
            b.Last.color = "#2E5A78";
            return y + h + 2f;
        }

        /// <summary>一张图（底图加叠加层，同一个位置），居中，宽高是显示尺寸；返回图的底边。</summary>
        private static float Art(PaperUI b, float x, float y, float scale, params string[] layers)
        {
            float w = 192f * scale, h = 128f * scale;
            foreach (var l in layers)
            {
                b.AddImage(x, y, w, h, Img(l), "stretch");
            }
            return y + h;
        }

        /// <summary>正面底图：供电口类别（0 = 6 针，1 = 8 针，2 = 双 8 针）与风扇数（1 到 3）各自独立。</summary>
        private static string Front(string brand, int pin, int count) => $"gpu_big_{brand}_f_p{pin}n{count}";
        private static string Front(string brand, int tier) => Front(brand, Hardware.PinClass[tier], Hardware.FanCount[tier]);
        private static int Fans(int tier) => Hardware.FanCount[tier];

        private static void Caption(PaperUI b, float x, float y, float w, string text, string color = null)
        {
            b.AddText(x, y, w, 10, text, "caption", align: "center");
            if (color != null) b.Last.color = color;
        }

        /// <summary>背面贴纸上的序列号：贴纸在大图 (142, 91) 附近，随图的缩放摆放。</summary>
        private static void SerialOnSticker(PaperUI b, float ax, float ay, float scale, string serial)
        {
            float cx = ax + 142f * scale, cy = ay + 91f * scale;
            b.AddText(cx - 26f * scale, cy - 4.5f, 52f * scale, 9f, serial, "caption", align: "center", fontSize: 3.2f * scale / 0.78f);
            b.Last.color = "#1B1B1B";
        }

        /// <summary>剩余高度能放下的图缩放（图高 128 设计单位）：长语言文字多，图自动缩小，不超出纸面。</summary>
        private static float FitScale(float usedHeight, int count, float max, float min = 0.3f)
        {
            float avail = Bottom - Top - usedHeight;
            return Mathf.Clamp(avail / (128f * count), min, max);
        }

        private static string SampleSerial(string model, string chip = null, int kind = 0)
        {
            var u = new Unit { Model = model, FakeOf = chip, Health = 100, Look = new CardLook { Seed = 31337, Serial = kind } };
            return CardLabels.SerialText(u);
        }

        // ---- 1 概述 ----
        private static void Page1(PaperUI b)
        {
            float y = NewPage(b);
            string body = L.T("gg.p1.body"), makers = L.T("gg.p1.makers"), note = T("gg.p1.note");
            float used = Measure(body, "body", 0, W) + Measure(makers, "body", 0, W) + Measure(note, "body", 7f, W) + 6f + 14f + 8f + 14f;
            float scale = FitScale(used, 1, 0.9f);
            y = Text(b, y, body, gap: 6f);
            float x = X + (W - 192f * scale) / 2;
            float bottom = Art(b, x, y, scale, Front("probidia", 1));
            Caption(b, X, bottom + 1, W, L.T("gg.p1.cap"));
            y = bottom + 14f;
            y = Text(b, y, makers, gap: 8f);
            Text(b, y, note, "body", Good);
            b.Last.fontSize = 7f;
        }

        // ---- 2 显卡型号 ----
        private static void Page2(PaperUI b)
        {
            float y = NewPage(b);
            y = Section(b, y, L.T("gg.p2.h"));
            y = Text(b, y, L.T("gg.p2.body"), gap: 6f);
            float cw = W / 2;
            for (int row = 0; row < 2; row++)
            {
                float rowBottom = y;
                for (int col = 0; col < 2; col++)
                {
                    int tier = row * 2 + col;
                    float ax = X + col * cw + (cw - 120f) / 2;
                    float bottom = Art(b, ax, y, 0.625f, Front("probidia", tier));
                    Caption(b, X + col * cw, bottom, cw, L.T("gg.tier." + tier));
                    rowBottom = bottom + 12f;
                }
                y = rowBottom + 4f;
            }
        }

        // ---- 3 风扇数 ----
        private static void Page3(PaperUI b)
        {
            float y = NewPage(b);
            y = Section(b, y, L.T("gg.count.h"));
            string cb = L.T("gg.count.body");
            float cs = FitScale(15f + Measure(cb, "body", 0, W) + 6f + 3 * 15f, 3, 0.5f, 0.3f);
            y = Text(b, y, cb, gap: 6f);
            for (int n = 1; n <= 3; n++)
            {
                float bottom = Art(b, X + (W - 192f * cs) / 2, y, cs, Front("probidia", 1, n));
                Caption(b, X, bottom, W, L.T("lbl.count." + n));
                y = bottom + 15f;
            }
        }

        // ---- 4 供电口 ----
        private static void Page4(PaperUI b)
        {
            float y = NewPage(b);
            y = Section(b, y, L.T("gg.pin.h"));
            string pbd = L.T("gg.pin.body");
            float ps = FitScale(15f + Measure(pbd, "body", 0, W) + 6f + 3 * 15f, 3, 0.5f, 0.3f);
            y = Text(b, y, pbd, gap: 6f);
            for (int p = 0; p < 3; p++)
            {
                float bottom = Art(b, X + (W - 192f * ps) / 2, y, ps, Front("probidia", p, 2));
                Caption(b, X, bottom, W, L.T("lbl.pin." + p));
                y = bottom + 15f;
            }
        }

        // ---- 5 序列号 ----
        private static void Page5(PaperUI b)
        {
            float y = NewPage(b);
            y = Section(b, y, L.T("gg.serial.h"));
            string body = L.T("gg.serial.body"), pb = L.T("gg.serial.pb"), md = L.T("gg.serial.md"), rule = L.T("gg.serial.rule");
            float used = 15f + Measure(body, "body", 0, W) + Measure(pb, "body", 0, W) + Measure(md, "body", 0, W) + Measure(rule, "body", 0, W) + 6f + 14f + 4f + 8f;
            float scale = FitScale(used, 1, 0.78f);
            y = Text(b, y, body, gap: 6f);
            float ax = X + (W - 192f * scale) / 2;
            float bottom = Art(b, ax, y, scale, "gpu_big_probidia_b", "gpu_big_ov_print_normal_rtx80");
            SerialOnSticker(b, ax, y, scale, SampleSerial("RTX80"));
            Caption(b, X, bottom + 1, W, L.T("gg.serial.cap"));
            y = bottom + 14f;
            y = Text(b, y, pb, gap: 4f);
            y = Text(b, y, md, gap: 8f);
            Text(b, y, rule, "body");
        }

        // ---- 6 背面丝印 ----
        private static void Page6(PaperUI b)
        {
            float y = NewPage(b);
            y = Section(b, y, L.T("gg.print.h"));
            string note = L.T("gg.print.note");
            string[] kinds = { "normal", "stamp", "skew" };
            float scale = FitScale(15f + Measure(note, "body", 0, W) + 3 * 14f, 3, 0.5f, 0.3f);
            for (int s = 0; s < 3; s++)
            {
                string model = s == 1 ? "rtx60" : "rtx80";
                float ax = X + (W - 192f * scale) / 2;
                float bottom = Art(b, ax, y, scale, "gpu_big_probidia_b", $"gpu_big_ov_print_{kinds[s]}_{model}");
                SerialOnSticker(b, ax, y, scale, SampleSerial("RTX80"));
                Caption(b, X, bottom, W, L.T("gg.print." + s), s == 0 ? null : Bad);
                y = bottom + 14f;
            }
            Text(b, y, note, "body");
        }

        // ---- 7 叶片与金手指 ----
        private static void Page7(PaperUI b)
        {
            float y = NewPage(b);
            y = Section(b, y, L.T("gg.fan.h"));
            float cw = W / 3;
            float bottom = y;
            for (int s = 0; s < 3; s++)
            {
                var layers = new List<string> { Front("probidia", 1) };
                if (s == 1) layers.Add("gpu_big_ov_fan_dust2_" + Fans(1));
                if (s == 2) { layers.Add("gpu_big_ov_fan_dust1_" + Fans(1)); layers.Add("gpu_big_ov_fan_chip_" + Fans(1)); }
                float bx = Art(b, X + s * cw + (cw - 78f) / 2, y, 0.4f, layers.ToArray());
                Caption(b, X + s * cw, bx, cw, T("gg.fan." + s), s == 0 ? null : Bad);
                bottom = bx + 24f;
            }
            y = bottom + 10f;
            y = Section(b, y, L.T("gg.fin.h"));
            for (int s = 0; s < 3; s++)
            {
                var layers = new List<string> { Front("probidia", 1) };
                if (s == 1) layers.Add("gpu_big_ov_fin_dim");
                if (s == 2) layers.Add("gpu_big_ov_fin_oxide");
                float bx = Art(b, X + s * cw + (cw - 78f) / 2, y, 0.4f, layers.ToArray());
                Caption(b, X + s * cw, bx, cw, T("gg.fin." + s), s == 0 ? null : Bad);
                bottom = bx + 24f;
            }
            Text(b, bottom + 6f, L.T("gg.wear.note"), "body");
        }

        // ---- 8 挡板与封条 ----
        private static void Page8(PaperUI b)
        {
            float y = NewPage(b);
            y = Section(b, y, L.T("gg.brk.h"));
            float cw = W / 2;
            float bottom = y;
            for (int s = 0; s < 2; s++)
            {
                var layers = new List<string> { Front("probidia", 1) };
                if (s == 1) layers.Add("gpu_big_ov_brk_scratch");
                float bx = Art(b, X + s * cw + (cw - 120f) / 2, y, 0.625f, layers.ToArray());
                Caption(b, X + s * cw, bx, cw, T("gg.brk." + s), s == 0 ? null : Bad);
                bottom = bx + 12f;
            }
            y = bottom + 10f;
            y = Section(b, y, L.T("gg.seal.h"));
            bottom = y;
            for (int s = 0; s < 2; s++)
            {
                var layers = new List<string> { "gpu_big_probidia_b", "gpu_big_ov_print_normal_rtx70" };
                if (s == 1) layers.Add("gpu_big_ov_seal_missing");
                float ax = X + s * cw + (cw - 120f) / 2;
                float bx = Art(b, ax, y, 0.625f, layers.ToArray());
                SerialOnSticker(b, ax, y, 0.625f, SampleSerial("RTX70"));
                Caption(b, X + s * cw, bx, cw, T(s == 0 ? "gg.seal.0" : "gg.seal.2"), s == 0 ? null : Bad);
                bottom = bx + 12f;
            }
        }

        // ---- 9 指出 ----
        private static void Page9(PaperUI b)
        {
            float y = NewPage(b);
            y = Section(b, y, L.T("gg.point.h"));
            Text(b, y, T("gg.point.body"), "body");
            foreach (var e in Examples) ExamplePage(b, e);
        }

        // ---- 9 起：鉴定示例 ----
        private sealed class Ex { public string Title, Label, Chip; public int Health, Fan, Fingers, Bracket, Seal, CountBad, PinBad, SerialBad; }

        private static readonly Ex[] Examples =
        {
            new Ex { Title = "gg.ex.h", Label = "RTX80", Health = 60, Fan = 2, Fingers = 1, Seal = 2 },
            new Ex { Title = "gg.ex.h3", Label = "RX800", Health = 35, Fan = 2, Fingers = 2, Bracket = 1, Seal = 2 },
            new Ex { Title = "gg.ex.h2", Label = "RTX90", Chip = "RTX60", Health = 90, Fan = 1, CountBad = 1, SerialBad = 2 },
            new Ex { Title = "gg.ex.h4", Label = "RTX70", Health = 85, Fan = 1 },
            new Ex { Title = "gg.ex.h5", Label = "RX70", Health = 100 },
        };


        private static string Brand(string model) => Hardware.Specs[model].Brand.ToString();

        private static void ExamplePage(PaperUI b, Ex e)
        {
            float y = NewPage(b);
            y = Section(b, y, L.T(e.Title));
            string real = e.Chip ?? e.Label;
            var u = new Unit { Model = e.Label, FakeOf = e.Chip, Health = e.Health, Look = new CardLook { Seed = 31337, Fan = e.Fan, Fingers = e.Fingers, Bracket = e.Bracket, Sticker = e.Seal, CountBad = e.CountBad, PinBad = e.PinBad, Serial = e.SerialBad } };
            long baseV = CardValue.Of(real, e.Health);
            string brandKey = Hardware.Specs[real].Brand == "PMD" ? "pmd" : "probidia";
            var layers = new List<string> { Front(brandKey, u.ObservedPin, u.ObservedCount) };
            int fans = u.ObservedCount;
            if (e.Fan == 1) layers.Add("gpu_big_ov_fan_dust2_" + fans);
            if (e.Fan == 2) { layers.Add("gpu_big_ov_fan_dust1_" + fans); layers.Add("gpu_big_ov_fan_chip_" + fans); }
            if (e.Fingers == 1) layers.Add("gpu_big_ov_fin_dim");
            if (e.Fingers == 2) layers.Add("gpu_big_ov_fin_oxide");
            if (e.Bracket == 1) layers.Add("gpu_big_ov_brk_scratch");
            float bottom = Art(b, X + (W - 140f) / 2, y, 0.73f, layers.ToArray());
            y = bottom + 6f;
            string name = Hardware.Specs[e.Label].Brand + " " + Items.DisplayModel(e.Label);
            y = Text(b, y, L.T("gg.ex.card", name, e.Health), "body", align: "center", gap: 3f);
            y = Text(b, y, L.T("gg.ex.serial", CardLabels.SerialText(u)), "caption", Ink, "center", 8f);
            const float lx = X + 20f, lw = W - 40f;
            if (e.Chip != null)
            {
                y = Text(b, y, L.T("gg.ex.fake", name, Hardware.Specs[e.Chip].Brand + " " + Items.DisplayModel(e.Chip)), "caption", Bad, "center", 6f);
                y = Text(b, y, L.T("gg.ex.claimed", CardValue.Of(e.Label, e.Health)), "caption", Ink, "right", 3f, lx, lw);
            }
            y = Text(b, y, L.T("gg.ex.base", baseV), "caption", Ink, "right", 3f, lx, lw);
            int n = 0; long discSum = 0;
            long Disc(double p) { n++; long d = (long)Math.Round(baseV * p); discSum += d; return d; }
            if (e.Fan == 1) y = Text(b, y, L.T("gg.ex.fan1", Disc(Tuning.CardFanDust)), "caption", Bad, "right", 3f, lx, lw);
            if (e.Fan == 2) y = Text(b, y, L.T("gg.ex.fan", Disc(Tuning.CardFanChipped)), "caption", Bad, "right", 3f, lx, lw);
            if (e.Fingers == 1) y = Text(b, y, L.T("gg.ex.fin", Disc(Tuning.CardFingersDark)), "caption", Bad, "right", 3f, lx, lw);
            if (e.Fingers == 2) y = Text(b, y, L.T("gg.ex.fin2", Disc(Tuning.CardFingersOxidized)), "caption", Bad, "right", 3f, lx, lw);
            if (e.Bracket == 1) y = Text(b, y, L.T("gg.ex.brk", Disc(Tuning.CardBracketScratch)), "caption", Bad, "right", 3f, lx, lw);
            if (e.Seal == 2) y = Text(b, y, L.T("gg.ex.seal", Disc(Tuning.CardSealMissing)), "caption", Bad, "right", 3f, lx, lw);
            if (n == 0) y = Text(b, y, L.T("gg.ex.nodisc"), "caption", Good, "right", 3f, lx, lw);
            y += 3f;
            b.AddLine(lx, y, lx + lw, y, 1f, "#6A6458");
            y += 6f;
            // 外观写死（种子 0）才不会被健康度重算覆盖，价值按它算；序列号要种子，用上面带种子的那个
            long total = baseV - discSum;   // 页面上各项相加，和游戏的价值函数差不过 1（四舍五入）
            y = Text(b, y, L.T("gg.ex.total", total), "label", Ink, "right", 8f, lx, lw);
            if (e.Chip != null) y = Text(b, y, L.T("gg.ex.gap", CardValue.Of(e.Label, e.Health) - total), "caption", Bad, "right", 8f, lx, lw);
            Text(b, y, L.T("gg.ex.note"), "caption", Ink, "center");
        }
    }
}
