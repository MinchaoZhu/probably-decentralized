using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>一天一根蜡烛：开盘接前一天的收盘价、包括夜间变动和白天真实发生过的最高 / 最低、夜间结算前的收盘价；成交额分玩家和 NPC 大户（信用点）。</summary>
    public sealed class Candle
    {
        public int Day;
        public double O, H, L, C, VolPlayer, VolNpc;
        public bool Closed;
    }

    /// <summary>
    /// 日 K 历史（按币种）。开门时开一根，白天的价格变动（开门波动、插针、大户、玩家大单）更新高低和现价，夜间结算时收盘。
    /// 没有发生过的日内走势不编造：没有事件的日子高低就等于开收。
    /// </summary>
    public sealed class CandleBook
    {
        public const int Keep = 60;
        public Dictionary<string, List<Candle>> Series = new Dictionary<string, List<Candle>>();

        public List<Candle> Of(string asset) => Series.TryGetValue(asset, out var s) ? s : new List<Candle>();

        /// <summary>今天的蜡烛：没有就按现价开一根（读档时补上，开盘价就是读档时的价格）。</summary>
        public Candle Today(string asset, int day, double price)
        {
            if (!Series.TryGetValue(asset, out var s)) Series[asset] = s = new List<Candle>();
            var c = s.Count > 0 ? s[s.Count - 1] : null;
            if (c != null && c.Day == day) return c;
            c = new Candle { Day = day, O = price, H = price, L = price, C = price };
            s.Add(c);
            while (s.Count > Keep) s.RemoveAt(0);
            return c;
        }

        /// <summary>
        /// 开门：新开一根。币是全天候交易的，所以开盘价接昨天的收盘价，夜里的回归和开门波动都算进这根蜡烛（没有跳空）；
        /// 没有昨天的（刚开始记录）就从现价开。
        /// </summary>
        public void Open(string asset, int day, double price)
        {
            var s = Of(asset);
            var prev = s.Count > 0 && s[s.Count - 1].Day < day ? s[s.Count - 1] : null;
            var c = Today(asset, day, price);
            if (c.Closed) return;
            double o = prev != null ? prev.C : price;
            c.O = o; c.H = Math.Max(o, price); c.L = Math.Min(o, price); c.C = price;
        }

        /// <summary>白天的价格变动。</summary>
        public void Tick(string asset, int day, double price, double volume = 0, bool player = false)
        {
            var c = Today(asset, day, price);
            if (c.Closed) return;
            c.H = Math.Max(c.H, price); c.L = Math.Min(c.L, price); c.C = price;
            if (player) c.VolPlayer += Math.Abs(volume); else c.VolNpc += Math.Abs(volume);
        }

        /// <summary>夜间结算：收盘。</summary>
        public void Close(string asset, int day, double price)
        {
            Tick(asset, day, price);
            Today(asset, day, price).Closed = true;
        }

        /// <summary>NPC 当天的成交额（信用点）：基础量随池深，波动越大成交越多。</summary>
        public static double NpcVolume(double depth, double share, double absMove, Rng rng) =>
            depth * share * (1 + Tuning.ChartVolMove * absMove) * rng.Range(0.6, 1.4);

        /// <summary>收盘价的 n 日均线（不足 n 根时用已有的）；每根蜡烛一个值。</summary>
        public static List<double> Ma(IList<Candle> s, int n)
        {
            var r = new List<double>();
            for (int i = 0; i < s.Count; i++)
            {
                int a = Math.Max(0, i - n + 1);
                double sum = 0;
                for (int j = a; j <= i; j++) sum += s[j].C;
                r.Add(sum / (i - a + 1));
            }
            return r;
        }
    }
}
