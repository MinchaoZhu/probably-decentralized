using System;

namespace Decentralized.Domain
{
    /// <summary>
    /// 顾客用币付款：成交的信用点在店里扣回，按当前币价换成币进热钱包。
    /// 收赃物的款项不再记销赃，改为币带污点：货值 × 赃物热度%。
    /// </summary>
    public static class CoinPayment
    {
        /// <summary>各派系用什么币付款（其余派系不用币）。</summary>
        public static string AssetFor(string faction)
        {
            switch (faction)
            {
                case "FACTION_UPPER_LEVEL": return Assets.CRC;
                case "FACTION_BLACK_MARKET": return Assets.CRT;
                case "FACTION_REVOLUTION": return Assets.CRA;
                case "FACTION_LOWER_LEVEL": return Assets.PSC;
                default: return null;
            }
        }

        /// <summary>一位顾客用币付款的概率。</summary>
        public static double Chance(string asset)
        {
            switch (asset)
            {
                case Assets.CRC: return Tuning.CoinPayCrc;
                case Assets.CRT: return Tuning.CoinPayCrt;
                case Assets.CRA: return Tuning.CoinPayCra;
                default: return Tuning.CoinPayPsc;
            }
        }

        /// <summary>收款：返回收到的币数。heatPct 是物品的赃物热度（0–100）：按货值 × 热度那部分的币是脏的；多付的溢价是顾客给的额外收入，干净。</summary>
        public static double Receive(Wallet w, string asset, double credits, double price, double heatPct)
        {
            if (credits <= 0 || price <= 0) return 0;
            double qty = credits * (1 + Tuning.CoinPayPremium) / price;   // 溢价只加在币数上，污点仍按货值算
            w.AddDirty(asset, qty, credits * Math.Max(0, Math.Min(100, heatPct)) / 100.0 / price);
            return qty;
        }
    }
}
