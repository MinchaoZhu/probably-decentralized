using System;
using System.Collections.Generic;
using System.Linq;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// 预测市场的接入层：按游戏当前状态给出候选盘口、公开信息下的合理概率（盘口夜里向它靠拢）、线索、情报贩子的情报，
    /// 夜间读游戏事件、派系行动、水纯度、gas 和本 mod 的状态来结算。
    /// </summary>
    public static class PredictGame
    {
        public static readonly string[] Factions = { "FACTION_SECURITY", "FACTION_REVOLUTION", "FACTION_LOWER_LEVEL", "FACTION_UPPER_LEVEL", "FACTION_BLACK_MARKET" };
        private static readonly double[] GutterCuts = { 8300, 8700 }, RustCuts = { 9050, 9100 };
        private static readonly double[] PriceMult = { 0.9, 0.95, 1.0, 1.05, 1.1 };
        private static readonly double[] GasCuts = { 0.8, 1.0, 1.2, 1.4 };

        /// <summary>夜间结算开始时记下的收盘数据：行情盘读夜间更新前的价格，gas 盘读当天的 gas（接着链上夜间事件就会改成明天的）。</summary>
        public static double ClosePrice, CloseGas, ClosePush;

        public static bool Available(int day) => Session.Data != null && Unlock.IsUnlocked && day >= Tuning.PredictUnlockDay;

        /// <summary>情报贩子的门路：Web3 黑市解锁，且声望够。</summary>
        public static bool BrokerOpen => Unlock.BlackUnlocked && StoreReputation.GetBMReputation() >= Tuning.IntelRep;

        /// <summary>夜间观测用的标识：生效中的事件，以及派系行动的 "F:派系"。</summary>
        public static List<string> ActiveIds()
        {
            var list = new List<string>();
            var mgr = StoreStation.Instance?.storeEventManager;
            if (mgr == null) return list;
            foreach (var e in mgr.GetActiveEvents())
            {
                list.Add(e.identifier);
                if (e.isFactionOperation && !string.IsNullOrEmpty(e.sourceFaction)) list.Add("F:" + e.sourceFaction);
            }
            return list;
        }

        // ——— 游戏事件模型 ———

        private static double DayChance(StoreEventManager mgr)
        {
            int c = mgr.GetEventCounter();
            return c == 0 ? 0.50 : c == 1 ? 0.35 : c == 2 ? 0.25 : 0;
        }

        private static double EventProb(StoreEventManager mgr, string id, int days, bool ignoreCooldown = false)
        {
            var pool = StoreEventManager.normalEventBlueprints.Where(e => ignoreCooldown || !mgr.IsEventOnCooldown(e.identifier)).ToList();
            var bp = pool.Find(e => e.identifier == id);
            if (bp == null) return 0;
            return PredictMath.EventProb(ignoreCooldown ? 0.35 : DayChance(mgr), bp.odd, pool.Sum(e => (double)e.odd), days);
        }

        /// <summary>已经排进日程的事件 / 派系行动在窗口里的开始日（known = 只看玩家已知的）。</summary>
        private static int? QueuedStart(PredictMarket m, bool knownOnly)
        {
            var mgr = StoreStation.Instance?.storeEventManager;
            if (mgr == null || (m.Kind != PredictKind.Event && m.Kind != PredictKind.Faction)) return null;
            int today = StoreStation.GetDayCounter();
            for (int i = 0; i < mgr.futurEvents.Count; i++)
            {
                var e = mgr.futurEvents[i];
                if (knownOnly && !e.IsKnown) continue;
                bool match = m.Kind == PredictKind.Event ? e.identifier == m.Key : e.isFactionOperation && e.sourceFaction == m.Key;
                if (i >= mgr.futurEventsDay.Count) break;
                int start = today + mgr.futurEventsDay[i];
                if (match && start >= m.FromDay && start <= m.ToDay) return start;
            }
            return null;
        }

        private static bool FactionBusy(StoreEventManager mgr, string faction) => mgr.GetActiveEvents().Any(e => e.isFactionOperation && e.sourceFaction == faction);

        // ——— 候选盘口 ———

        public static List<PredictCandidate> Candidates(int day)
        {
            var d = Session.Data;
            var list = new List<PredictCandidate>();
            var mgr = StoreStation.Instance?.storeEventManager;
            if (mgr == null) return list;
            double dayChance = DayChance(mgr);
            var pool = StoreEventManager.normalEventBlueprints.Where(e => !mgr.IsEventOnCooldown(e.identifier)).ToList();
            double total = pool.Sum(e => (double)e.odd);
            foreach (var bp in pool)
            {
                if (mgr.IsEventActive(bp.identifier)) continue;
                string name;
                try { name = bp.storeEventFunc().GetDisplayName(); } catch (Exception) { continue; }
                double odd = bp.odd;
                list.Add(new PredictCandidate { Kind = PredictKind.Event, Key = bp.identifier, Title = name, Prob = (from, to) => PredictMath.EventProb(dayChance, odd, total, to - from + 1) });
            }
            foreach (var f in Factions)
            {
                if (FactionBusy(mgr, f)) continue;
                list.Add(new PredictCandidate { Kind = PredictKind.Faction, Key = f, Title = L.T("predict.faction." + f), Prob = (from, to) => PredictMath.FactionProb(to - from + 1) });
            }
            foreach (var t in GutterCuts)
            {
                double thr = t;
                list.Add(new PredictCandidate { Kind = PredictKind.Purity, Key = "gutter", Title = L.T("predict.purity.gutter"), Param = thr, Tomorrow = true, Prob = (a, b) => PredictMath.PurityAtLeast(thr, 8000, 8999) });
            }
            foreach (var t in RustCuts)
            {
                double thr = t;
                list.Add(new PredictCandidate { Kind = PredictKind.Purity, Key = "rust", Title = L.T("predict.purity.rust"), Param = thr, Tomorrow = true, Prob = (a, b) => PredictMath.PurityAtLeast(thr, 9000, 9150) });
            }
            double price = d.Market.Price, anchor = d.Market.Anchor(d.ActiveEvents);
            foreach (var mult in PriceMult)
            {
                double thr = Math.Round(price * mult, price >= 100 ? 0 : 1);
                list.Add(new PredictCandidate { Kind = PredictKind.Price, Key = "PSC", Title = "PSC", Param = thr, Prob = (from, to) => PredictMath.PriceAtLeast(price, anchor, Tuning.PscVol, Tuning.Revert, to - day, thr) });
            }
            if (d.GasPhase >= 0)
                foreach (var t in GasCuts)
                {
                    double thr = t;
                    list.Add(new PredictCandidate { Kind = PredictKind.Gas, Key = "gas", Title = "Gas", Param = thr, Tomorrow = true, Prob = (a, b) => GasPublic(d, day + 1, thr) });
                }
            if (d.Stable.CraWarnDay < 0 && d.Stable.CraCrashDay < 0)
                list.Add(new PredictCandidate { Kind = PredictKind.Cra, Key = "cra", Title = "CRA", Prob = (from, to) => PredictMath.CraWarnProb(d.Stable.Cushion, CraDrain(d), day, from, to, CraHazard) });
            if (!d.Network.EraDone && d.Network.EraDay > 0 && day + 4 >= (d.Network.EraWarned ? d.Network.EraWarnDay : Tuning.EraMin))
                list.Add(new PredictCandidate { Kind = PredictKind.Era, Key = "era", Title = L.T("predict.era"), Prob = (from, to) => PredictMath.EraProb(day, from, to, d.Network.EraWarned ? d.Network.EraWarnDay : -1) });
            return list;
        }

        /// <summary>gas 盘的公开价：绝大部分按真实相位、小部分按不知道相位（PredictGasBlind）——七天周期是链上公开的，盘口里的 NPC 多数看得见；数清周期的人只剩一点优势。</summary>
        private static double GasPublic(SaveData d, int day, double thr) =>
            d.GasPhase < 0 ? PredictMath.GasFlat(day, thr) : (1 - Tuning.PredictGasBlind) * PredictMath.GasAtLeast(day, d.GasPhase, thr) + Tuning.PredictGasBlind * PredictMath.GasFlat(day, thr);

        private static double CraHazard => 1 - Math.Pow(1 - Tuning.CraSpiralWeekly, 1.0 / 7);
        private static double CraDrain(SaveData d) => Stablecoins.CushionDrain(Tuning.CraNpcStake + d.Staking.Earning, d.Stable.CraRate);

        /// <summary>开门后补盘。价格盘一次只留一个（阈值不同的盘口会互相对冲）。</summary>
        public static void Morning(SaveData d, int day)
        {
            if (!Available(day)) return;
            ForceKnown(d, day);
            var made = d.Predict.Refill(day, d.Rng, Candidates(day));
            foreach (var m in made) Log.Info($"预测盘口 #{m.Id}：{m.Kind} {m.Key} {m.Title} 阈值 {m.Param} 窗口 {m.FromDay}-{m.ToDay} 先验 {m.P0:0.000}");
        }

        /// <summary>
        /// 柜台线人（游戏里卖消息的顾客）、传闻纸条和派系特权会把事件排进日程且标为已知：每个这样的事件保证有一个盘口（窗口 = 开始那天），
        /// 开盘价按公开信息下的基础概率，不因为已经排期而抬高——知道的人能赚到价格追上来之前的差。开始日是明天的来不及，不开。
        /// </summary>
        private static void ForceKnown(SaveData d, int day)
        {
            var mgr = StoreStation.Instance?.storeEventManager;
            if (mgr == null) return;
            var bk = d.Predict;
            for (int i = 0; i < mgr.futurEvents.Count; i++)
            {
                var e = mgr.futurEvents[i];
                if (i >= mgr.futurEventsDay.Count) break;
                int start = day + mgr.futurEventsDay[i];
                if (!e.IsKnown || start < day + 2 || e.eventType == StoreEvent.EventType.COSMETIC) continue;
                bool op = e.isFactionOperation;
                var kind = op ? PredictKind.Faction : PredictKind.Event;
                string key = op ? e.sourceFaction : e.identifier;
                if (!op && !StoreEventManager.normalEventBlueprints.Any(b => b.identifier == key)) continue;
                if (bk.Markets.Any(m => m.Outcome < 0 && m.Kind == kind && m.Key == key && m.FromDay <= start && m.ToDay >= start)) continue;
                if (bk.Markets.Count(m => m.Outcome < 0) >= Tuning.PredictOpenMax + 2) break;
                double p = op ? PredictMath.FactionProb(1) : EventProb(mgr, key, 1, true);
                string title = op ? L.T("predict.faction." + key) : e.GetDisplayName();
                var m2 = PredictMarket.Create(++bk.Seq, kind, key, title, 0, day, start, start, p);
                bk.Markets.Add(m2);
                Log.Info($"线人情报开盘 #{m2.Id}：{kind} {key} 第 {start} 天，先验 {p:0.000}");
            }
        }

        // ——— 公开信息下的合理概率（盘口夜里向它靠拢） ———

        /// <summary>
        /// 市场里的 NPC 看得到的东西：报纸上已经排期的事件、正在进行的事件与派系行动、冷却、当前行情、gas 的周期、补贴池、矿机时代预警。
        /// 看不到：没上报纸的排期（传闻、派系特权之外）、减半抬价和排期好的镇压对价格锚的未来影响。day 是今晚。
        /// </summary>
        public static double? PublicProb(PredictMarket m, SaveData d, int day)
        {
            var mgr = StoreStation.Instance?.storeEventManager;
            int days = m.ToDay - m.FromDay + 1;
            switch (m.Kind)
            {
                case PredictKind.Event:
                    if (mgr == null) return null;
                    if (QueuedStart(m, true) != null) return 0.92;
                    if (mgr.IsEventActive(m.Key) || mgr.IsEventOnCooldown(m.Key)) return 0.02;
                    return EventProb(mgr, m.Key, days);
                case PredictKind.Faction:
                    if (mgr == null) return null;
                    if (QueuedStart(m, true) != null) return 0.92;
                    if (FactionBusy(mgr, m.Key)) return 0.02;
                    return PredictMath.FactionProb(days);
                case PredictKind.Price:
                    return PredictMath.PriceAtLeast(d.Market.Price, d.Market.Anchor(d.ActiveEvents), Tuning.PscVol, Tuning.Revert, Math.Max(1, m.ToDay - day), m.Param);
                case PredictKind.Gas:
                    return d.GasPhase < 0 ? (double?)null : GasPublic(d, m.ToDay, m.Param);
                case PredictKind.Cra:
                    if (d.Stable.CraWarnDay >= 0 || d.Stable.CraCrashDay >= 0) return 0.02;
                    return PredictMath.CraWarnProb(d.Stable.Cushion, CraDrain(d), day + 1, m.FromDay, m.ToDay, CraHazard);
                case PredictKind.Era:
                    return PredictMath.EraProb(day, m.FromDay, m.ToDay, d.Network.EraWarned ? d.Network.EraWarnDay : -1);
            }
            return null;
        }

        // ——— 结算 ———

        /// <summary>夜间结算的结果：事件、派系读观测；纯度读当天的数值；行情读夜间更新之前的价格；gas 读当天的指数；CRA 预警与矿机时代读 mod 状态（要在这两项夜间逻辑之后结算）。</summary>
        public static bool? Outcome(PredictMarket m, SaveData d)
        {
            switch (m.Kind)
            {
                case PredictKind.Event: case PredictKind.Faction: return d.Predict.Happened(m);
                case PredictKind.Purity:
                    var st = StoreStation.Instance;
                    return st == null ? (bool?)null : (m.Key == "gutter" ? st.todayGutterflowPurity : st.todayRustwaterPurity) >= m.Param;
                case PredictKind.Price: return ClosePrice >= m.Param;
                case PredictKind.Gas: return CloseGas >= m.Param;
                case PredictKind.Cra: return d.Stable.CraWarnDay >= m.FromDay && d.Stable.CraWarnDay <= m.ToDay;
                case PredictKind.Era: return d.Network.EraDay < 0 ? (bool?)null : d.Network.EraDay >= m.FromDay && d.Network.EraDay <= m.ToDay;
            }
            return null;
        }

        // ——— 情报 ———

        /// <summary>这个盘口的真相是不是已经定了（情报贩子只能卖已经定了的）：派系行动看排期（包括没上报纸的），矿机时代看 mod 自己掷出的日子。</summary>
        public static bool? Truth(PredictMarket m)
        {
            var d = Session.Data;
            if (m.Kind == PredictKind.Faction) return QueuedStart(m, false) != null;
            if (m.Kind == PredictKind.Era) return d.Network.EraDay > 0 ? d.Network.EraDay >= m.FromDay && d.Network.EraDay <= m.ToDay : (bool?)null;
            return null;
        }

        public static bool Tippable(PredictMarket m, int day) => m.IsOpen(day) && Truth(m) != null && (m.Kind != PredictKind.Faction || m.ToDay <= day + 4);

        /// <summary>下注时玩家是否已经知道答案：这个事件（或派系行动）已经以玩家看得到的形式排进日程（传闻纸条、派系特权），或者买了情报贩子的内幕消息且说的就是这一边。</summary>
        public static bool Known(PredictMarket m, bool yes)
        {
            var tip = Session.Data?.Predict.TipFor(m.Id);
            if (tip != null && tip.Claim == yes) return true;
            return yes && QueuedStart(m, true) != null;
        }

        public static string Question(PredictMarket m)
        {
            string win = m.FromDay == m.ToDay ? L.T("predict.win.one", m.FromDay) : L.T("predict.win.range", m.FromDay, m.ToDay);
            switch (m.Kind)
            {
                case PredictKind.Event: return L.T("predict.q.event", win, m.Title);
                case PredictKind.Faction: return L.T("predict.q.faction", win, m.Title);
                case PredictKind.Purity: return L.T("predict.q.purity", win, m.Title, Fmt.Num(m.Param));
                case PredictKind.Gas: return L.T("predict.q.gas", win, Fmt.Num(m.Param));
                case PredictKind.Cra: return L.T("predict.q.cra", win);
                case PredictKind.Era: return L.T("predict.q.era", win);
                default: return L.T("predict.q.price", win, m.Title, Fmt.Num(m.Param));
            }
        }

        // ——— 线索 ———

        /// <summary>盘口面板里的"线索"：玩家手里已有的、和这个盘口有关的信息。每条尽量短。</summary>
        public static List<string> Clues(PredictMarket m)
        {
            var d = Session.Data;
            var list = new List<string>();
            var mgr = StoreStation.Instance?.storeEventManager;
            int today = StoreStation.GetDayCounter();
            var tip = d.Predict.TipFor(m.Id);
            if (tip != null) list.Add(L.T("predict.clue.tip", L.T("predict.tier." + tip.Tier), L.T(tip.Claim ? "predict.yes" : "predict.no")));
            switch (m.Kind)
            {
                case PredictKind.Event:
                    {
                        int? s = QueuedStart(m, true);
                        if (s != null) list.Add(L.T("predict.clue.queued", s.Value));
                        else if (mgr != null && (mgr.IsEventActive(m.Key) || mgr.IsEventOnCooldown(m.Key))) list.Add(L.T("predict.clue.cool"));
                        break;
                    }
                case PredictKind.Faction:
                    {
                        int? s = QueuedStart(m, true);
                        if (s != null) list.Add(L.T("predict.clue.queued", s.Value));
                        else if (mgr != null && FactionBusy(mgr, m.Key)) list.Add(L.T("predict.clue.busy"));
                        break;
                    }
                case PredictKind.Price:
                    {
                        int toHalv = Tuning.HalvingDays - today % Tuning.HalvingDays;
                        if (toHalv <= m.ToDay - today + Tuning.HalvingRampDays) list.Add(L.T("predict.clue.halving", today + toHalv));
                        if (mgr != null)
                            for (int i = 0; i < mgr.futurEvents.Count; i++)
                            {
                                if (i >= mgr.futurEventsDay.Count) break;
                                var e = mgr.futurEvents[i];
                                if (e.IsKnown && (e.identifier == "crackdown" || e.identifier == "stationWideBlackout" || e.identifier == "powerShortage"))
                                    list.Add(L.T("predict.clue.queued2", today + mgr.futurEventsDay[i], e.GetDisplayName()));
                            }
                        list.Add(L.T("predict.clue.anchor", Fmt.Num(d.Market.Anchor(d.ActiveEvents))));
                        if (Math.Abs(d.Market.PlayerPush) >= Tuning.ManipFree) list.Add(L.T("predict.clue.push", (d.Market.PlayerPush * 100).ToString("+0.0;-0.0", System.Globalization.CultureInfo.InvariantCulture)));
                        break;
                    }
                case PredictKind.Gas:
                    if (d.GasHist.Count > 0) list.Add(L.T("predict.clue.gas", string.Join(" ", d.GasHist.Skip(Math.Max(0, d.GasHist.Count - 7)).Select(x => x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))));
                    break;
                case PredictKind.Cra:
                    list.Add(L.T("predict.clue.subsidy", (d.Stable.Cushion / Stablecoins.Cushion0 * 100).ToString("0")));
                    list.Add(L.T("predict.clue.drain", Fmt.Num(CraDrain(d))));
                    break;
                case PredictKind.Era:
                    list.Add(d.Network.EraWarned ? L.T("predict.clue.era.warned", d.Network.EraWarnDay + Tuning.EraWarnMin, d.Network.EraWarnDay + Tuning.EraWarnMax) : L.T("predict.clue.era", Tuning.EraMin, Tuning.EraMax));
                    break;
            }
            return list;
        }

        // ——— 局势简报 ———

        private static string OpName(string id)
        {
            var bp = StoreOperationManager.operationBlueprints.Find(b => b.identifier == id);
            try { return bp != null ? bp.storeEventFunc().GetDisplayName() : id; } catch (Exception) { return id; }
        }

        /// <summary>按游戏的阈值列出每个派系现在能发起的行动。fake：假情报，数值是编的。</summary>
        public static List<string> MakeBrief(bool fake, int day)
        {
            var mgr = StoreStation.Instance?.storeEventManager;
            var om = StoreStation.Instance?.storeOperationManager;
            var lines = new List<string>();
            if (om == null || mgr == null) return lines;
            int sec = om.secPower, rev = om.revPower, bm = om.blackMarketPower, ll = om.lowerLevelUnrest, ul = om.upperLevelFriendliness;
            if (fake)
            {
                var rng = Session.Data.Rng;
                sec = rng.Range(0, 100); rev = rng.Range(0, 100); bm = rng.Range(0, 100); ll = rng.Range(0, 100); ul = rng.Range(0, 100);
            }
            lines.Add(L.T("predict.brief.levels", sec, rev, bm, ll, ul));
            Func<string, List<string>> ops = f =>
            {
                var l = new List<string>();
                switch (f)
                {
                    case "FACTION_SECURITY":
                        if (sec >= 20 && bm >= 40) l.Add("crackdown");
                        if (sec >= 20 && (rev >= 33 || ll >= 33 || bm >= 33)) l.Add("sec_increase_patrols");
                        if (sec >= 15 && rev >= 40) l.Add("sec_raid_revolution");
                        if (sec >= 15 && bm >= 40) l.Add("sec_raid_black_market");
                        if (sec >= 50 && (rev >= 65 || ll >= 65 || bm >= 65)) l.Add("sec_martial_law");
                        if (sec <= 5) l.Add("sec_budget_increase");
                        break;
                    case "FACTION_REVOLUTION":
                        if (day > 7) { if (rev <= 25) l.Add("rev_mobilization_secret"); if (rev >= 50) l.Add("rev_mobilization"); if (rev >= 15) l.Add("rev_raid_upper_level"); if (rev >= 25) l.Add("raid_security"); }
                        break;
                    case "FACTION_LOWER_LEVEL":
                        if (ll >= 85) l.Add("ll_riot"); if (ll >= 50) l.Add("ll_worker_strike");
                        break;
                    case "FACTION_UPPER_LEVEL":
                        if (ul >= 50 && ll >= 50) l.Add("ul_medical_aid"); if (ul >= 50 && ll <= 35) l.Add("ul_household_relief");
                        break;
                    case "FACTION_BLACK_MARKET":
                        if (day > 9) { if (bm >= 85) l.Add("bm_gang_war"); l.Add("bm_illegal_weapon_shipment"); }
                        break;
                }
                return l;
            };
            foreach (var f in Factions)
            {
                string name = L.T("predict.faction." + f);
                if (FactionBusy(mgr, f)) { lines.Add(L.T("predict.brief.busy", name)); continue; }
                if (mgr.futurEvents.Any(e => e.isFactionOperation && e.sourceFaction == f)) { lines.Add(L.T("predict.brief.queued", name)); continue; }
                var l = ops(f);
                lines.Add(l.Count == 0 ? L.T("predict.brief.none", name) : L.T("predict.brief.can", name, string.Join("、", l.Select(OpName))));
            }
            return lines;
        }
    }
}
