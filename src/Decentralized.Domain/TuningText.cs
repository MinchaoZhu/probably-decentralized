using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Decentralized.Domain
{
    /// <summary>
    /// 文本里的数值占位符：<c>{Name}</c>、<c>{Name:%}</c>、<c>{Name[2]:%}</c>，也可以走嵌套类 <c>{Section.Name}</c>。
    /// 手册等说明文字不直接写数字，渲染时从 <see cref="Tuning"/> 取值，数值一改文字跟着变。
    /// <c>%</c> 把小数按百分比显示（0.03 → 3%），<c>+%</c> 把倍数显示成涨幅（1.2 → 20%）；不带格式时去掉多余的零。
    /// </summary>
    public static class TuningText
    {
        private static readonly Regex Placeholder = new Regex(@"\{([A-Za-z0-9_.]+)(?:\[(\d+)\])?(?::(\+?%))?\}", RegexOptions.Compiled);

        public static string Render(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Placeholder.Replace(text, m => TryValue(m.Groups[1].Value, m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : -1, out var v)
                ? Format(v, m.Groups[3].Value)
                : m.Value);
        }

        /// <summary>文本里取不到值的占位符（测试用）。</summary>
        public static System.Collections.Generic.List<string> Unresolved(string text)
        {
            var bad = new System.Collections.Generic.List<string>();
            foreach (Match m in Placeholder.Matches(text ?? ""))
                if (!TryValue(m.Groups[1].Value, m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : -1, out _)) bad.Add(m.Value);
            return bad;
        }

        private static string Format(double v, string format)
        {
            bool percent = format == "%" || format == "+%";
            if (format == "+%") v -= 1; // 倍数 1.2 → 20%
            if (percent) v *= 100;
            return Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture) + (percent ? "%" : "");
        }

        private static bool TryValue(string path, int index, out double value)
        {
            value = 0;
            if (TuningTable.Arrays.TryGetValue(path, out var arr))
            {
                if (index < 0 || index >= arr.Length) return false;
                value = arr[index];
                return true;
            }
            return index < 0 && TuningTable.Scalars.TryGetValue(path, out value);
        }
    }
}
