using System;
using System.Collections.Generic;

namespace Decentralized.Domain
{
    /// <summary>一笔黑币报价：卖家手里的币、按现价的市值和他要的现金。Sting 是钓鱼执法（收下就记罪证，币不会到）。</summary>
    public sealed class HotOffer
    {
        public string Asset;
        public double Qty, Value;
        public int Cash;
        public bool Sting;
    }

    /// <summary>
    /// 收黑币：游戏里出了事，手里有来路不明的币的人上门，折价换现金。
    /// 帮派火并、镇压期间黑市的人出手 CRT；革命军袭击上层、下层动荡期间有人出手抢来的 PSC；黑市声望够了以后偶尔有人出手勒索得来的 Ponero。
    /// PSC、CRT 链上可追，收下的币按市值全额带污点；Ponero 不可追、不带污点，折扣也小，出口只有 Ponero 兑换处。
    /// </summary>
    public static class HotCoins
    {
        /// <summary>今天哪种黑币可能有人上门（按游戏当前的事件），没有返回 null。poneroOpen：Ponero 兑换处已开放。</summary>
        public static string Pick(ICollection<MarketEvent> active, bool poneroOpen, Rng rng)
        {
            var c = new List<string>();
            if (active.Contains(MarketEvent.GangWar) || active.Contains(MarketEvent.Crackdown)) c.Add(Assets.CRT);
            if (active.Contains(MarketEvent.RevRaidUpper) || active.Contains(MarketEvent.LowerUnrest)) c.Add(Assets.PSC);
            if (c.Count > 0 && rng.Chance(Tuning.HotEventChance)) return c[rng.Range(0, c.Count - 1)];
            if (poneroOpen && rng.Chance(Tuning.HotPoneroChance)) return Assets.Ponero;
            return null;
        }

        /// <summary>开价：市值 = 房租 × 区间，现金要价 = 市值 × 折扣（Ponero 折扣小）。crackdown：镇压期间钓鱼执法更多。</summary>
        public static HotOffer Make(string asset, double coinPrice, double rent, bool crackdown, Rng rng)
        {
            if (coinPrice <= 0 || rent <= 0) return null;
            double value = rent * rng.Range(Tuning.HotValueMinRents, Tuning.HotValueMaxRents);
            bool ponero = asset == Assets.Ponero;
            double cut = ponero ? rng.Range(Tuning.HotPoneroCutMin, Tuning.HotPoneroCutMax) : rng.Range(Tuning.HotCutMin, Tuning.HotCutMax);
            return new HotOffer
            {
                Asset = asset, Qty = value / coinPrice, Value = value, Cash = (int)Math.Round(value * cut),
                Sting = rng.Chance(Tuning.HotSting + (crackdown ? Tuning.HotStingCrackdown : 0)),
            };
        }

        /// <summary>收下：币进钱包，成本是付出的现金；PSC、CRT 按市值记污点，Ponero 不记。钓鱼执法不在这里处理。</summary>
        public static void Receive(Wallet w, HotOffer o)
        {
            w.Add(o.Asset, o.Qty);
            w.Basis += o.Cash;
            if (o.Asset != Assets.Ponero) w.Taint += o.Value;
        }
    }
}
