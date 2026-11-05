using System;

namespace Decentralized.Domain
{
    /// <summary>
    /// 显卡的外观：5 个维度，每个 3 种取值，共 243 种组合。前三个画在正面，后两个画在背面（右键"切换"翻面）。
    /// 老化类的维度（风扇、金手指、挡板划痕）随健康度出现，真卡也会有；破绽类的取值只会出现在假卡上，
    /// 玩家要结合名字（标称型号）与背面的丝印、序列号贴纸判断。
    /// </summary>
    public struct CardLook
    {
        /// <summary>风扇：0 干净，1 积灰，2 叶片缺角（老化）。</summary>
        public int Fan;
        /// <summary>金手指：0 光亮，1 发暗，2 氧化发绿（老化）。</summary>
        public int Fingers;
        /// <summary>挡板：0 完好，1 划痕（老化）。</summary>
        public int Bracket;
        /// <summary>背面丝印：0 与标称型号一致，1 印的是真实芯片的型号（假卡），2 重新印过、字歪（假卡）。</summary>
        public int Print;
        /// <summary>封条：0 完好，2 缺失（开过封的旧卡或翻新卡）。</summary>
        public int Sticker;
        /// <summary>序列号：0 合规（格式对、工厂码在合法范围、档次码与型号一致），1 不合规（工厂码不在合法范围或位数不对，贴纸是后补的），
        /// 2 档次码与标称型号不符（贴的是原芯片的序列号）。后两种只出现在假卡上。</summary>
        public int Serial;

        /// <summary>风扇数对不上标称型号（实物是真实芯片的风扇数）：0 对得上，1 对不上。只出现在假卡上。</summary>
        public int CountBad;
        /// <summary>供电口对不上标称型号（实物是真实芯片的供电口）：0 对得上，1 对不上。只出现在假卡上。</summary>
        public int PinBad;

        public const int Variants = 3;

        /// <summary>每张卡自己的随机种子：老化类维度（风扇、金手指、挡板划痕）由它和当前健康度决定，每晚损耗后重新算，
        /// 所以越旧的卡看着越旧。为 0 时表示外观是写死的（测试用）。</summary>
        public uint Seed;

        private static double U(uint seed, int salt)
        {
            uint x = seed * 2654435761u + (uint)salt * 40503u + 12345u;
            x ^= x >> 15; x *= 2246822519u; x ^= x >> 13; x *= 3266489917u; x ^= x >> 16;
            return (x >> 8) * (1.0 / (1 << 24));
        }

        /// <summary>按当前健康度重新算出的外观：磨损度 = 1 − 健康度 / 100，再加上这张卡自己的偏差（±0.15）。
        /// 风扇磨损 ≥ 0.25 积灰、≥ 0.55 缺角；金手指 ≥ 0.30 发暗、≥ 0.60 氧化；挡板磨损 ≥ 0.45 出划痕（接口数不对是假卡的固定特征，不变）。</summary>
        public CardLook Appearance(double health)
        {
            if (Seed == 0) return this;
            double worn = 1 - Math.Max(0, Math.Min(100, health)) / 100.0;
            var l = this;
            double fan = worn + (U(Seed, 1) - 0.5) * 0.3, fin = worn + (U(Seed, 2) - 0.5) * 0.3, brk = worn + (U(Seed, 3) - 0.5) * 0.3;
            l.Fan = fan >= 0.55 ? 2 : fan >= 0.25 ? 1 : 0;
            l.Fingers = fin >= 0.60 ? 2 : fin >= 0.30 ? 1 : 0;
            l.Bracket = brk >= 0.45 ? 1 : 0;
            return l;
        }

        private static readonly double[] FanHash = { 1.00, 0.97, 0.92 }, FingerHash = { 1.00, 0.98, 0.94 };

        /// <summary>算力倍数：风扇积灰 ×0.97、缺角 ×0.92（降频）；金手指发暗 ×0.98、氧化 ×0.94（接触不良掉速）。</summary>
        public double HashFactor => FanHash[Fan] * FingerHash[Fingers];

        /// <summary>六位数字，存在物品标签上：风扇、金手指、挡板、丝印、封条、序列号。</summary>
        public string Encode() => $"{Fan}{Fingers}{Bracket}{Print}{Sticker}{Serial}{CountBad}{PinBad}";

        /// <summary>
        /// 旧版存档的外观只有 5 或 6 位（没有风扇数、供电口两位）：旧规则里假卡的风扇数和供电口只要档次不同就一定对不上。
        /// 这里按旧规则补上，旧存档里的假卡仍然露出同样的破绽。新格式（8 位）不动。
        /// </summary>
        public static CardLook Legacy(string encoded, CardLook look, string label, string chip)
        {
            if (encoded == null || encoded.Length >= 8 || string.IsNullOrEmpty(chip) || chip == label) return look;
            var l = Hardware.Specs[label]; var c = Hardware.Specs[chip];
            look.CountBad = Hardware.FanCount[l.Tier] != Hardware.FanCount[c.Tier] ? 1 : 0;
            look.PinBad = Hardware.PinClass[l.Tier] != Hardware.PinClass[c.Tier] ? 1 : 0;
            return look;
        }

        public static CardLook Decode(string s)
        {
            if (s == null || s.Length < 5 || s.Length > 8) return default;
            int D(int i) => Math.Max(0, Math.Min(Variants - 1, s[i] - '0'));
            int B(int i) => s.Length > i && s[i] == '1' ? 1 : 0;
            return new CardLook { Fan = D(0), Fingers = D(1), Bracket = D(2), Print = D(3), Sticker = D(4), Serial = s.Length > 5 ? D(5) : 0, CountBad = B(6), PinBad = B(7) };
        }

        // 外观维度对应卡的真实状态：损耗、暴毙概率随之变化，价值走标签（见 CardLabels）
        private static readonly double[] FanWear = { 1.00, 1.10, 1.30 }, FanDeath = { 1.00, 1.00, 1.20 };
        private static readonly double[] FingerDeath = { 1.00, 1.10, 1.30 };

        /// <summary>每晚损耗倍数：风扇积灰散热差 ×1.1，缺角 ×1.3。</summary>
        public double WearFactor => FanWear[Fan];

        /// <summary>暴毙概率倍数：风扇缺角 ×1.2；金手指发暗 ×1.1、氧化 ×1.3（接触不良）。</summary>
        public double DeathFactor => FanDeath[Fan] * FingerDeath[Fingers];

        /// <summary>只在假卡上出现的取值：玩家一旦看到，就能肯定是假的。</summary>
        public bool HasFakeSign => Print != 0 || Serial != 0 || CountBad != 0 || PinBad != 0;

        /// <summary>
        /// 生成外观。真卡：老化维度按健康度出现，封条缺失在二手卡上很常见，不会出现破绽类取值。
        /// 假卡：在三类破绽（接口数、丝印、手写序列号）里随机挑 1–3 处，再叠加老化与封条缺失。
        /// </summary>
        public static CardLook Generate(Rng rng, double health, bool fake, int labelTier = 3, int chipTier = 0)
        {
            var look = new CardLook { Seed = rng.NextUInt() | 1, Sticker = rng.Chance(fake ? 0.35 : 0.15 + 0.30 * (1 - Math.Max(0, Math.Min(100, health)) / 100.0)) ? 2 : 0 };
            if (fake)
            {
                // 四项真伪依据（风扇数、供电口、背面丝印、序列号）各自独立地露出破绽，至少一项；平均每张假卡约 1.5 项
                bool countCan = Hardware.FanCount[chipTier] != Hardware.FanCount[labelTier], pinCan = Hardware.PinClass[chipTier] != Hardware.PinClass[labelTier];
                double p = Tuning.FakeSignChance;
                for (int tries = 0; tries < 30 && !look.HasFakeSign; tries++)
                {
                    look.CountBad = countCan && rng.Chance(p) ? 1 : 0;
                    look.PinBad = pinCan && rng.Chance(p) ? 1 : 0;
                    look.Print = rng.Chance(p) ? (rng.Chance(0.6) ? 1 : 2) : 0;
                    look.Serial = rng.Chance(p) ? (rng.Chance(0.55) ? 2 : 1) : 0;
                }
                if (!look.HasFakeSign) look.Serial = 1;
            }
            return look.Appearance(health);
        }
    }

    /// <summary>
    /// 显卡的统一价值函数：实际售价和鉴定公会的"正确价值"是同一个数。按真实芯片和健康度算；
    /// 假卡的标称价（贩子报的价）用标称型号算，二者之差就是被坑的钱。
    /// </summary>
    public static class CardValue
    {
        /// <summary>价值 = 型号定价 × 健康度系数。健康 100 时满价，健康 0 时只剩 25%。</summary>
        public static long Of(string model, double health)
        {
            var spec = Hardware.Specs[model];
            double f = Tuning.CardValueFloor + (1 - Tuning.CardValueFloor) * Math.Max(0, Math.Min(100, health)) / 100.0;
            return Math.Max(1, (long)Math.Round(spec.Price * f));
        }

        /// <summary>真实价值：全部标签都按真实情况算。</summary>
        public static long True(Unit u) => CardLabels.Value(u, null, 0);
    }
}
