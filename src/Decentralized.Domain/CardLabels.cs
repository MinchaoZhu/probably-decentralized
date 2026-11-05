using System;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>卖家的说法：每个标签一项。型号是文字，其余取值与 <see cref="CardLook"/> 一致；「供电与风扇」由说法里的型号决定档次。</summary>
    public struct Claim
    {
        public string Model;
        public int Fan, Fingers, Bracket, Print, Sticker, Serial;
        /// <summary>风扇数、供电口：0 表示由型号的档次决定（说法里写的型号该有的），否则是写明的值（风扇数 1 到 3；供电口 1 到 3，对应 0 到 2 加一）。</summary>
        public int CountSet, PinSet;

        public int Tier => Hardware.Specs[Model].Tier;
        public int Count => CountSet != 0 ? CountSet : Hardware.FanCount[Tier];
        public int Pin => PinSet != 0 ? PinSet - 1 : Hardware.PinClass[Tier];

        public int Get(int row) => row == CardLabels.RowCount ? Count : row == CardLabels.RowPin ? Pin : row == CardLabels.RowFan ? Fan : row == CardLabels.RowFingers ? Fingers
            : row == CardLabels.RowBracket ? Bracket : row == CardLabels.RowPrint ? Print : row == CardLabels.RowSerial ? Serial : Sticker;

        public Claim With(int row, int value)
        {
            var c = this;
            if (row == CardLabels.RowFan) c.Fan = value; else if (row == CardLabels.RowFingers) c.Fingers = value;
            else if (row == CardLabels.RowBracket) c.Bracket = value; else if (row == CardLabels.RowPrint) c.Print = value;
            else if (row == CardLabels.RowSerial) c.Serial = value; else if (row == CardLabels.RowSeal) c.Sticker = value;
            else if (row == CardLabels.RowCount) c.CountSet = value; else if (row == CardLabels.RowPin) c.PinSet = value + 1;
            return c;
        }

        public string Encode() => $"{Model}:{Fan}{Fingers}{Bracket}{Print}{Sticker}{Serial}{CountSet}{PinSet}";

        public static Claim Decode(string s)
        {
            var p = (s ?? "").Split(':');
            if (p.Length != 2 || (p[1].Length != 6 && p[1].Length != 8) || !Hardware.Specs.ContainsKey(p[0])) return default;
            int D(int i) => Math.Max(0, Math.Min(2, p[1][i] - '0'));
            int E(int i) => p[1].Length > i ? Math.Max(0, Math.Min(3, p[1][i] - '0')) : 0;
            return new Claim { Model = p[0], Fan = D(0), Fingers = D(1), Bracket = D(2), Print = D(3), Sticker = D(4), Serial = D(5), CountSet = E(6), PinSet = E(7) };
        }

        /// <summary>按真实情况写出的说法（没有隐瞒）。</summary>
        public static Claim Truth(Unit u)
        {
            var l = u.Appearance;
            return new Claim { Model = u.FakeOf ?? u.Model, Fan = l.Fan, Fingers = l.Fingers, Bracket = l.Bracket, Print = l.Print, Sticker = l.Sticker, Serial = l.Serial, CountSet = u.ObservedCount, PinSet = u.ObservedPin + 1 };
        }
    }

    /// <summary>
    /// 标签栏的数据：五个标签（型号真伪、风扇叶片、金手指、挡板、封条），每个标签有卖家的说法和真实情况；
    /// 供电与风扇、背面丝印、序列号三项是型号真伪的依据，只存状态，不单独成行。
    /// 一个标签被指出（拆穿）之后，说法改回真实情况；价格按「已指出的标签取真实情况，其余取说法」算，
    /// 所以卖家隐瞒的问题要玩家一项项指出来才能把价格拉回真实价值。指出型号有问题，型号自动改回真实型号。
    /// </summary>
    public static class CardLabels
    {
        public const int RowSpec = 1, RowCount = 2, RowFan = 4, RowFingers = 8, RowBracket = 16, RowPrint = 32, RowSerial = 64, RowSeal = 128, RowPin = 256, AllRows = 511;
        /// <summary>标签栏的顺序：风扇数、供电口、背面丝印、序列号（真伪依据），型号，叶片、金手指、挡板、封条（成色）。</summary>
        public static readonly int[] Rows = { RowSpec, RowCount, RowPin, RowPrint, RowSerial, RowFan, RowFingers, RowBracket, RowSeal };
        /// <summary>第一行是型号（唯一的“指出型号”按钮），接着四行是真伪依据（风扇数、供电口、背面丝印、序列号，只读，四项全部符合标称型号才是正品），之后四行是成色。</summary>
        public const int AuthRows = 5;
        public static bool IsAuth(int row) => row == RowCount || row == RowPin || row == RowPrint || row == RowSerial;

        /// <summary>各标签各取值对价格的影响（百分比，相加）：风扇叶片积灰 −4%、缺角 −12%；金手指发暗 −4%、氧化 −12%；挡板划痕 −2%；封条缺失 −6%。
        /// 型号、供电与风扇、丝印、序列号本身不扣价，它们决定的是这块卡是什么芯片。</summary>
        public static double Pct(int row, int value)
        {
            switch (row)
            {
                case RowFan: return value == 1 ? -Tuning.CardFanDust * 100 : value == 2 ? -Tuning.CardFanChipped * 100 : 0;
                case RowFingers: return value == 1 ? -Tuning.CardFingersDark * 100 : value == 2 ? -Tuning.CardFingersOxidized * 100 : 0;
                case RowBracket: return value == 1 ? -Tuning.CardBracketScratch * 100 : 0;
                case RowSeal: return value == 2 ? -Tuning.CardSealMissing * 100 : 0;
            }
            return 0;
        }

        /// <summary>这一行是不是假货的证据：说法与真实情况不同，并且真实情况本身不该出现在真卡上。</summary>
        public static bool IsEvidence(int row, Claim claim, Claim real)
        {
            switch (row)
            {
                case RowCount: return claim.Count != real.Count;
                case RowPin: return claim.Pin != real.Pin;
                case RowPrint: return real.Print != 0 && claim.Print != real.Print;
                case RowSerial: return real.Serial != 0 && claim.Serial != real.Serial;
            }
            return false;
        }

        /// <summary>
        /// 价值：claim 为空表示没有人说过什么，一律按真实情况；exposed 里的位表示已指出的标签（取真实情况）。
        /// 型号取「说法里的型号」，型号标签被拆穿之后取真实芯片。价值 = 型号定价 ×（0.25 + 0.75 × 健康度）×（1 + 各标签折扣之和）。
        /// </summary>
        public static long Value(Unit u, Claim? claim, int exposed)
        {
            var real = Claim.Truth(u);
            Claim believed = claim == null ? real : claim.Value;
            double sum = 0;
            foreach (int r in new[] { RowFan, RowFingers, RowBracket, RowSeal })
            {
                int v = claim == null || (exposed & r) != 0 ? real.Get(r) : believed.Get(r);
                sum += Pct(r, v);
            }
            string model = claim == null || (exposed & RowSpec) != 0 ? real.Model : believed.Model;
            double value = CardValue.Of(model, u.Health) * Math.Max(0, 1 + sum / 100.0);
            return Math.Max(1, (long)Math.Round(value));
        }

        /// <summary>指出一个标签：返回 0 没问题（说法属实）、1 隐瞒了对价格不利的情况、2 拆穿了假货（型号一并改回真实型号）。</summary>
        public static int Point(Unit u, Claim claim, int row, ref int exposed)
        {
            var real = Claim.Truth(u);
            bool wasExposed = (exposed & row) != 0;
            exposed |= row;
            if (row == RowSpec) return claim.Model != real.Model ? 2 : 0;
            if (IsEvidence(row, claim, real))
            {
                exposed |= RowSpec | RowCount | RowPin | RowPrint | RowSerial;
                return 2;
            }
            if (wasExposed) return 0;
            return claim.Get(row) != real.Get(row) ? 1 : 0;
        }

        /// <summary>说法里隐瞒了什么：说法比真实情况价值高的标签，型号不符，以及假货的证据。</summary>
        public static int Lies(Unit u, Claim claim, int exposed)
        {
            var real = Claim.Truth(u);
            int lies = 0;
            foreach (int r in Rows)
            {
                if ((exposed & r) != 0) continue;
                if (r == RowSpec) { if (claim.Model != real.Model) lies |= r; }
                else if (IsAuth(r)) { if (claim.Get(r) != real.Get(r)) lies |= r; }
                else if (claim.Get(r) != real.Get(r) && Pct(r, claim.Get(r)) > Pct(r, real.Get(r))) lies |= r;
            }
            return lies;
        }

        /// <summary>卖家怎么说：老实人如实说；中间商每项有七成概率说成没问题；骗子全部说成没问题。假卡的型号一律说成标称型号。</summary>
        public static Claim DealerClaim(Unit u, DealerKind kind, Rng rng)
        {
            var c = Claim.Truth(u);
            if (kind == DealerKind.Honest) return c;
            c.Model = u.Model;
            double hide = kind == DealerKind.Scammer ? 1.0 : Tuning.UsedMiddlemanHide;
            c.CountSet = 0; c.PinSet = 0;
            foreach (int r in new[] { RowFan, RowFingers, RowBracket, RowPrint, RowSerial, RowSeal })
                if (rng.Chance(hide)) c = c.With(r, 0);
            return c;
        }

        // ---------- 序列号 ----------

        /// <summary>各牌子的序列号前缀与合法工厂码。序列号格式：前缀 + 档次码（1 到 4）+ "-" + 工厂码（两位）+ "-" + 四位流水。</summary>
        public static string BrandCode(string brand) => brand == Hardware.Probidia ? "PB" : "MD";
        public static readonly int[] ProbidiaFactories = { 11, 12, 21, 34, 47 }, PmdFactories = { 13, 22, 35, 41, 58 };
        public static int[] Factories(string brand) => brand == Hardware.Probidia ? ProbidiaFactories : PmdFactories;

        private static uint Mix(uint seed, int salt)
        {
            uint x = seed * 2654435761u + (uint)salt * 40503u + 777u;
            x ^= x >> 15; x *= 2246822519u; x ^= x >> 13; x *= 3266489917u; x ^= x >> 16;
            return x;
        }

        /// <summary>
        /// 卡上贴纸的序列号。合规：前缀（牌子）+ 标称型号的档次码 + 合法工厂码 + 四位流水。
        /// 不合规（贴纸是后补的）：工厂码不在合法范围，或流水只有三位。档次码不符：贴的是原芯片的序列号，档次码是真实芯片的档次。
        /// </summary>
        public static string SerialText(Unit u)
        {
            var label = Hardware.Specs[u.Model];
            var chip = Hardware.Specs[u.FakeOf ?? u.Model];
            uint seed = u.Look.Seed == 0 ? 12345u : u.Look.Seed;
            int kind = u.Appearance.Serial;
            int[] ok = Factories(label.Brand);
            int tier = (kind == 2 ? chip.Tier : label.Tier) + 1;
            int factory = ok[(int)(Mix(seed, 1) % (uint)ok.Length)];
            int number = 1000 + (int)(Mix(seed, 2) % 9000);
            if (kind == 1)
            {
                if (Mix(seed, 3) % 2 == 0) { int[] bad = { 0, 9, 66, 77, 88, 99 }; factory = bad[(int)(Mix(seed, 4) % (uint)bad.Length)]; }
                else number = 100 + (int)(Mix(seed, 5) % 900);
            }
            return $"{BrandCode(label.Brand)}{tier}-{factory:00}-{number}";
        }
    }
}
