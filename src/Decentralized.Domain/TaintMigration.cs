using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>
    /// 存档迁移（数据版本 5）：旧存档的污点是一笔信用点（整个钱包、托管里各一笔），换成按币种记的脏枚数。
    /// 钱包：按当前价格摊到各种可追踪的币上（<see cref="Wallet.MigrateTaint"/>）；Poinbase 账户和永续仓位的污点先挪到 1 号钱包一起摊（超出的作废）。
    /// 托管（混币器在途、黑市订单、质押、空投池、预测持仓、自营混币器本金）：按当前价格换成那种币的脏枚数，不超过数量。旧字段清零。
    /// </summary>
    public static class TaintMigration
    {
        private static double Qty(Func<string, double> price, string asset, double amount, double legacy)
        {
            double p = price(asset);
            return legacy > 0 && p > 0 ? Math.Min(Math.Max(0, amount), legacy / p) : 0;
        }

        /// <summary>迁移全部，返回旧存档里的污点总额（信用点，日志用）。</summary>
        public static double Run(IList<Wallet> wallets, Wallet poinbase, IEnumerable<PerpPosition> perps, Mixer mixer, BlackMarket black,
            Staking staking, Scams scams, PredictBook predict, OwnMixer own, Func<string, double> price)
        {
            double total = wallets.Sum(w => w.LegacyTaint) + poinbase.LegacyTaint + perps.Sum(p => p.LegacyTaint);
            double extra = Exchange.TakeLegacyTaint(poinbase, perps);
            for (int i = 0; i < wallets.Count; i++) wallets[i].MigrateTaint(price, i == 0 ? extra : 0);
            foreach (var d in mixer.Pending) { total += d.LegacyTaint; d.Dirty += Qty(price, d.Asset, d.Amount, d.LegacyTaint); d.LegacyTaint = 0; }
            foreach (var o in black.Orders) { total += o.LegacyTaint; o.Dirty += Qty(price, o.Asset, o.Qty, o.LegacyTaint); o.LegacyTaint = 0; }
            foreach (var l in staking.Lots) { total += l.LegacyTaint; l.Dirty += Qty(price, Assets.CRA, l.Total, l.LegacyTaint); l.LegacyTaint = 0; }
            foreach (var p in scams.Pools)
            {
                foreach (var kv in p.LegacyTaint)
                {
                    total += kv.Value;
                    p.Dirty[kv.Key] = (p.Dirty.TryGetValue(kv.Key, out var d) ? d : 0) + Qty(price, Assets.CRC, p.Of(kv.Key), kv.Value);
                }
                p.LegacyTaint.Clear();
                foreach (var q in p.Pending) { total += q.LegacyTaint; q.Dirty += Qty(price, Assets.CRC, q.Amount, q.LegacyTaint); q.LegacyTaint = 0; }
            }
            foreach (var p in predict.Positions) { total += p.LegacyTaint; p.Dirty += Qty(price, Assets.CRC, p.Cost * (1 + Tuning.PredictFee), p.LegacyTaint); p.LegacyTaint = 0; }
            foreach (var kv in own.Pools) { total += kv.Value.LegacyCapTaint; kv.Value.CapDirty += Qty(price, kv.Key, kv.Value.Capital, kv.Value.LegacyCapTaint); kv.Value.LegacyCapTaint = 0; }
            return total;
        }
    }
}
