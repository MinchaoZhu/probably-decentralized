using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Decentralized
{
    /// <summary>
    /// PLEDGER 硬件风格公共皮肤与组件：
    /// - 机身：自画像素九宫格 ui_hw_shell（圆角石墨，左上受光边、右下背光边），3× 像素放大
    /// - 刻字：游戏 machinery 窗口的标题栏与分隔线设为透明；标题当机身刻字（灰色粗体、字距 18、居中）
    /// - 屏幕：黑色边框 ui_hw_bezel 包一块浅色墨水屏 ui_hw_paper（#D8DAD0），左右对称居中
    /// - 文字：深墨色 Ink，次要信息灰 _dim，警示暗红 _note
    /// - 按钮：黑底白字触控键 Key / KeyText；选中的项反色（白底黑框黑字加粗）加准星角标 ui_hw_sel，不用 [方括号]
    /// - 数据表：列右对齐，整行可点，选中行整行反色（黑底白字）
    /// - 滚动区：背景透明露出墨水屏底色，滚动条滑块为墨色
    /// </summary>
    public static class PledgerSkin
    {
        public static readonly Color Ink = new Color(0.12f, 0.12f, 0.14f);
        public static readonly Color Key = new Color(0.16f, 0.16f, 0.19f);
        public static readonly Color KeyText = new Color(0.93f, 0.93f, 0.90f);
        public static readonly Color Dim = new Color(0.38f, 0.39f, 0.40f);
        public static readonly Color Alert = new Color(0.62f, 0.16f, 0.12f);
        public static readonly Color Title = new Color(0.62f, 0.64f, 0.70f);
        public static readonly Color Paper = new Color(0.847f, 0.855f, 0.816f); // #D8DAD0
        public static readonly Color RowNormal = new Color(0f, 0f, 0f, 0.04f);
        public static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);
        public static readonly Color MaskClear = new Color(0f, 0f, 0f, 0.004f);
        public static readonly Color Separator = new Color(0.12f, 0.12f, 0.14f, 0.18f);

        /// <summary>透明化底图：若附带 Mask 组件则保留极微小 alpha（0.004），避免 Mask 模板 alpha 为 0 导致子内容被整裁。</summary>
        public static void SetClear(Image img)
        {
            if (img == null) return;
            img.color = (img.GetComponent<Mask>() != null) ? MaskClear : Transparent;
        }

        private static readonly Dictionary<string, Sprite> Sliced = new Dictionary<string, Sprite>();

        /// <summary>把本 mod 的像素图做成九宫格贴到 Image 上：边角 border 像素不拉伸，一个像素放大成 3 个界面单位。</summary>
        public static void Slice(Image img, string key, int border)
        {
            var src = Items.ModSprite(key);
            if (img == null || src == null) return;
            if (!Sliced.TryGetValue(key, out var sp))
            {
                src.texture.filterMode = FilterMode.Point;
                sp = Sprite.Create(src.texture, src.rect, new Vector2(0.5f, 0.5f), src.pixelsPerUnit, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
                Sliced[key] = sp;
            }
            img.sprite = sp;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 100f / (3f * sp.pixelsPerUnit);
            img.color = Color.white;
        }

        /// <summary>把输入框切片为墨水屏凹槽：浅色底、1 像素深墨边框（1:1 像素映射）。</summary>
        /// <summary>圆角触控键：白色的九宫格底图按颜色着色（黑底白字键、选中行里的浅色键共用同一张图）。</summary>
        public static void KeyShape(Image img, Color color)
        {
            Slice(img, "ui_hw_key", 4);
            if (img != null) img.color = color;
        }

        public static void SliceGroove(Image img)
        {
            if (img == null) return;
            if (!Sliced.TryGetValue("ui_hw_groove", out var sp))
            {
                var src = Items.ModSprite("ui_hw_groove");
                if (src != null)
                {
                    src.texture.filterMode = FilterMode.Point;
                    sp = Sprite.Create(src.texture, src.rect, new Vector2(0.5f, 0.5f), src.pixelsPerUnit, 0, SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
                }
                else
                {
                    var tex = new Texture2D(12, 12, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                    for (int y = 0; y < 12; y++)
                        for (int x = 0; x < 12; x++)
                            tex.SetPixel(x, y, (x == 0 || x == 11 || y == 0 || y == 11) ? Ink : Paper);
                    tex.Apply();
                    sp = Sprite.Create(tex, new Rect(0, 0, 12, 12), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
                }
                Sliced["ui_hw_groove"] = sp;
            }
            img.sprite = sp;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 100f / sp.pixelsPerUnit;
            img.color = Color.white;
        }

        /// <summary>屏上的选择按钮（币种、档位、子页）：选中的一个画成白底黑框黑字、四角带准星角标。</summary>
        public static void Pick(CustomUIBuilder b, string text, bool selected, Action onClick, ICollection<GameObject> picked)
        {
            b.AddButton(text, onClick);
            if (selected && picked != null) picked.Add(b.Last.root);
        }

        /// <summary>数据表行定义：整行高亮与点击回调。</summary>
        public sealed class RowDef
        {
            public GameObject Root;
            public Image Image;
            public bool Selected;
            public Action OnClick;
        }

        /// <summary>数据表的一行：列右对齐（第一列默认左对齐），整行可点击，选中行反色。</summary>
        public static void TableRow(CustomUIBuilder b, float totalW, float height, float[] widths, TextAnchor[] aligns, string[] texts, bool selected, Action onClick, ICollection<RowDef> rows, ICollection<Image> clear)
        {
            b.BeginPanel(totalW, height, TextAnchor.MiddleLeft, 6f, 0f);
            var root = b.Last.root;
            var img = b.Last.image;
            if (rows != null) rows.Add(new RowDef { Root = root, Image = img, Selected = selected, OnClick = onClick });
            b.BeginRow(0f, TextAnchor.MiddleLeft);
            for (int i = 0; i < widths.Length; i++)
            {
                float w = widths[i];
                var align = (aligns != null && i < aligns.Length) ? aligns[i] : (i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
                b.BeginPanel(w, height - 6f, align, 0f, 0f);
                if (clear != null) clear.Add(b.Last.image);
                b.AddLabel(i < texts.Length ? texts[i] : "");
                FitCurrent(b);
                b.End();
            }
            b.End();
            b.End();
        }

        /// <summary>数据表表头：与表行同对齐方式，加粗，无底色。</summary>
        public static void TableHeader(CustomUIBuilder b, float totalW, float height, float[] widths, TextAnchor[] aligns, string[] headers, ICollection<Image> clear)
        {
            b.BeginPanel(totalW, height, TextAnchor.MiddleLeft, 6f, 0f);
            if (clear != null) clear.Add(b.Last.image);
            b.BeginRow(0f, TextAnchor.MiddleLeft);
            for (int i = 0; i < widths.Length; i++)
            {
                float w = widths[i];
                var align = (aligns != null && i < aligns.Length) ? aligns[i] : (i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
                b.BeginPanel(w, height - 6f, align, 0f, 0f);
                if (clear != null) clear.Add(b.Last.image);
                b.AddLabel(i < headers.Length ? "<b>" + headers[i] + "</b>" : "");
                FitCurrent(b);
                b.End();
            }
            b.End();
            b.End();
        }

        /// <summary>单行固定宽度的文字放不下时先缩小字号（最小 68%），再省略：各语言译文长度不同，不靠逐语言调列宽。</summary>
        public static void Fit(TMP_Text t)
        {
            if (t == null) return;
            t.enableWordWrapping = false;
            t.enableAutoSizing = true;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = Mathf.Round(t.fontSize * 0.68f);
            t.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>刚加的标签限定在固定高度内（可折行），放不下时缩小字号：商品名等多语言下长度差很大的标题用。</summary>
        public static void FitBox(CustomUIBuilder b, float height)
        {
            var c = b.CurrentContainer;
            if (c == null || c.childCount == 0) return;
            var t = c.GetChild(c.childCount - 1).GetComponentInChildren<TextMeshProUGUI>(true);
            if (t == null) return;
            var le = t.gameObject.GetComponent<LayoutElement>() ?? t.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height; le.preferredHeight = height; le.flexibleHeight = 0f;
            t.enableAutoSizing = true;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = Mathf.Round(t.fontSize * 0.7f);
            t.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>对当前容器（刚加完标签的单元面板）里的文字做 Fit。</summary>
        public static void FitCurrent(CustomUIBuilder b)
        {
            if (b.CurrentContainer == null) return;
            foreach (var t in b.CurrentContainer.GetComponentsInChildren<TextMeshProUGUI>(true)) Fit(t);
        }

        /// <summary>应用整套 PLEDGER 皮肤：机身、边框、墨水屏、刻字、文字颜色、触控键、滚动条与数据表行。</summary>
        public static void Apply(CustomUIWindow win, RectTransform screen, Image bezel, Image paper, ICollection<GameObject> picked, ICollection<Image> clear, IEnumerable<RowDef> rows = null, float fontScale = 1f, bool frame = true)
        {
            if (win?.root == null) return;
            if (frame) ApplyFrame(win, bezel, paper);
            if (screen == null) return;
            ApplyScreen(screen, picked, clear, rows, fontScale);
        }

        /// <summary>窗口外壳：机身、黑边、纸面、标题栏刻字。只在窗口新建时做一次。</summary>
        private static void ApplyFrame(CustomUIWindow win, Image bezel, Image paper)
        {
            Slice(win.root.GetComponent<Image>(), "ui_hw_shell", 10);
            if (bezel != null) Slice(bezel, "ui_hw_bezel", 5);
            if (paper != null) Slice(paper, "ui_hw_paper", 3);

            // 标题栏和分隔线透明，标题当机身刻字
            foreach (var name in new[] { "header", "rule" })
            {
                var img = win.root.transform.Find(name)?.GetComponent<Image>();
                if (img != null) SetClear(img);
            }
            if (win.frame?.titleText != null)
            {
                var t = win.frame.titleText;
                t.color = Title;
                t.fontStyle = FontStyles.Bold;
                t.characterSpacing = 18f;
                t.fontSize = 16f;
                t.horizontalAlignment = HorizontalAlignmentOptions.Center;
            }
            var content = win.frame?.contentRoot?.GetComponent<VerticalLayoutGroup>();
            if (content != null) content.childAlignment = TextAnchor.UpperCenter;
        }

        /// <summary>屏幕内的样式：文字、输入框、分隔线、按钮、滚动条、表格行。内容刷新时只对新建的这块调用。</summary>
        private static void ApplyScreen(RectTransform screen, ICollection<GameObject> picked, ICollection<Image> clear, IEnumerable<RowDef> rows, float fontScale)
        {
            if (fontScale > 1.001f)
                foreach (var t in screen.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    t.fontSize *= fontScale;
                    if (t.enableAutoSizing) { t.fontSizeMax *= fontScale; t.fontSizeMin *= fontScale; }
                }

            // 收集所有表格行 root，避免按钮样式覆盖整行
            var rowRoots = new HashSet<GameObject>();
            if (rows != null)
            {
                foreach (var r in rows)
                    if (r?.Root != null) rowRoots.Add(r.Root);
            }

            // 屏上文字：note 暗红，dim 灰，其余深墨色
            foreach (var t in screen.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (t.GetComponentInParent<Button>() != null || t.GetComponentInParent<TMP_InputField>() != null) continue;
                t.color = t.gameObject.name.Contains("_note") ? Alert : t.gameObject.name.Contains("_dim") ? Dim : Ink;
            }

            // 屏上输入框：墨水屏凹槽样式（浅色底、深墨 1px 边、深墨文字、灰色占位符）
            foreach (var inp in screen.GetComponentsInChildren<TMP_InputField>(true))
            {
                var bg = inp.GetComponent<Image>() ?? inp.targetGraphic as Image;
                if (bg != null) SliceGroove(bg);
                if (inp.textComponent != null) inp.textComponent.color = Ink;
                if (inp.placeholder is TextMeshProUGUI ph) ph.color = Dim;
            }

            // 分隔线：墨水屏上为淡墨色细线
            foreach (var img in screen.GetComponentsInChildren<Image>(true))
            {
                if (img.gameObject.name.Contains("separator"))
                {
                    img.sprite = MachineryStyle.White;
                    img.color = Separator;
                }
            }

            // 屏上普通按钮与选择按钮
            foreach (var btn in screen.GetComponentsInChildren<Button>(true))
            {
                if (rowRoots.Contains(btn.gameObject)) continue;
                bool on = picked != null && picked.Contains(btn.gameObject);
                var img = btn.GetComponent<Image>();
                if (img != null)
                {
                    if (on)
                    {
                        Slice(img, "ui_hw_sel", 5);
                        btn.transition = Selectable.Transition.None;
                    }
                    else
                    {
                        KeyShape(img, Key);
                    }
                }
                foreach (var t in btn.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    Fit(t);
                    t.color = on ? Ink : KeyText;
                    if (on) t.fontStyle |= FontStyles.Bold;
                }
            }

            // 滚动视图：背景全透明露出墨水屏底色，滑块墨色
            foreach (var scroll in screen.GetComponentsInChildren<ScrollRect>(true))
            {
                var bg = scroll.GetComponent<Image>();
                if (bg != null) SetClear(bg);
                if (scroll.viewport != null)
                {
                    var vpImg = scroll.viewport.GetComponent<Image>();
                    // 视口的图是 Mask 的模板：alpha 为 0 会把整个内容裁掉，只能压到几乎看不见
                    if (vpImg != null) SetClear(vpImg);
                }
                if (scroll.verticalScrollbar != null)
                {
                    var sb = scroll.verticalScrollbar;
                    var sbrt = sb.transform as RectTransform;
                    if (sbrt != null) sbrt.sizeDelta = new Vector2(6f, sbrt.sizeDelta.y);
                    foreach (var img in sb.GetComponentsInChildren<Image>(true))
                    {
                        if (sb.handleRect != null && (img.gameObject == sb.handleRect.gameObject || img.transform.IsChildOf(sb.handleRect)))
                            continue;
                        SetClear(img);
                    }
                    if (sb.handleRect != null)
                    {
                        sb.handleRect.sizeDelta = new Vector2(0f, sb.handleRect.sizeDelta.y);
                        var handleImg = sb.handleRect.GetComponent<Image>();
                        if (handleImg != null)
                        {
                            handleImg.sprite = MachineryStyle.White;
                            handleImg.type = Image.Type.Simple;
                            handleImg.color = Ink;
                        }
                    }
                }
                if (scroll.horizontalScrollbar != null)
                {
                    var sb = scroll.horizontalScrollbar;
                    var sbrt = sb.transform as RectTransform;
                    if (sbrt != null) sbrt.sizeDelta = new Vector2(sbrt.sizeDelta.x, 6f);
                    foreach (var img in sb.GetComponentsInChildren<Image>(true))
                    {
                        if (sb.handleRect != null && (img.gameObject == sb.handleRect.gameObject || img.transform.IsChildOf(sb.handleRect)))
                            continue;
                        SetClear(img);
                    }
                    if (sb.handleRect != null)
                    {
                        sb.handleRect.sizeDelta = new Vector2(sb.handleRect.sizeDelta.x, 0f);
                        var handleImg = sb.handleRect.GetComponent<Image>();
                        if (handleImg != null)
                        {
                            handleImg.sprite = MachineryStyle.White;
                            handleImg.type = Image.Type.Simple;
                            handleImg.color = Ink;
                        }
                    }
                }
            }

            // 清理辅助 Panel 底图与任何残留原生灰色边框的容器
            if (clear != null)
            {
                foreach (var img in clear)
                    if (img != null) SetClear(img);
            }
            foreach (var img in screen.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite == MachineryStyle.BevelFrame) SetClear(img);
            }

            // 数据表行：反色高亮与整行点击
            if (rows != null)
            {
                foreach (var r in rows)
                {
                    if (r?.Root == null || r.Image == null) continue;
                    r.Image.sprite = MachineryStyle.White;
                    r.Image.type = Image.Type.Simple;
                    r.Image.raycastTarget = true;
                    r.Image.color = r.Selected ? Ink : RowNormal;
                    foreach (var t in r.Root.GetComponentsInChildren<TextMeshProUGUI>(true))
                    {
                        // 避免修改行内子按钮的文字颜色
                        var pBtn = t.GetComponentInParent<Button>();
                        if (pBtn != null && pBtn.gameObject != r.Root) continue;
                        t.color = r.Selected ? KeyText : Ink;
                    }
                    if (r.Selected)
                    {
                        // 选中行整行反色（深墨底）：行内的操作按钮反转为浅色底、深墨字
                        foreach (var cBtn in r.Root.GetComponentsInChildren<Button>(true))
                        {
                            if (cBtn.gameObject == r.Root) continue;
                            cBtn.transition = Selectable.Transition.None;
                            var img = cBtn.GetComponent<Image>();
                            if (img != null)
                            {
                                KeyShape(img, KeyText);
                            }
                            foreach (var t in cBtn.GetComponentsInChildren<TextMeshProUGUI>(true))
                            {
                                t.color = Ink;
                            }
                        }
                    }
                    if (r.OnClick != null)
                    {
                        var btn = r.Root.GetComponent<Button>() ?? r.Root.AddComponent<Button>();
                        btn.transition = Selectable.Transition.None;
                        btn.onClick.RemoveAllListeners();
                        var act = r.OnClick;
                        btn.onClick.AddListener(() => act());
                    }
                }
            }
        }
    }
}
