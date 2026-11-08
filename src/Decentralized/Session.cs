using System;
using System.Collections.Generic;
using System.Linq;
using Decentralized.Domain;
using Newtonsoft.Json;

namespace Decentralized
{
    /// <summary>上一晚的挖矿报告（终端"报告"页和晨间日志）。</summary>
    public sealed class NightReport
    {
        public int Day;
        public int Rigs, PoweredRigs;
        public double Hash, NetworkHash, Mined, Price, Kj;
        public List<string> Burnt = new List<string>();
    }

    /// <summary>地址投毒的计划：某个钱包转给某地址后，到了这一天收到一笔来自相似地址的 0 币转入。</summary>
    public sealed class PoisonPlan
    {
        public string Wallet, Target;
        public int Day;
    }

    /// <summary>一局的 mod 状态。整体序列化成 JSON 存进游戏的 mod 存档数据；显卡的健康度等存在显卡物品的标签上。</summary>
    public sealed class SaveData
    {
        /// <summary>存档数据的版本：2 起保险基金用新的数值，3 起 Poinbase 主池是订单簿，4 起出块奖励和 NPC 算力 ×10（第十一阶段），5 起 Poinbase 没有污点。</summary>
        public int Version = 5;
        public int Day;
        public Rng Rng = new Rng((ulong)DateTime.UtcNow.Ticks);
        public Market Market;
        public Stablecoins Stable = new Stablecoins();
        /// <summary>革命军 CRA 质押池里玩家各钱包的仓位。</summary>
        public Staking Staking = new Staking();
        /// <summary>浏览器页的全局转账账本。</summary>
        public Ledger Ledger = new Ledger();
        /// <summary>Poinbase 永续合约账本（保证金用 Poinbase 账户的 CRC）。</summary>
        public PerpBook Perps = new PerpBook();
        /// <summary>预测市场：盘口、持仓、观测记录（下注资金用 Poinbase 账户的 CRC）。</summary>
        public PredictBook Predict = new PredictBook();
        /// <summary>高息理财（庞氏）：一个存档只能开一次。</summary>
        public Ponzi Ponzi = new Ponzi();
        /// <summary>NPC 骗局：空投与授权、第三方收益池、骗子地址。</summary>
        public Scams Scams = new Scams();
        /// <summary>日 K 历史（PSC，Poinbase 主池价格）。</summary>
        public CandleBook Candles = new CandleBook();
        /// <summary>游戏剧情选择对本 mod 世界的影响。</summary>
        public StoryMods Story = new StoryMods();
        /// <summary>今天被伪造转账骗走的（晨间日志用）：顾客名 + 信用点。</summary>
        public List<string> FakePaidToday = new List<string>();
        /// <summary>近几天的 gas 指数（每晚记明天的），情报线索里显示；最近一份局势简报。</summary>
        public List<double> GasHist = new List<double>();
        public List<string> Brief = new List<string>();
        public int BriefDay = -1;
        /// <summary>质押日息下调的新闻已经发过。</summary>
        public bool CraLowNews;
        public Network Network = new Network();
        /// <summary>已加载的链上钱包（硬件钱包拖到终端上加载），各自的余额、污点、成本、冻结独立；没有主钱包这回事，销毁后里面的币永久丢失。</summary>
        public List<Wallet> Wallets = new List<Wallet>();
        /// <summary>终端当前连接的钱包的编号，-1 是没有连接：Web3 页的交易、黑市、混币器都用它，用币付款的顾客也进它。</summary>
        public int Current = -1;
        /// <summary>Poinbase 托管账户：银行存取、Poinbase 交易、出金审查都在这里。</summary>
        public Wallet Poinbase = new Wallet { Address = "poinbase" };

        [JsonIgnore] public bool Connected => Current >= 0 && Current < Wallets.Count;

        /// <summary>当前连接的钱包；没有连接时是一个空的一次性钱包（读到的余额都是 0，写进去的东西不保留）。动手的操作要先看 <see cref="Connected"/>。</summary>
        [JsonIgnore] public Wallet Hot => Connected ? Wallets[Current] : new Wallet();

        /// <summary>按地址找自己的钱包；找不到（钱包已销毁或从没有过）是一个一次性钱包，发给它的钱等于丢了。</summary>
        public Wallet WalletAt(string address) => Wallets.Find(w => ChainAddress.Same(w.Address, address)) ?? new Wallet { Address = address };

        public Wallet Own(string address) => Wallets.Find(w => ChainAddress.Same(w.Address, address));

        /// <summary>钱包编号：每添加一个加一，销毁后不回收，名字不会错位。</summary>
        public int WalletSeq;

        public Wallet AddWallet(bool poisoned)
        {
            var w = new Wallet { Address = ChainAddress.Make(Rng.NextUInt()), Poisoned = poisoned, Name = (++WalletSeq).ToString() };
            Wallets.Add(w);
            if (!Connected) Current = Wallets.Count - 1;   // 第一个加载的钱包自动连接
            return w;
        }

        /// <summary>链上资产合计（全部钱包）。</summary>
        public double ChainValue(Func<string, double> price) { double v = 0; foreach (var w in Wallets) v += w.Value(price); return v; }
        public Exchange Exchange = new Exchange();
        public Mixer Mixer = new Mixer();
        /// <summary>换币台的累计成交与客户地址编号。</summary>
        public OtcDesk Otc = new OtcDesk();
        /// <summary>自营混币器（买下场外换币台就开放）。</summary>
        public OwnMixer OwnMix = new OwnMixer();
        public BlackMarket Black = new BlackMarket();
        public List<MarketEvent> ActiveEvents = new List<MarketEvent>();
        public NightReport LastNight;
        public bool OpeningNewsShown;
        /// <summary>各个收款方的地址（平台、混币器、Ponero 兑换处等），第一次用到时随机生成并存档。付款前要手输。</summary>
        public Dictionary<string, string> Addresses = new Dictionary<string, string>();

        public string Address(string key)
        {
            if (!Addresses.TryGetValue(key, out var a) || !ChainAddress.Valid(a)) { a = ChainAddress.Make(Rng.NextUInt()); Addresses[key] = a; }
            return a;
        }

        /// <summary>
        /// 链上记账：钱包的每一笔进出都走这里。写进浏览器账本；转出方、转入方是自己的钱包时，也写进那个钱包自己的转账记录
        /// （history: false 只记账本，矿池每晚的收益用，免得把钱包的记录挤满）。Ponero 是隐私币，不上浏览器。数额为 0 的只在 zero 时记（地址投毒）。
        /// </summary>
        public void Chain(string from, string to, string asset, double amount, double fee = 0, long block = -1, bool history = true, bool zero = false)
        {
            if (string.IsNullOrEmpty(asset) || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || amount < 0 || (amount <= 1e-12 && !zero)) return;
            int day = StoreStation.GetDayCounter();
            if (asset != Assets.Ponero) Ledger.Add(new LedgerEntry { Block = block >= 0 ? block : Block, Day = day, From = from, To = to, Asset = asset, Amount = amount, Fee = fee });
            if (!history) return;
            var src = Own(from ?? "");
            if (src != null) TxRules.Record(src.History, new TxRecord { Addr = to, Asset = asset, Amount = amount, Day = day });
            var dst = Own(to ?? "");
            if (dst != null && dst != src) TxRules.Record(dst.History, new TxRecord { Addr = from, Asset = asset, Amount = amount, Day = day, In = true });
        }

        public static Dictionary<string, double> Snapshot(Wallet w) => new Dictionary<string, double>(w.Balances);

        public Dictionary<string, Dictionary<string, double>> SnapshotAll() => Wallets.ToDictionary(w => w.Address, Snapshot);

        /// <summary>一次操作前后钱包余额的差，按币种记账：少了的记"钱包 → party"，多了的记"party → 钱包"。inOnly：只记转入（付款那一笔已经按转账记过）。</summary>
        public void ChainDiff(Wallet w, Dictionary<string, double> before, string party, bool inOnly = false)
        {
            foreach (var asset in w.Balances.Keys.Union(before.Keys).ToList())
            {
                double diff = w.Get(asset) - (before.TryGetValue(asset, out var b) ? b : 0);
                if (diff > 1e-9) Chain(party, w.Address, asset, diff);
                else if (diff < -1e-9 && !inOnly) Chain(w.Address, party, asset, -diff);
            }
        }

        /// <summary>夜间结算这类会动多个钱包的操作：所有自己的钱包各按差额记账。</summary>
        public void ChainDiffAll(Dictionary<string, Dictionary<string, double>> before, string party)
        {
            foreach (var w in Wallets) if (before.TryGetValue(w.Address, out var b)) ChainDiff(w, b, party);
        }
        /// <summary>网络拥堵的起止天（含）。</summary>
        public int CongestedFrom = -1, CongestedUntil = -1;
        /// <summary>当天的 gas 指数（夜间结算算出第二天的）与这一局的周期相位。</summary>
        public double Gas = 1;
        public int GasPhase = -1;
        public List<PoisonPlan> PoisonPlans = new List<PoisonPlan>();

        /// <summary>链上的区块高度：每签一笔交易往上走几块。</summary>
        public long Block = 1204331;
        /// <summary>当天用币付款的成交额（信用点），夜间清零。</summary>
        public double CoinSalesToday;
    }

    /// <summary>当前这一局的状态：读档 / 新游戏时载入，睡觉结算后写回。</summary>
    public static class Session
    {
        private const string Key = "state";
        public static SaveData Data;

        /// <summary>今晚有电的矿架上的显卡（矿架在夜间早段扣电时登记，结算时用）。</summary>
        public static readonly List<KeyValuePair<GameItem, ModuleBonus>> TonightCards = new List<KeyValuePair<GameItem, ModuleBonus>>();
        /// <summary>今晚已扣过电的矿机编号：每台矿机每晚只算一次（外力在白天触发夜间结算，不会重复登记显卡和扣电）。</summary>
        public static readonly HashSet<int> TonightDrawn = new HashSet<int>();
        /// <summary>今晚参与挖矿的显卡（或 ASIC 矿机）属于哪台机器：挖矿产出按机器设置的收款地址发。</summary>
        public static readonly Dictionary<GameItem, GameItem> TonightMachine = new Dictionary<GameItem, GameItem>();
        public static int TonightRigs, TonightPoweredRigs;
        public static double TonightKj;

        public static void Load()
        {
            Data = null;
            WalletWindow.Reset();
            TerminalWindow.ClearCart();
            try
            {
                string json = ModHelper.GetModData(ModInfo.Id, Key);
                if (!string.IsNullOrEmpty(json)) Data = JsonConvert.DeserializeObject<SaveData>(json);
            }
            catch (Exception ex) { Log.Error($"读取存档数据失败，重新开始：{ex.Message}"); }
            if (Data == null) Data = new SaveData();
            // 池子表在第十阶段改过（全部以 CRC 计价）；旧测试档里的表缺池子，重建成默认值（价格从起始价重来）
            if (Data.Market == null || Data.Market.DexPool(Assets.SPARK) == null) Data.Market = Market.CreateDefault(StoreStation.GetDayCounter());
            // 旧存档里的商家地址没有校验位：换成合法地址
            foreach (var s in Data.Black.Sellers) if (!ChainAddress.Valid(s.Name)) s.Name = ChainAddress.Make(Data.Rng.NextUInt());
            // 保险基金按新的规模补足
            if (Data.Version < 2) Data.Perps.InsuranceFund = Math.Max(Data.Perps.InsuranceFund, Tuning.PerpFund0);
            // Poinbase 主池换成订单簿（按当前价格、新手续费；侧池夜里按新深度重设）
            if (!Data.Market.Main.Book)
                Data.Market.Pools[0] = Pool.CreateBook(Venue.Exchange, Assets.PSC, Assets.CRC, Data.Market.Price, Tuning.PoolDepth(StoreStation.GetDayCounter()), Tuning.ExchangeFee);
            Data.Market.AddPoinSpark(Data.Stable.Spark, StoreStation.GetDayCounter());
            Data.Market.UpgradeDexPools(StoreStation.GetDayCounter());
            if (Data.Version < 3) { Data.Version = 3; Log.Info("存档数据升级到版本 3：保险基金、Poinbase 订单簿"); }
            // 第十一阶段：出块奖励和 NPC 算力同乘 10（均衡时每 H 收入不变，玩家算力占比变低）；旧存档按当时的减半进度一起放大
            if (Data.Version < 4)
            {
                Data.Network.Reward *= Tuning.MiningScale;
                Data.Network.NpcHash *= Tuning.MiningScale;
                Data.Version = 4;
                Log.Info($"存档数据升级到版本 4：出块奖励 {Data.Network.Reward:0}、NPC 算力 {Data.Network.NpcHash:0}");
            }
            // 第十一阶段 stage 5：污点按币种记脏枚数、只在钱包里。旧的污点额按当前价格摊成脏枚数；Poinbase 账户和永续仓位的挪到 1 号钱包
            if (Data.Version < 5)
            {
                double was = TaintMigration.Run(Data.Wallets, Data.Poinbase, Data.Perps.Positions, Data.Mixer, Data.Black, Data.Staking, Data.Scams, Data.Predict, Data.OwnMix ?? new OwnMixer(), Price);
                Data.Version = 5;
                Log.Info($"存档数据升级到版本 5：旧污点 {was:0} 摊成各币的脏枚数，现在按现价共 {Data.Wallets.Sum(w => w.Taint(Price)):0}");
            }
            if (Data.Otc == null) Data.Otc = new OtcDesk();
            if (Data.OwnMix == null) Data.OwnMix = new OwnMixer();
            if (Data.Story == null) Data.Story = new StoryMods();
            StoryMods.Current = Data.Story;
            Log.Info($"状态已载入（第 {Data.Day} 天，PSC {Data.Market.Price:0.00}）");
            Log.Guard("换币灯", OtcLamp.Attach);
        }

        public static void Save()
        {
            if (Data == null) return;
            try { ModHelper.SetModData(ModInfo.Id, Key, JsonConvert.SerializeObject(Data)); }
            catch (Exception ex) { Log.Error($"写入存档数据失败：{ex.Message}"); }
        }

        /// <summary>链上资产的信用点价格。</summary>
        public static double Price(string asset)
        {
            if (asset == Assets.PSC) return Data.Market.Price;
            var spec = Assets.Get(asset);
            if (asset == Assets.Ponero) return BlackMarket.PoneroPrice(Data.Market.Price);
            if (asset == Assets.SPARK) return Data.Stable.Spark;
            if (spec != null && spec.Stable) return Data.Stable.Price(asset);
            return 0;
        }

        public static double Rent() => PlayerStore.Instance != null ? PlayerStore.Instance.rentValue : 200;
    }
}
