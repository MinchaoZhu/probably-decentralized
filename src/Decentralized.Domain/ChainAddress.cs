namespace Decentralized.Domain
{
    /// <summary>
    /// 链上地址："0x" + 6 位随机十六进制 + 2 位校验位。手输地址时输错一位，校验位几乎总能发现（约 1/256 恰好也合法，币就转进了没人用的地址）。
    /// </summary>
    public static class ChainAddress
    {
        public const int BodyLength = 6, CheckLength = 2, Length = 2 + BodyLength + CheckLength;

        public static string Make(uint r)
        {
            string body = (r & 0xFFFFFF).ToString("x6");
            return "0x" + body + Check(body);
        }

        /// <summary>校验位：地址主体的 FNV-1a 散列取高 8 位（低位对单字符改动是一一对应的，永远不会碰巧合法）。</summary>
        public static string Check(string body)
        {
            uint h = 2166136261;
            foreach (char c in body) { h ^= c; h *= 16777619; }
            return (h >> 24).ToString("x2");
        }

        public static string Normalize(string s) => (s ?? "").Trim().ToLowerInvariant();

        /// <summary>格式对（0x 开头、十六进制、长度对）。</summary>
        public static bool WellFormed(string s)
        {
            s = Normalize(s);
            if (s.Length != Length || !s.StartsWith("0x")) return false;
            for (int i = 2; i < s.Length; i++)
                if (!(s[i] >= '0' && s[i] <= '9' || s[i] >= 'a' && s[i] <= 'f')) return false;
            return true;
        }

        /// <summary>格式对且校验位对得上。</summary>
        public static bool Valid(string s)
        {
            s = Normalize(s);
            return WellFormed(s) && Check(s.Substring(2, BodyLength)) == s.Substring(2 + BodyLength);
        }

        public static bool Same(string a, string b) => Normalize(a) == Normalize(b);

        /// <summary>缩写（钱包列表里的样子）：0x + 主体前 3 位 + … + 校验位，例如 0x4b9…17。</summary>
        public static string Short(string a)
        {
            a = Normalize(a);
            return WellFormed(a) ? a.Substring(0, 5) + "…" + a.Substring(2 + BodyLength) : a;
        }

        /// <summary>
        /// 相似地址（地址投毒、剪贴板木马用）：主体前 3 位和校验位都和原地址一样，缩写看起来完全相同，中间几位不同；本身是合法地址，
        /// 币转过去就进了骗子手里。找不到校验位相同的就只保留前 3 位。
        /// </summary>
        public static string Lookalike(string a, Rng rng)
        {
            a = Normalize(a);
            if (!Valid(a)) return Make(rng.NextUInt());
            string prefix = a.Substring(2, 3), check = a.Substring(2 + BodyLength), body0 = a.Substring(2, BodyLength);
            for (int i = 0; i < 20000; i++)
            {
                string body = prefix + (rng.NextUInt() & 0xFFF).ToString("x3");
                if (body != body0 && Check(body) == check) return "0x" + body + check;
            }
            string fallback;
            do fallback = prefix + (rng.NextUInt() & 0xFFF).ToString("x3"); while (fallback == body0);
            return "0x" + fallback + Check(fallback);
        }
    }
}
