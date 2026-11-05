using System;
using System.Collections.Generic;

namespace Decentralized.Domain
{
    /// <summary>
    /// 游戏剧情选择对本 mod 世界的影响：一组倍率和期限，随存档保存。各处的概率读 <see cref="Current"/>（接入层读档时指向存档里的这一份）。
    /// 每个选择只生效一次（Applied 记下键）。
    /// </summary>
    public sealed class StoryMods
    {
        /// <summary>当前生效的那一份。接入层读档时设成存档里的；测试里可以换。</summary>
        public static StoryMods Current = new StoryMods();

        public List<string> Applied = new List<string>();
        /// <summary>CRC 脱锚、CRC 托管银行减记、CRT 脱锚、镇压时 CRT 被出卖的概率倍率（永久）。</summary>
        public double CrcDepeg = 1, CrcHaircut = 1, CrtDepeg = 1, CrtFreeze = 1;
        /// <summary>到这一天之前 CRA 螺旋概率减半。</summary>
        public int CraSafeUntil = -1;
        /// <summary>治安部整顿到这一天：出金审查命中率、混币器制裁概率、操纵与内幕查处概率上调。</summary>
        public int SecUntil = -1;
        /// <summary>伦警官被捕后的立威期到这一天：出金审查、操纵、内幕查处概率上调。</summary>
        public int LunUntil = -1;
        /// <summary>SPARK 每晚额外乘的倍数，还剩几晚。</summary>
        public double SparkPush = 1;
        public int SparkPushNights;
        /// <summary>混币器：手续费、匿名集（NPC 混币量）、跑路概率的倍率；被举报后永久关闭（在途的照常提回，不能再存）。</summary>
        public double MixerFee = 1, MixerVolume = 1, MixerRug = 1;
        public bool MixerClosed;
        /// <summary>本 mod 自己的剧情线：混币器运营者 Tumbler。</summary>
        public Tumbler Tumbler = new Tumbler();

        public bool Has(string key) => Applied.Contains(key);

        public double CraSpiral(int day) => day < CraSafeUntil ? Tuning.StoryCraSpiralMult : 1;
        public double Aml(int day) => (day < SecUntil ? Tuning.StorySecAmlMult : 1) * (day < LunUntil ? Tuning.StoryLunMult : 1);
        public double Sanction(int day) => day < SecUntil ? Tuning.StorySecSanctionMult : 1;
        /// <summary>操纵检出、内幕查处的概率倍率。</summary>
        public double Enforce(int day) => (day < SecUntil ? Tuning.StorySecEnforceMult : 1) * (day < LunUntil ? Tuning.StoryLunMult : 1);

        /// <summary>每晚取 SPARK 的额外倍数（推完就是 1）。</summary>
        public double TakeSparkPush()
        {
            if (SparkPushNights <= 0) return 1;
            SparkPushNights--;
            return SparkPush;
        }

        /// <summary>
        /// 记下一个剧情选择并设置倍率。返回 false 表示已经生效过。CRA 补贴池的注资由接入层做（这里只管倍率）。
        /// key：serpico_rev / serpico_bm / serpico_sec / eleanor_leak / eleanor_return / lun_arrested。
        /// </summary>
        public bool Apply(string key, int day)
        {
            if (Has(key)) return false;
            Applied.Add(key);
            switch (key)
            {
                case "serpico_rev":
                    CraSafeUntil = day + Tuning.StorySecDays;
                    PushSpark(Tuning.StorySerpicoSpark);
                    break;
                case "serpico_bm":
                    CrtDepeg *= Tuning.StoryBmCrtMult; CrtFreeze *= Tuning.StoryBmCrtMult;
                    break;
                case "serpico_sec":
                    SecUntil = day + Tuning.StorySecDays;
                    break;
                case "eleanor_leak":
                    CrcHaircut *= Tuning.StoryLeakHaircutMult;
                    PushSpark(Tuning.StoryLeakSpark);
                    break;
                case "eleanor_return":
                    CrcDepeg *= Tuning.StoryReturnCrcMult;
                    break;
                case "lun_arrested":
                    LunUntil = day + Tuning.StoryLunDays;
                    break;
                case "tumbler_kept":
                    MixerFee *= Tuning.TumblerKeptFee; MixerVolume *= Tuning.TumblerKeptVolume;
                    break;
                case "tumbler_reported":
                    MixerClosed = true;
                    break;
                case "tumbler_sold":
                    MixerFee *= Tuning.TumblerSoldFee; MixerRug *= Tuning.TumblerSoldRug;
                    break;
            }
            return true;
        }

        /// <summary>SPARK 在接下来几晚里一共涨到 total 倍（每晚乘相同的倍数）。</summary>
        private void PushSpark(double total)
        {
            int n = Tuning.StorySparkNights;
            // 已经在推：把剩下的叠上去
            double left = SparkPushNights > 0 ? Math.Pow(SparkPush, SparkPushNights) : 1;
            SparkPush = Math.Pow(left * total, 1.0 / n);
            SparkPushNights = n;
        }
    }

    /// <summary>
    /// 混币器运营者 Tumbler 的剧情线：解锁混币器、第 <see cref="Tuning.TumblerMinDay"/> 天起开始，四次上门。
    /// 1 自我介绍 → 2 说治安部在追查混币器（当晚混币器被制裁）→ 3 求你藏密钥盘（三选一：藏 / 举报 / 卖给黑市）→ 4 结局。
    /// 只记进度，对话和物品在接入层。
    /// </summary>
    public sealed class Tumbler
    {
        public const int Hide = 1, Report = 2, Sell = 3;
        public int StartDay = -1, Stage, Choice, VisitedDay = -1;
        public bool Done;

        /// <summary>第几次上门在哪一天（相对开始日）。</summary>
        public static readonly int[] Offsets = { 0, 3, 5, 9 };

        public int NextVisitDay => StartDay < 0 || Stage >= Offsets.Length ? -1 : StartDay + Offsets[Stage];

        /// <summary>每天开门时问：是否开始（明天第一次上门）。</summary>
        public bool TryStart(int day, bool mixerUnlocked)
        {
            if (StartDay >= 0 || Done || !mixerUnlocked || day < Tuning.TumblerMinDay) return false;
            StartDay = day + 1;
            return true;
        }

        /// <summary>今天该上门的是第几次（1–4），没有就返回 0（第 4 次举报过的话由接入层改成新闻：他被抓了）。</summary>
        public int Due(int day)
        {
            if (Done || StartDay < 0 || Stage >= Offsets.Length || VisitedDay == day) return 0;
            if (day < NextVisitDay) return 0;
            return Stage + 1;
        }

        /// <summary>这一次上门已经安排了。</summary>
        public void Visited(int day)
        {
            VisitedDay = day;
            Stage++;
            Done = Stage >= Offsets.Length;
        }
    }
}
