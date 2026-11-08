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
        /// <summary>治安部整顿到这一天：混币器清洗变差，制裁概率、操纵查处概率上调。</summary>
        public int SecUntil = -1;
        /// <summary>伦警官被捕后的立威期到这一天：操纵查处概率、换币客户里便衣的概率上调。</summary>
        public int LunUntil = -1;
        /// <summary>SPARK 每晚额外乘的倍数，还剩几晚。</summary>
        public double SparkPush = 1;
        public int SparkPushNights;
        /// <summary>混币器：手续费、匿名集（NPC 混币量）、跑路概率的倍率；被举报后永久关闭（在途的照常提回，不能再存）。</summary>
        public double MixerFee = 1, MixerVolume = 1, MixerRug = 1;
        public bool MixerClosed;
        /// <summary>混币器换皮重开过（换了名字、地址）；重开的是治安部的钓鱼混币器（提回的币一律被标记）。</summary>
        public bool MixerRebranded, MixerHoneypot;
        /// <summary>本 mod 自己的剧情线：混币器运营者 Tumbler。</summary>
        public Tumbler Tumbler = new Tumbler();
        /// <summary>Tumbler 四次上门之后的剧情：探员、泄露、审判、黑市跑路、报复、长期联系人、换皮重开。</summary>
        public TumblerAfter After = new TumblerAfter();

        public bool Has(string key) => Applied.Contains(key);

        public double CraSpiral(int day) => day < CraSafeUntil ? Tuning.StoryCraSpiralMult : 1;
        /// <summary>混币器每轮清洗率的倍率（治安部监控资金流向期间洗得更差）。</summary>
        public double MixClean(int day) => day < SecUntil ? Tuning.StorySecMixClean : 1;
        /// <summary>换币客户里便衣概率的倍率（伦警官被捕后的立威期）。</summary>
        public double OtcSting(int day) => day < LunUntil ? Tuning.StoryLunMult : 1;
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
                case "tumbler_bm_rug":
                    MixerClosed = true;
                    break;
            }
            return true;
        }

        /// <summary>混币器换皮重开：重新开放，费率、匿名集、跑路倍率回到 1；钓鱼混币器的费率打折（引人上钩）。</summary>
        public void Rebrand(bool honeypot)
        {
            MixerClosed = false;
            MixerRebranded = true;
            MixerHoneypot = honeypot;
            MixerFee = honeypot ? Tuning.TumblerHoneypotFee : 1;
            MixerVolume = 1; MixerRug = 1;
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
        /// <summary>Tumbler 的外观（第一次上门时随机定下，之后每次都一样）。</summary>
        public string Sprite;

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

        /// <summary>
        /// 今天该上门的是第几次（1–4），没有就返回 0（第 4 次举报过的话由接入层改成新闻：他被抓了）。
        /// after 不为空时：第 3 次要等治安部探员来过（第二天起），配合过探员又选了藏的，第 4 次要等搜查过（第二天起）。
        /// </summary>
        public int Due(int day, TumblerAfter after = null)
        {
            if (Done || StartDay < 0 || Stage >= Offsets.Length || VisitedDay == day) return 0;
            if (day < NextVisitDay) return 0;
            if (after != null && Stage == 2 && (after.Agent == 0 || day <= after.AgentDay)) return 0;
            if (after != null && Stage == 3 && after.NeedSearch(this) && (!after.Searched || day <= after.SearchDay)) return 0;
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

    /// <summary>
    /// Tumbler 四次上门之后的剧情（原来的进度类不动）：
    /// 治安部探员在第 2、3 次上门之间来一次（配合 / 否认）；配合过又选了藏的，第 4 次前一天来搜查；
    /// 举报、被搜走、第 4 次盘不在手上 → 密钥落到治安部，混币记录泄露；举报 → 审判盘；卖给黑市 → 黑市跑路；
    /// 无罪获释、黑市跑路 → Tumbler 报复；藏好归还 → 长期联系人；混币器关停若干天后换皮重开。只记进度，对话、物品、夜间效果在接入层。
    /// </summary>
    public sealed class TumblerAfter
    {
        public const int Cooperate = 1, Deny = 2;
        /// <summary>探员上门的结果（0 = 还没来）和那一天。</summary>
        public int Agent, AgentDay = -1;
        /// <summary>探员的外观（问话和搜查是同一个人）。</summary>
        public string AgentSprite;
        /// <summary>搜查过（不管有没有搜到）、搜走了密钥盘、哪一天。</summary>
        public bool Searched, Seized;
        public int SearchDay = -1;
        /// <summary>哪一晚泄露（-1 = 没安排）；已经泄露过（整局一次）。</summary>
        public int LeakDay = -1;
        public bool Leaked;
        /// <summary>藏好并归还：成为长期联系人；已经提示过的镇压开始日。</summary>
        public bool Contact;
        public List<int> Tipped = new List<int>();
        /// <summary>审判：开盘日、宣判日、判决（1 定罪，0 无罪，-1 没有审判）；每晚的庭审新闻偏向（true = 控方占上风）；最后一条新闻是哪晚。</summary>
        public int TrialOpenDay = -1, TrialDay = -1, Verdict = -1, HintDay = -1;
        public List<bool> Hints = new List<bool>();
        public bool TrialMarket, TrialDone;
        /// <summary>报复在哪一晚（-1 = 没有）、已经报复过。</summary>
        public int RevengeDay = -1;
        public bool Revenged;
        /// <summary>黑市卷款跑路在哪一晚（-1 = 没有）、已经跑路。</summary>
        public int BmRugDay = -1;
        public bool BmRugged;
        /// <summary>混币器关停的那天（-1 = 没关停）；换皮重开过。</summary>
        public int ClosedDay = -1;
        public bool Rebranded;

        /// <summary>探员今天该不该来：第 2 次上门之后的第二天起，还没来过。</summary>
        public bool AgentDue(Tumbler t, int day) => !t.Done && t.Stage == 2 && Agent == 0 && day > t.VisitedDay;

        /// <summary>配合过探员、又选了藏，第 4 次上门前要先来搜查。</summary>
        public bool NeedSearch(Tumbler t) => Agent == Cooperate && t.Choice == Tumbler.Hide;

        /// <summary>搜查今天该不该来：第 4 次上门的前一天起（第 3 次之后的第二天起）。</summary>
        public bool SearchDue(Tumbler t, int day) => !t.Done && t.Stage == 3 && NeedSearch(t) && !Searched && day >= t.NextVisitDay - 1 && day > t.VisitedDay;

        /// <summary>安排今晚泄露（整局一次）。</summary>
        public void LeakTonight(int day)
        {
            if (!Leaked && LeakDay < 0) LeakDay = day;
        }

        /// <summary>举报：掷出判决，安排开盘和宣判；混币器从今天起关停。</summary>
        public void StartTrial(int day, Rng rng)
        {
            if (Verdict >= 0) return;
            Verdict = rng.Chance(Tuning.TumblerTrialConvict) ? 1 : 0;
            TrialOpenDay = day + Tuning.TumblerTrialOpen;
            TrialDay = day + Tuning.TumblerTrialDays;
            ClosedDay = day;
        }

        /// <summary>今晚的庭审新闻（开盘前一晚起到宣判前一晚，每晚一条）：偏向真实判决的概率 HintAcc。没有就返回 null。</summary>
        public bool? Hint(int day, Rng rng)
        {
            if (Verdict < 0 || TrialDone || day < TrialOpenDay - 1 || day >= TrialDay || HintDay >= day) return null;
            HintDay = day;
            bool truth = Verdict == 1;
            bool h = rng.Chance(Tuning.TumblerTrialHintAcc) ? truth : !truth;
            Hints.Add(h);
            return h;
        }

        /// <summary>公开信息下的定罪概率：先验按已经登出的庭审新闻做贝叶斯更新。</summary>
        public double TrialPublic()
        {
            double acc = Tuning.TumblerTrialHintAcc, lr = Math.Log(acc / (1 - acc)), p0 = Tuning.TumblerTrialConvict;
            double l = Math.Log(p0 / (1 - p0));
            foreach (var h in Hints) l += h ? lr : -lr;
            return 1 / (1 + Math.Exp(-l));
        }

        /// <summary>卖给黑市：安排黑市卷款跑路的那一晚。</summary>
        public void StartSold(int day, Rng rng)
        {
            if (BmRugDay < 0) BmRugDay = day + rng.Range(Tuning.TumblerBmRugMin, Tuning.TumblerBmRugMax);
        }

        /// <summary>今晚该不该换皮重开：关停满 RebrandDays 天，只一次。</summary>
        public bool RebrandDue(int day) => !Rebranded && ClosedDay >= 0 && day >= ClosedDay + Tuning.TumblerRebrandDays;
    }
}
