using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>Web3 手册阅读窗口：左边章节列表，右边章节全文。章节在本地化文本里用 § 分隔，章节名取第一个冒号或括号之前。</summary>
    public static class HandbookWindow
    {
        public const string Id = "pd_handbook";
        private const float WinW = 828f, WinH = 680f, ScreenW = 768f, ScreenH = 578f, Bezel = 6f;
        private static float ListW => L.Wide ? 276f : 190f;
        private static float RowW => L.Wide ? ListW - 52f : ListW - 12f;
        private static float TextW => L.Wide ? 456f : 540f;

        private static int _chapter;
        private static readonly List<GameObject> _picked = new List<GameObject>();
        private static readonly List<Image> _clear = new List<Image>();
        private static readonly List<PledgerSkin.RowDef> _rows = new List<PledgerSkin.RowDef>();

        private static string Title(string chapter)
        {
            int end = chapter.Length;
            foreach (var m in new[] { "：", ": ", "（", " (" })
            {
                int i = chapter.IndexOf(m, StringComparison.Ordinal);
                if (i > 0 && i < end) end = i;
            }
            return chapter.Substring(0, end).TrimEnd();
        }

        private static string Body(string chapter)
        {
            foreach (var m in new[] { "：", ": " })
            {
                int i = chapter.IndexOf(m, StringComparison.Ordinal);
                int p = chapter.IndexOf("（", StringComparison.Ordinal);
                if (i > 0 && (p < 0 || i < p || p > 40)) return chapter.Substring(i + m.Length);
            }
            return chapter;
        }

        private static string _key = "guide.web3.text", _nameKey = "guide.web3.title";
        private static readonly Dictionary<string, int> _chapterOf = new Dictionary<string, int>();

        public static void Open() => Show("guide.web3.text", "guide.web3.title");

        private static void Show(string key, string nameKey)
        {
            _key = key; _nameKey = nameKey;
            _chapter = _chapterOf.TryGetValue(key, out var ch0) ? ch0 : 0;
            Reopen();
        }

        private static void Reopen()
        {
            _chapterOf[_key] = _chapter;
            var chapters = TuningText.Render(L.T(_key)).Replace("¶", "\n\n").Split('§');
            if (_chapter >= chapters.Length) _chapter = 0;
            var b = CustomUIManager.Window(Id, L.T(_nameKey));
            if (b == null) return;
            _picked.Clear(); _clear.Clear(); _rows.Clear();
            b.SetSize(WinW, WinH).SetDraggable(true).SetCloseOnEscape(true).UseMachineryStyle();
            b.BeginPanel(ScreenW + Bezel * 2, ScreenH + Bezel * 2, TextAnchor.MiddleCenter, Bezel + 3f, 0f);
            var bezel = b.Last.image;
            b.BeginPanel(ScreenW, ScreenH, TextAnchor.UpperLeft, 12f, 4f);
            var screen = b.CurrentContainer;
            var paper = b.Last.image;
            b.BeginRow(10f);
            b.BeginPanel(ListW, -1f, TextAnchor.UpperLeft, 0f, 0f);
            _clear.Add(b.Last.image);
            b.BeginScroll(ScreenH - 30f);
            _clear.Add(b.Last.image);
            for (int i = 0; i < chapters.Length; i++)
            {
                int k = i;
                PledgerSkin.TableRow(b, RowW, 30f, new[] { RowW }, new[] { TextAnchor.MiddleLeft },
                    new[] { Title(chapters[i]) }, i == _chapter, () => { _chapter = k; Reopen(); }, _rows, _clear);
            }
            b.End();
            b.End();
            b.BeginPanel(TextW, -1f, TextAnchor.UpperLeft, 6f, 6f);
            _clear.Add(b.Last.image);
            b.BeginScroll(ScreenH - 30f);
            _clear.Add(b.Last.image);
            b.AddHeader(Title(chapters[_chapter]));
            b.AddSeparator();
            b.AddLabel(Body(chapters[_chapter]));
            b.End();
            b.End();
            b.End();
            b.End();
            b.End();
            var win = b.Show();
            Log.Guard("手册外观", () => PledgerSkin.Apply(win, screen, bezel, paper, _picked, _clear, _rows));
        }
    }
}
