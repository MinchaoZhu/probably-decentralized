using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>二手显卡贩子的类型：老实人按真实价卖；中间商加价；骗子夹带假卡，按标称型号的价卖。</summary>
    public enum DealerKind { Honest, Middleman, Scammer }

    /// <summary>贩子带来的一张卡：型号、健康度、是不是假卡。怎么说由 <see cref="CardLabels.DealerClaim"/> 决定，中间商另加 <see cref="MarkupPct"/>% 的价。</summary>
    public sealed class UsedCard
    {
        public string Model;
        public string FakeOf;
        public double Health;
        public int MarkupPct;
    }

    public static class UsedDealers
    {
        public const double MiddlemanMarkup = Tuning.UsedMiddlemanMarkup;
        public const double FakeShareMiddleman = Tuning.UsedFakeMiddleman, FakeShareScammer = Tuning.UsedFakeScammer;

        /// <summary>今天来的贩子的类型：骗子越往后越多。</summary>
        public static DealerKind Kind(int day, Rng rng)
        {
            double scam = Scales.R(Math.Min(Tuning.UsedScamMax, Tuning.UsedScamBase + day * Tuning.UsedScamPerDay));
            double r = rng.NextDouble();
            return r < scam ? DealerKind.Scammer : r < scam + Tuning.UsedMiddlemanShare ? DealerKind.Middleman : DealerKind.Honest;
        }

        /// <summary>能出现在二手市场上的型号：按天数放开，旗舰第 30 天起。</summary>
        public static string[] Pool(int day)
        {
            var list = new List<string>(Dealer.Basic);
            if (day >= Dealer.AdvancedFromDay) list.AddRange(Dealer.Advanced);
            if (day >= Dealer.FlagshipFromDay) list.AddRange(Dealer.Flagship);
            return list.ToArray();
        }

        /// <summary>从池里抽一张：随机型号与成色；按 fakeShare 的概率是假卡（背后是便宜得多的真实芯片，废片只给低档标称）。</summary>
        public static UsedCard Card(string[] pool, double fakeShare, int markupPct, Rng rng, double minHealth = Tuning.CardHealthMin, double maxHealth = Tuning.CardHealthMax)
        {
            var label = pool[rng.Range(0, pool.Length - 1)];
            var card = new UsedCard { Model = label, Health = minHealth + rng.NextDouble() * (maxHealth - minHealth), MarkupPct = markupPct };
            var cheaper = Hardware.Specs.Values.Where(s => !s.Asic && s.Price * 2 <= Hardware.Specs[label].Price && (!s.Scrap || Hardware.Specs[label].Tier == 0)).Select(s => s.Model).ToArray();
            if (cheaper.Length > 0 && rng.Chance(fakeShare)) card.FakeOf = cheaper[rng.Range(0, cheaper.Length - 1)];
            return card;
        }

        public static List<UsedCard> Cards(DealerKind kind, int day, Rng rng)
        {
            var pool = Pool(day);
            int count = rng.Range(3, 5);
            var cards = new List<UsedCard>();
            for (int i = 0; i < count; i++)
            {
                double share = kind == DealerKind.Scammer ? FakeShareScammer : kind == DealerKind.Middleman ? FakeShareMiddleman : 0;
                cards.Add(Card(pool, share, kind == DealerKind.Middleman ? (int)Math.Round(MiddlemanMarkup * 100) : 0, rng, Tuning.UsedHealthMin[(int)kind], Tuning.UsedHealthMax[(int)kind]));
            }
            return cards;
        }
    }
}
