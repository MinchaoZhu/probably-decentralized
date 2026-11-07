using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// 每天的推进：开门时日内波动和 NPC 大户；睡觉时（游戏 EndNight 的最后）挖矿、损耗与暴毙、全网、稳定币、行情结算，写晨间日志，存档。
    /// 矿架自己在夜间早段扣电并登记显卡（Items.NightDraw），这里只结算。
    /// </summary>
    public static class Night
    {
        public static void Subscribe()
        {
            ModHook.OnShutterOpenedLate += OnOpen;
            ModHook.OnGoingSleepEarly += OnSleepEarly;
            ModHook.OnGoingSleepLate += OnSleepLate;
        }

        public static void Unsubscribe()
        {
            ModHook.OnShutterOpenedLate -= OnOpen;
            ModHook.OnGoingSleepEarly -= OnSleepEarly;
            ModHook.OnGoingSleepLate -= OnSleepLate;
        }

        private static bool Active => Session.Data != null && Unlock.IsUnlocked;

        /// <summary>游戏新闻事件与派系行动 → 行情事件。</summary>
        public static List<MarketEvent> ActiveEvents()
        {
            var list = new List<MarketEvent>();
            var mgr = StoreStation.Instance?.storeEventManager;
            if (mgr == null) return list;
            foreach (var e in mgr.GetActiveEvents())
            {
                switch (e.identifier)
                {
                    case "stationWideBlackout": case "powerShortage": case "powerPlantMaintenance": list.Add(MarketEvent.Blackout); break;
                    case "crackdown": case "sec_martial_law": list.Add(MarketEvent.Crackdown); break;
                    case "bm_gang_war": list.Add(MarketEvent.GangWar); break;
                    case "rev_raid_upper_level": list.Add(MarketEvent.RevRaidUpper); break;
                    case "ll_riot": case "ll_worker_strike": list.Add(MarketEvent.LowerUnrest); break;
                }
            }
            return list.Distinct().ToList();
        }

        private static void OnOpen()
        {
            if (!Active) return;
            Log.Guard("开门行情", () =>
            {
                var d = Session.Data;
                int day = StoreStation.GetDayCounter();
                d.Market.Open(ActiveEvents(), d.Rng, day);
                CandleChart.Morning(d, day);
                double whale = d.Market.Whale(d.Rng);
                d.Candles.Tick(Assets.PSC, day, d.Market.Price, whale);
                if (day >= Tuning.ScamMinDay) { double wk = d.Market.TryWick(d.Rng, day); if (wk != 0) Log.Info($"插针 {wk:+0.000;-0.000}，当晚回去 {d.Market.WickBack:0.00}"); }
                CandleChart.Touch(d, day, Assets.PSC, Assets.Ponero);
                PredictGame.Morning(d, day);
                if (!d.OpeningNewsShown)
                {
                    News.Show("pd_news_open", L.T("news.open.title"), L.T("news.open.desc"), 2);
                    d.OpeningNewsShown = true;
                }
                Session.Save();
            });
        }

        /// <summary>挖矿产出按各台机器的算力占比，发到每台机器设置的收款地址；没设置或地址对应的钱包已销毁，这一份丢失。</summary>
        private static void PayMined(SaveData d, IEnumerable<(GameItem item, double hash)> cards, double mined, double totalHash)
        {
            var byMachine = new Dictionary<GameItem, double>();
            foreach (var (item, hash) in cards)
            {
                var machine = Session.TonightMachine.TryGetValue(item, out var mm) ? mm : item;
                byMachine[machine] = (byMachine.TryGetValue(machine, out var h) ? h : 0) + hash;
            }
            double lost = 0;
            foreach (var kv in byMachine)
            {
                double share = mined * kv.Value / totalHash;
                string addr = Payout.Get(kv.Key);
                var w = string.IsNullOrEmpty(addr) ? null : d.Own(addr);
                if (w == null) { lost += share; continue; }
                w.Add(Assets.PSC, share);
                d.Chain(d.Address("pool"), w.Address, Assets.PSC, share, history: false);
            }
            if (lost > 1e-9)
            {
                Log.Info($"挖矿产出丢失 {lost:0.####} PSC（矿机没设置收款地址，或地址对应的钱包已销毁）");
                PlayerStore.Instance.AddNightLog(L.T("nightlog.mined.lost", Fmt.Num(lost)));
            }
        }

        private static void OnSleepEarly()
        {
            Session.TonightCards.Clear();
            Session.TonightDrawn.Clear();
            Session.TonightMachine.Clear();
            Session.TonightRigs = Session.TonightPoweredRigs = 0;
            Session.TonightKj = 0;
        }

        private static void OnSleepLate()
        {
            if (!Active) return;
            Log.Guard("夜间结算", Settle);
        }

        /// <summary>链上的夜间事件：网络拥堵、地址投毒的 0 币转入、钓鱼公告。</summary>
        private static void ChainNight(SaveData d, int day)
        {
            if (d.GasPhase < 0) d.GasPhase = d.Rng.Range(0, Tuning.TxGasPeriod - 1);
            d.Gas = TxRules.Gas(day + 1, d.GasPhase, d.Rng);
            d.GasHist.Add(d.Gas); while (d.GasHist.Count > 10) d.GasHist.RemoveAt(0);
            Log.Info($"明天 gas ×{d.Gas:0.00}");
            if (day >= d.CongestedUntil && d.Rng.Chance(Tuning.TxCongestionChance))
            {
                d.CongestedFrom = day + 1;
                d.CongestedUntil = day + d.Rng.Range(1, Tuning.TxCongestionDaysMax);
                News.Show("pd_news_congest_" + day, L.T("news.congest.title"), L.T("news.congest.desc"), d.CongestedUntil - day);
            }
            foreach (var p in d.PoisonPlans.Where(x => x.Day <= day + 1).ToList())
            {
                d.PoisonPlans.Remove(p);
                var w = d.Own(p.Wallet);
                if (w == null) continue;
                string fake = ChainAddress.Lookalike(p.Target, d.Rng);
                d.Chain(fake, w.Address, Assets.PSC, 0, zero: true);
                Log.Info($"地址投毒：{p.Target} 的相似地址 {fake} 向 {w.Address} 转入 0 币");
            }
            if (d.Rng.Chance(Scales.R(Tuning.TxPhishChance)))
            {
                string fake = ChainAddress.Make(d.Rng.NextUInt());
                if (Unlock.BlackUnlocked && d.Rng.Chance(0.5)) d.Black.Forum.Add("phish:" + fake);
                else News.Show("pd_news_phish_" + day, L.T("news.phish.title"), L.T("news.phish.desc", fake), 2);
                Log.Info("钓鱼公告：" + fake);
            }
        }

        /// <summary>欠款逾期：没收物品抵债（用游戏自己的没收接口，和房贷逾期同一套，发一张没收单）；抵不够的核销并拉黑永续账户。</summary>
        private static void SeizeForDebt(SaveData d, int day)
        {
            var ps = PlayerStore.Instance;
            var items = ps.FindRepoItems(ps.GetAllRepoEligibleItems(), (int)Math.Ceiling(d.Perps.Debt));
            double value = 0;
            var names = new System.Text.StringBuilder();
            foreach (var it in items)
            {
                if (it == null) continue;
                value += it.GetCurrentValue(useRetailMarkup: false, withChild: true, includeEvents: true, forceMarkup: false, 0L);
                names.AppendLine(it.name);
            }
            foreach (var it in items) it?.Destroy();
            var slip = DirectoryMaster.Item("slip");
            slip.name = L.T("perp.slip.title");
            slip.ModifyTag("SLIP_TITLE", st => st.Enable().SetString(L.T("perp.slip.title")));
            slip.ModifyTag("SLIP_BODY", st => st.Enable().SetString(names.ToString()));
            ps.AddDirectSellingItemToTableMiddle(slip, true);
            double off = d.Perps.ApplySeizure(value, day);
            Log.Info($"永续欠款没收：抵 {value:0}，核销 {off:0}");
            ps.AddNightLog(L.T("nightlog.perp.seize", Fmt.Credits(value)) + (off > 0.5 ? L.T("nightlog.perp.writeoff", Fmt.Credits(off), Tuning.PerpBanDays) : ""), "cc7755");
        }

        private static void Settle()
        {
            var d = Session.Data;
            int day = StoreStation.GetDayCounter();
            if (d.Day == day) { Log.Info($"第 {day} 天夜间结算已执行过，跳过重复调用"); return; }
            StoryLinks.Night(d, day);
            TumblerStory.Night(d, day);
            var events = ActiveEvents();
            Log.Info($"夜间事件：{string.Join(",", events)}（新增 {string.Join(",", events.Where(e => !d.ActiveEvents.Contains(e)))}），黑市声望 {StoreReputation.GetBMReputation()}");
            bool wasSanctioned = d.Mixer.Sanctioned(day);
            foreach (var ev in events.Where(e => !d.ActiveEvents.Contains(e)))
            {
                d.Stable.OnEventStart(ev, day, d.Rng); d.Mixer.OnEvent(ev, day, d.Rng);
                // 镇压开始：黑市为换取放行，按声望出卖一批 CRT 地址
                if (ev == MarketEvent.Crackdown && Stablecoins.CrtFrozenByCrackdown(StoreReputation.GetBMReputation(), d.Rng))
                {
                    double frozen = d.Wallets.Sum(w => w.Freeze(Assets.CRT, Tuning.CrtFreezeShare, day + Tuning.CrtUnfreezeAfter));
                    if (frozen > 0)
                    {
                        PlayerStore.Instance.AddNightLog(L.T("nightlog.crt_freeze", Fmt.Num(frozen), day + Tuning.CrtUnfreezeAfter), "cc7755");
                        News.Show("pd_news_crtfreeze_" + day, L.T("news.crtfreeze.title"), L.T("news.crtfreeze.desc"), 3);
                    }
                }
            }
            foreach (var w in d.Wallets)
                foreach (var t in w.Thaw(day)) PlayerStore.Instance.AddNightLog(L.T("nightlog.thaw", Fmt.Num(t.Value), t.Key));
            if (!wasSanctioned && d.Mixer.Sanctioned(day) && Unlock.MixerUnlocked) News.Show("pd_news_sanction_" + day, L.T("news.sanction.title"), L.T("news.sanction.desc"), 3);
            d.ActiveEvents = events;
            if (Unlock.BlackUnlocked)
            {
                // 黑市货款放款、订单退款都从平台转回钱包
                var bmBefore = d.SnapshotAll();
                BlackDelivery.Night(d, day, events.Contains(MarketEvent.Crackdown));
                d.ChainDiffAll(bmBefore, d.Address("platform"));
            }
            d.Network.Availability = (events.Contains(MarketEvent.Blackout) ? 0.8 : 1) * (events.Contains(MarketEvent.LowerUnrest) ? 0.9 : 1);

            d.Block += TxRules.BlocksPerDay(d.Rng);
            var units = Session.TonightCards.Select(c => (item: c.Key, unit: Items.ToUnit(c.Key), bonus: c.Value)).ToList();
            double hash = units.Sum(x => x.unit.Hash(x.bonus));
            double mined = d.Network.Mine(hash);
            if (mined > 0 && hash > 0) PayMined(d, units.Select(x => (x.item, x.unit.Hash(x.bonus))), mined, hash);

            var report = new NightReport
            {
                Day = day, Rigs = Session.TonightRigs, PoweredRigs = Session.TonightPoweredRigs, Kj = Session.TonightKj,
                Hash = hash, NetworkHash = d.Network.NpcHash * d.Network.Availability + hash, Mined = mined,
            };
            foreach (var (item, unit, bonus) in units)
            {
                bool died = unit.Night(bonus, d.Rng);
                Items.Store(item, unit);
                if (died) report.Burnt.Add(item.name);
            }

            double rewardBefore = d.Network.Reward;
            d.Network.Night(day, d.Market.Price, hash);
            if (d.Network.Reward < rewardBefore) d.Market.OnHalving(d.Rng);
            if (d.Network.Reward < rewardBefore) News.Show("pd_news_halving_" + day, L.T("news.halving.title"), L.T("news.halving.desc"), 2);
            // 预测市场：先登记今晚的观测再结算到期的盘口；行情盘读夜间更新之前的价格
            double pscMark = d.Market.Mark;
            d.Market.UndoWick(day);
            CandleChart.Close(d, day);
            PredictGame.ClosePrice = d.Market.Price; PredictGame.CloseGas = d.Gas; PredictGame.ClosePush = d.Market.PlayerPush;
            // 永续：用今天的标记价（当日均价）结算，要在今晚的价格更新之前
            var perpEvents = d.Perps.Night(day, new Dictionary<string, double> { ["PSC"] = pscMark, ["SPARK"] = d.Stable.Spark }, d.Poinbase, Session.Price);
            foreach (var pp in d.Perps.Positions) Log.Info($"永续夜间：{pp.Product} {(pp.Long ? "多" : "空")} 保证金 {pp.Margin:0.00}，资金费率 {d.Perps.Rate(pp.Product) * 100:0.000}%，标记价 {(pp.Product == "PSC" ? pscMark : d.Stable.Spark):0.00}");
            if (perpEvents.Any(e => e.Kind == "seize")) SeizeForDebt(d, day);
            var stakeEvents = d.Staking.Night(day, d.Stable.CraRate, d.WalletAt);
            foreach (var se in stakeEvents) if (se.Released > 0) d.Chain(d.Address("staking"), se.Wallet, Assets.CRA, se.Released);
            double crcFactor = d.Stable.Night(day, d.Rng, d.Staking.Earning);
            var orderEvents = d.Stable.NightOrders(day, d.Rng);
            foreach (var oe in orderEvents)
            {
                var ow = d.WalletAt(oe.Order.Wallet);
                if (oe.Kind == CraOrderKind.Minted)
                {
                    ow.Add(Assets.CRA, oe.Cra); d.Staking.Stake(ow, oe.Cra, Session.Price);
                    d.Chain(d.Address("cra"), ow.Address, Assets.CRA, oe.Cra);
                    d.Chain(ow.Address, d.Address("staking"), Assets.CRA, oe.Cra);
                }
                else if (oe.Kind == CraOrderKind.Burned)
                {
                    ow.Add(Assets.CRC, oe.Crc); ow.Add(Assets.SPARK, oe.Spark);
                    d.Chain(d.Address("cra"), ow.Address, Assets.CRC, oe.Crc);
                    d.Chain(d.Address("cra"), ow.Address, Assets.SPARK, oe.Spark);
                }
            }
            if (d.Stable.CraWarnDay == day) News.Show("pd_news_crawarn_" + day, L.T("news.crawarn.title"), L.T("news.crawarn.desc"), 4);
            if (d.Stable.CraSubsidyLow && !d.CraLowNews) { d.CraLowNews = true; News.Show("pd_news_cralow_" + day, L.T("news.cralow.title"), L.T("news.cralow.desc"), 3); }
            if (crcFactor < 1)
                foreach (var w in d.Wallets.Append(d.Poinbase))
                    if (w.Get(Assets.CRC) > 0) w.Balances[Assets.CRC] = w.Get(Assets.CRC) * crcFactor;
            d.Market.UpdateSupply(day);
            if (Tuning.HalvingDays - day % Tuning.HalvingDays == 3) News.Show("pd_news_prehalving_" + day, L.T("news.prehalving.title"), L.T("news.prehalving.desc"), 3);
            d.Market.Night(events, d.Rng, day, Session.Price);
            report.Price = d.Market.Price;
            d.LastNight = report;
            d.Day = day;

            var ps = PlayerStore.Instance;
            if (mined > 0) ps.AddNightLog(L.T("nightlog.mined", Fmt.Num(mined)));
            var mixBefore = d.SnapshotAll();
            var mixed = d.Mixer.Night(d.WalletAt, day, Session.Price, d.Rng);
            d.ChainDiffAll(mixBefore, d.Address("mixer"));
            foreach (var m in mixed)
            {
                Log.Info($"混币器：{(m.Rugged ? "跑路" : "提回")} {m.Amount:0.##} {m.Asset}，污点 {m.TaintBefore:0} → {m.TaintAfter:0}，标记 {m.Flagged}");
                if (m.Rugged) ps.AddNightLog(L.T("nightlog.mixer_rug", Fmt.Num(m.Amount), m.Asset), "cc7755");
                else
                {
                    string line = L.T("nightlog.mixer", Fmt.Num(m.Amount), m.Asset, Fmt.Credits(m.TaintBefore), Fmt.Credits(m.TaintAfter)) + (m.Flagged ? L.T("nightlog.mixer_flag") : "");
                    if (m.Flagged) ps.AddNightLog(line, "cc7755"); else ps.AddNightLog(line);
                }
            }
            // 被投毒的硬件钱包：钱够多以后，别人用手里的助记词把它转空
            foreach (var w in d.Wallets.Where(x => x.Poisoned))
            {
                double v = w.Value(Session.Price);
                if (!d.Rng.Chance(Wallet.DrainChance(v))) continue;
                var drained = SaveData.Snapshot(w);
                w.Drain(Session.Price);
                d.ChainDiff(w, drained, d.Address("drainer"));
                Log.Info($"投毒钱包 {w.Address} 被转空：{v:0}");
                ps.AddNightLog(L.T("nightlog.drained", WalletWindow.NameOf(w), Fmt.Credits(v)), "cc7755");
            }
            ChainNight(d, day);
            switch (d.Network.EraNight(day, d.Rng))
            {
                case Network.EraStep.Warn: News.Show("pd_news_erawarn_" + day, L.T("news.erawarn.title"), L.T("news.erawarn.desc"), 5); break;
                case Network.EraStep.Start: News.Show("pd_news_era_" + day, L.T("news.era.title"), L.T("news.era.desc"), 5); Log.Info("矿机时代到来：NPC 耗电效率翻倍"); break;
            }
            var predictBefore = d.SnapshotAll();
            var predictEvents = PredictGame.Available(day) || d.Predict.Markets.Count > 0
                ? d.Predict.Night(day, PredictGame.ActiveIds(), m => PredictGame.Outcome(m, d), d.WalletAt, d.Rng, PredictGame.ClosePush) : new List<PredictEvent>();
            d.ChainDiffAll(predictBefore, d.Address("predict"));
            foreach (var mv in d.Predict.Drift(day, m => PredictGame.PublicProb(m, d, day), d.Rng))
            {
                if (Math.Abs(mv.To - mv.From) < Tuning.PredictMoveLog) continue;
                var why = PredictGame.Clues(mv.Market);
                ps.AddNightLog(L.T("nightlog.predict.move", PredictGame.Question(mv.Market), mv.From.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), mv.To.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)) + (why.Count > 0 ? L.T("nightlog.predict.why", why[0]) : ""));
            }
            var ponziBefore = d.SnapshotAll();
            var ponzi = d.Ponzi.Night(day, StoreReputation.GetStoreReputation(StoreReputation.FACTION_LOWER_LEVEL)?.GetReputation() ?? 0, d.Own(d.Ponzi.Wallet ?? "") ?? d.Hot, d.Rng);
            d.ChainDiffAll(ponziBefore, d.Address("ponzi"));
            if (ponzi != null)
            {
                ps.secData.CommitCrime("FRAUD", (int)Tuning.PonziCrime);
                StoreReputation.GetStoreReputation(StoreReputation.FACTION_LOWER_LEVEL)?.SetReputation(0);
                ps.AddNightLog(L.T(ponzi.Kind == "collapse" ? "nightlog.ponzi.collapse" : "nightlog.ponzi.reported", Fmt.Credits(ponzi.Amount)), "cc7755");
                News.Show("pd_news_ponzi_" + day, L.T("news.ponzi.title"), L.T("news.ponzi.desc"), 4);
                Log.Info($"理财崩盘：{ponzi.Kind}，池子 {ponzi.Amount:0} 被没收");
            }
            foreach (var e in d.Scams.Night(day, d.Rng, d.Own, new[] { Assets.CRT, Assets.PSC, Assets.CRC, Assets.CRA }, Session.Price))
            {
                var sw = d.Own(e.Wallet);
                string wn = sw != null ? WalletWindow.NameOf(sw) : e.Wallet;
                if (e.Kind == "drain") d.Chain(e.Wallet, d.Address("airdrop:" + e.Name), e.Asset, e.Amount);
                else if (e.Kind == "dry" || e.Kind == "paid") d.Chain(d.Address("yield:" + e.Name), e.Wallet, e.Asset, e.Amount);
                if (e.Kind == "drain") ps.AddNightLog(L.T("nightlog.scam.drain", wn, Fmt.Num(e.Amount), e.Asset, e.Name), "cc7755");
                else if (e.Kind == "rug") ps.AddNightLog(L.T("nightlog.scam.rug", e.Name, Fmt.Num(e.Amount), wn), "cc7755");
                else if (e.Kind == "dry") ps.AddNightLog(L.T("nightlog.scam.dry", e.Name, Fmt.Num(e.Amount), wn));
                else if (e.Kind == "paid") ps.AddNightLog(L.T("nightlog.scam.paid", e.Name, Fmt.Num(e.Amount), wn));
                if (sw != null) WalletWindow.RefreshDevice(sw);
            }
            foreach (var f in d.FakePaidToday) ps.AddNightLog(L.T("nightlog.scam.fakepay", f), "cc7755");
            d.FakePaidToday.Clear();
            foreach (var e in predictEvents)
            {
                if (e.Kind == "win") ps.AddNightLog(L.T("nightlog.predict.win", PredictGame.Question(e.Market), Fmt.Credits(e.Amount)));
                else if (e.Kind == "lose") ps.AddNightLog(L.T("nightlog.predict.lose", PredictGame.Question(e.Market), Fmt.Credits(e.Amount)), "cc7755");
                else if (e.Kind == "void") ps.AddNightLog(L.T("nightlog.predict.void", PredictGame.Question(e.Market), Fmt.Credits(e.Amount)));
                else if (e.Kind == "manip")
                {
                    ps.secData.CommitCrime("FRAUD", (int)Math.Round(e.Fraud));
                    ps.AddNightLog(L.T("nightlog.predict.manip", PredictGame.Question(e.Market), Fmt.Credits(e.Amount)), "cc7755");
                    News.Show("pd_news_manip_" + day, L.T("news.manip.title"), L.T("news.manip.desc"), 3);
                }
                else if (e.Kind == "caught")
                {
                    ps.secData.CommitCrime("FRAUD", (int)Math.Round(e.Fraud));
                    ps.AddNightLog(L.T("nightlog.predict.caught", PredictGame.Question(e.Market), Fmt.Credits(e.Amount)), "cc7755");
                }
            }
            foreach (var e in perpEvents)
            {
                string who = e.Product + " " + L.T(e.Long ? "perp.long" : "perp.short");
                if (e.Kind == "liq") ps.AddNightLog(L.T("nightlog.perp.liq", who, Fmt.Credits(e.Amount)), "cc7755");
                else if (e.Kind == "adl") ps.AddNightLog(L.T("nightlog.perp.adl", who, Fmt.Credits(e.Amount)), "cc7755");
                else if (e.Kind == "interest") ps.AddNightLog(L.T("nightlog.perp.interest", Fmt.Credits(e.Amount)), "cc7755");
                else if (e.Kind == "repaid") ps.AddNightLog(L.T("nightlog.perp.repaid", Fmt.Credits(e.Amount)), "cc7755");
                else if (e.Kind == "wipe")
                {
                    // 单日大涨大跌：全网爆仓上报纸（人数和金额含玩家被强平的仓位）
                    string dir = e.Long ? "down" : "up", pct = Math.Abs(e.Move * 100).ToString("0", CultureInfo.InvariantCulture), n = Fmt.Credits(e.Count);
                    News.Show("pd_news_wipe_" + e.Product + "_" + day, L.T("news.wipe." + dir + ".title", e.Product, pct, n), L.T("news.wipe." + dir + ".desc", e.Product, n, Fmt.Credits(e.Amount), Fmt.Credits(d.Perps.InsuranceFund)), 2);
                    Log.Info($"爆仓新闻：{e.Product} {e.Move:+0.000;-0.000}，{e.Count} 人，{e.Amount:0} 信用点，保险基金 {d.Perps.InsuranceFund:0}");
                }
            }
            foreach (var e in stakeEvents)
            {
                var sw = d.Own(e.Wallet);
                if (e.Released > 0) ps.AddNightLog(L.T("nightlog.unstaked", Fmt.Num(e.Released), sw != null ? WalletWindow.NameOf(sw) : e.Wallet));
            }
            foreach (var oe in orderEvents)
            {
                var ow = d.Own(oe.Order.Wallet);
                string wn = ow != null ? WalletWindow.NameOf(ow) : oe.Order.Wallet;
                if (oe.Kind == CraOrderKind.Minted) ps.AddNightLog(L.T("nightlog.cra.minted", Fmt.Num(oe.Cra), wn));
                else if (oe.Kind == CraOrderKind.Burned) ps.AddNightLog(L.T("nightlog.cra.burned", Fmt.Num(oe.Order.Cra), Fmt.Num(oe.Crc), Fmt.Num(oe.Spark), wn));
                else ps.AddNightLog(L.T("nightlog.cra.lost", Fmt.Num(oe.Order.Cra), oe.Order.Mint ? L.T("cra.mint") : L.T("cra.burn")), "cc7755");
            }
            double interest = stakeEvents.Sum(e => e.Interest);
            if (interest > 0) ps.AddNightLog(L.T("nightlog.stakeint", Fmt.Num(interest)));
            foreach (var name in report.Burnt) ps.AddNightLog(L.T("nightlog.burnt", name), "cc7755");
            int unpowered = report.Rigs - report.PoweredRigs;
            if (unpowered > 0 && units.Count + unpowered > 0) ps.AddNightLog(L.T("nightlog.unpowered", unpowered), "cc7755");
            if (d.CoinSalesToday > 0) ps.AddNightLog(L.T("nightlog.coinpay", Fmt.Credits(d.CoinSalesToday)));
            d.CoinSalesToday = 0;
            Log.Info($"夜间结算第 {day} 天：钱包污点 {d.Hot.Taint:0}，CRT {d.Hot.Get(Assets.CRT):0}（冻结 {(d.Hot.Frozen.TryGetValue(Assets.CRT, out var fz) ? fz : 0):0}），混币器在途 {d.Mixer.Pending.Count}，制裁至 {d.Mixer.SanctionedUntil}");
            Session.Save();
        }
    }

    /// <summary>
    /// mod 新闻：不带价格修正的新闻事件，和原版新闻一样出现在报纸上。报纸按重要度排序、最多登 4 条，
    /// 游戏自己的事件重要度多在 4–7，mod 新闻默认 5 才不会总被挤掉；剧情联动用 9 登头版。
    /// </summary>
    public static class News
    {
        public static void Show(string id, string title, string text, int days, int importance = 5)
        {
            var mgr = StoreStation.Instance?.storeEventManager;
            if (mgr == null || mgr.IsEventActive(id)) return;
            mgr.ActivateEvent(new StoreEvent
            {
                identifier = id, newsName = title, displayName = title, newsDescription = text,
                duration = days, eventType = StoreEvent.EventType.COSMETIC, eventArea = StoreEvent.EventArea.ALL,
                importance = importance, cooldown = 0, negociationDatas = new List<NegociationData>(),
            });
        }
    }
}
