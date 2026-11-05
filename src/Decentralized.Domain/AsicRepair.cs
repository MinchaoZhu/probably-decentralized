using System;

namespace Decentralized.Domain
{
    /// <summary>
    /// RatDigg 矿机暴毙后的修理：往机器的修理槽里按顺序放电子元件，约占机价的 4%。每一步只收这一步需要的那种元件，攒够这一步再进下一步，
    /// 全部攒够就修好（健康扣 <see cref="Tuning.AsicRepairHealthCut"/> 点）。游戏里电子元件的价值是 33 / 350 / 2,500（普通 / 稀有 / 超科技）。
    /// </summary>
    public static class AsicRepair
    {
        public static (string Item, int Count)[] Steps(string model)
        {
            switch (model)
            {
                case "M1": return new[] { ("common_electronic", 4), ("rare_electronic", 1) };
                case "M2": return new[] { ("rare_electronic", 2) };
                case "M3": return new[] { ("hypertech_electronic", 1), ("rare_electronic", 1) };
                default: throw new ArgumentOutOfRangeException(nameof(model));
            }
        }

        /// <summary>当前这一步需要的元件；已修好（step 越界）返回 null。</summary>
        public static (string Item, int Count)? Need(string model, int step)
        {
            var steps = Steps(model);
            return step < steps.Length ? steps[step] : ((string, int)?)null;
        }

        /// <summary>
        /// 投入 count 件 item：只收当前这一步需要的那种，攒够就进下一步。返回实际收下的件数；step 达到总步数表示修好了。
        /// </summary>
        public static int Insert(string model, ref int step, ref int progress, string item, int count)
        {
            var need = Need(model, step);
            if (need == null || count <= 0 || item != need.Value.Item) return 0;
            int take = Math.Min(count, need.Value.Count - progress);
            progress += take;
            if (progress >= need.Value.Count) { step++; progress = 0; }
            return take;
        }

        public static bool Done(string model, int step) => step >= Steps(model).Length;
    }
}
