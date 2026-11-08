using System.Collections.Generic;
using System.Linq;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// 涡轮增压器作用在矿架、矿机上：和原版机器一样立刻多跑一轮，这一轮就是立刻挖一晚。
    /// 原版增压器的效果是调用机器的夜间周期回调（矿架、矿机的回调是"扣电并登记今晚挖矿"），所以在增压器外包一层，
    /// 作用于矿机时打上 <see cref="Boosting"/>，回调看到它就改为立刻结算一轮：扣一晚的电，按这台机器的算力出块并发到它的收款地址，算一次损耗和暴毙。
    /// 增压器本身的消耗、充电、可用次数都照原版。
    /// </summary>
    public static class TurboMine
    {
        /// <summary>原版增压器正在作用于矿机（只在增压器的作用回调里为真）。</summary>
        public static bool Boosting;

        /// <summary>
        /// 立刻挖一轮（电已经扣过）。出块按全网份额算：玩家这边的算力取昨晚全部机器的算力（至少是这台），这台机器分其中自己的那份，
        /// 和夜里一起挖时分到的差不多。
        /// </summary>
        public static void Round(GameItem machine, List<KeyValuePair<GameItem, ModuleBonus>> cards)
        {
            var d = Session.Data;
            if (d == null || cards.Count == 0) return;
            var units = cards.Select(c => (item: c.Key, unit: Items.ToUnit(c.Key), bonus: c.Value)).ToList();
            double hash = units.Sum(x => x.unit.Hash(x.bonus));
            if (hash <= 0) return;
            double player = System.Math.Max(hash, d.LastNight?.Hash ?? 0);
            double mined = d.Network.Mine(player) * hash / player;
            bool paid = Night.PayMachine(d, machine, mined);
            var ps = PlayerStore.Instance;
            ps.AddNightLog(paid ? L.T("nightlog.turbo", machine.name, Fmt.Num(mined)) : L.T("nightlog.mined.lost", Fmt.Num(mined)));
            foreach (var (item, unit, bonus) in units)
            {
                bool died = unit.Night(bonus, d.Rng);
                Items.Store(item, unit);
                if (died) ps.AddNightLog(L.T("nightlog.burnt", item.name), "cc7755");
            }
            Log.Info($"涡轮增压：{machine.name} 立刻挖一轮，算力 {hash:0.##}，产出 {mined:0.####} PSC{(paid ? "" : "（没设置收款地址，丢失）")}");
            Session.Save();
        }
    }
}
