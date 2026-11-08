namespace Decentralized.Domain
{
    /// <summary>
    /// 数值（普通难度基准）。改数值只改这里。
    /// </summary>
    public static class Tuning
    {
        /// <summary>人脉网络升级价格：Web3 治理社区（入口）、混币器。</summary>
        public const int UnlockPrice = 400, MixerUnlockPrice = 300;

        // 挖矿
        public const double PscStartPrice = 100;
        /// <summary>第十一阶段把出块奖励和 NPC 初始算力同乘的倍数（100 → 1000、360 → 3600）；读旧存档时按它放大全网。</summary>
        public const double MiningScale = 10;
        public const double BlockReward = 1000;
        public const int HalvingDays = 90;
        /// <summary>减半对币价的传导：每次减半在区间内随机一个指数，价格锚上移 2^指数 倍（0.15–0.85 即约 +11% 到 +80%）。
        /// 参考比特币：每次减半后的涨幅差别很大，市场也已部分预期到，所以既不固定也不翻倍。</summary>
        public const double HalvingPassMin = 0.15, HalvingPassMax = 0.85;
        /// <summary>减半前一周的预期行情：锚逐日抬高，到减半当天为 +8%。</summary>
        public const int HalvingRampDays = 7;
        public const double HalvingRamp = 0.08;
        /// <summary>价格锚向目标的逐晚靠拢比例（约 7 天完成大半）。</summary>
        public const double SupplyAdjust = 0.15;
        public const double NpcHash0 = 3600;
        public const double NpcKjPerH = 2.4;
        public const double ElecPrice = 1.5;
        public const double NpcTargetRatio = 3.5;
        public const double NpcAdj = 0.05;
        public const double PoolFee = 0.03;
        public const int RigPrice = 400, RigKj = 2;
        /// <summary>矿架上模块百分比的折算系数（原版机器用 2–4）。模块效果不设上限，只有防出错的下限：算力、耗电、每日损耗三个乘数各自不低于下面的值。</summary>
        public const double ModuleRatio = 4, ModuleHashFloor = 0, ModuleKjFloor = 0.2, ModuleWearFloor = 0;
        public const double DeathBase = 0.001, DeathWorn = 0.03, FakeDeathFactor = 2;
        public const double RefurbishHealth = 30;

        // 市场
        /// <summary>交易对信用点一侧的基础深度（DEX 侧池按比例取）。</summary>
        public const double PoolDepth0 = 600000;
        /// <summary>Poinbase 订单簿的冲击曲线：价格 = 参考价 × exp(κ · |x|^α)，x = 当天净买入的名义价值 / 深度。第 38 天买 2 PSC 平均滑点约 0.1%，卖 200 PSC 约 4%。</summary>
        public const double BookK = 1.54, BookAlpha = 0.8;
        /// <summary>DEX 手续费；Poinbase 现货与出金的手续费（对照头部交易所 taker 约 0.1%）。</summary>
        public const double DexFee = 0.003, ExchangeFee = 0.001;
        /// <summary>DEX 稳定币对（CRT/CRC、CRA/CRC）的 StableSwap 池：手续费（参考 Curve 3pool 0.04%）、放大系数；
        /// 参考价偏离挂钩超过 1% 是脱锚（流动性变差，A 降到 20，参考 USDC 2023 年 3 月最低约 0.87、两三天回到 1），超过 15% 是崩盘（流动性被撤走，A 降到 1、深度只剩四分之一）。</summary>
        public const double StableFee = 0.0004, StableAmp = 200, StableAmpDepeg = 20, StableAmpBroken = 1, StableDepegBand = 0.01, StableBrokenBand = 0.15, StableBrokenDepth = 0.25;
        /// <summary>NPC 大户每笔成交额（信用点）的范围。</summary>
        public const double WhaleMin = 24000, WhaleMax = 96000;
        public const double Revert = 0.15;
        public const double PscVol = 0.04;
        public const double SentimentKappa = 0.02, SentimentVol = 0.02;
        public const double PriceFloor = 0.3, PriceCeiling = 3.0;

        // 稳定币
        public const double CrcDepegChance = 0.30, CrcDepegMin = 0.88, CrcDepegMax = 0.93;
        public const int CrcDepegDaysMin = 2, CrcDepegDaysMax = 3;
        public const double CrcHaircutChance = 0.15, CrcHaircut = 0.85;
        public const double CrtDepegChance = 0.40, CrtDepegMin = 0.92, CrtDepegMax = 0.96;
        public const int CrtDepegDaysMin = 1, CrtDepegDaysMax = 2;
        public const double CrtFreezeBase = 0.40, CrtFreezePerRep = 0.008, CrtFreezeShare = 0.5;
        public const int CrtUnfreezeAfter = 3;
        public const double CrtSwapFee = 0.02;
        public const double CraRate = 0.005, CraRateLow = 0.002;
        public const int CraUnlockDays = 4;
        public const double CraSubsidyWarn = 0.2, CraNpcStake = 20000;
        /// <summary>CRA 部分抵押：初始流通量与抵押率、抵押率下限与每晚调整步长；R/S 安全线、崩盘线、安全线到崩盘线之间的最大折价；
        /// 挪用储备的比例范围；劫掠上层补回的储备（占流通量）；一级市场铸造等待天数；官方跑路系数（× (1 − CRA 价格)）。数值由 research/crasim 定。</summary>
        public const double CraS0 = 60000, CraC0 = 0.8, CraCrMin = 0.5, CraCrStep = 0.02;
        public const double CraSafeRatio = 0.6, CraCrashRatio = 0.35, CraDiscountMax = 0.15;
        public const double CraEmbezzleMin = 0.4, CraEmbezzleMax = 0.8, CraLoot = 0.09;
        public const int CraMintDaysMin = 1, CraMintDaysMax = 2;
        public const double CraRugPerDepeg = 1;
        /// <summary>Poinbase 出金到钱包的确认动画：基础秒数，加每个确认的秒数（确认数同 PLEDGER 的 TxConfirm*）。</summary>
        public const double FundsBaseSecs = 1.2, FundsConfSecs = 0.6;
        /// <summary>浏览器账本最多保留的转账条数，一次最多显示的条数。</summary>
        public const int LedgerMax = 500, LedgerShow = 12;
        public const double CraSpiralWeekly = 0.03;
        public const int CraWarnLeadMin = 4, CraWarnLeadMax = 7;
        public static readonly double[] CraSpiralPath = { 0.85, 0.30, 0.02 };
        /// <summary>平衡代币 SPARK：起始价与长期锚（信用点）、日波动率、每晚向锚回归的比例、价格上下限（锚的倍数）；铸销手续费；
        /// 革命军袭击上层 / 下层动荡开始时的冲击；CRA 死亡螺旋三天里相对螺旋前价格的倍数。</summary>
        public const double SparkStart = 10, SparkVol = 0.08, SparkRevert = 0.03, SparkMinMult = 0.3, SparkMaxMult = 3, SparkFee = 0.003;
        public const double SparkRaidShock = 1.15, SparkUnrestShock = 0.90;
        public static readonly double[] SparkSpiralPath = { 0.8, 0.3, 0.05 };

        /// <summary>出金到银行：超出成本的部分征税。</summary>
        public const double TaxRate = 0.08;
        /// <summary>混币器：每轮手续费、基准清洗率、NPC 背景量（信用点）、最大轮数、最小存入、制裁概率与天数、每晚跑路概率。</summary>
        public const double MixerFee = 0.03, MixerCleanRate = 0.35, MixerNpcVolume = 20000, MixerMin = 100, MixerSanctionChance = 0.5, MixerRugChance = 0.004;
        public const int MixerMaxRounds = 5, MixerSanctionDays = 5;
        /// <summary>各派系顾客用币付款的概率。</summary>
        public const double CoinPayCrc = 0.30, CoinPayCrt = 0.50, CoinPayCra = 0.50, CoinPayPsc = 0.25;
        /// <summary>用币付款的溢价：顾客多付的比例，补偿持币的污点与兑出的手续费。</summary>
        public const double CoinPayPremium = 0.12;
        /// <summary>顾客验货：说法里每一项隐瞒被看穿的概率；型号档次不符（供电口、风扇数一眼可见）必被看穿；一次看穿最多扣的信誉。</summary>
        public const double CustomerSpotChance = 0.50, CustomerSpotTierMismatch = 1.0;
        public const int RepLossMax = 25;
        /// <summary>杰克逊博士上货：进阶 / 旗舰货的起始天（有特殊进货渠道时旗舰第 20 天起），矿架补货、进阶货（及第二件）、旗舰货的概率。</summary>
        public const int DealerAdvancedFromDay = 10, DealerFlagshipFromDay = 30, DealerSpecialFlagshipFromDay = 20;
        public const double DealerRigRestock = 0.30, DealerAdvanced = 0.60, DealerAdvancedSecond = 0.30, DealerFlagship = 0.25;
        /// <summary>二手贩子：每天来访概率与最早来访日；骗子占比 = 基础 + 每天增量，封顶；中间商占比；中间商加价；中间商 / 骗子夹带假卡的比例；
        /// 中间商把每项问题说成没问题的概率（骗子全说）。</summary>
        public const double UsedVisitChance = 0.05, UsedScamBase = 0.10, UsedScamPerDay = 0.004, UsedScamMax = 0.30, UsedMiddlemanShare = 0.30;
        public const double UsedMiddlemanMarkup = 0.08, UsedFakeMiddleman = 0.10, UsedFakeScammer = 0.45, UsedMiddlemanHide = 0.70;
        public const int UsedFirstDay = 3;
        /// <summary>显卡价值 = 型号定价 ×（下限 + (1 − 下限) × 健康度）。</summary>
        public const double CardValueFloor = 0.25;
        /// <summary>假卡的四项真伪依据（风扇数、供电口、背面丝印、序列号）各自露出破绽的概率（至少露出一项）；约对应平均 1.5 项。</summary>
        public const double FakeSignChance = 0.27;
        /// <summary>黑市、鉴定等处二手卡的健康度范围（均匀取）。</summary>
        public const double CardHealthMin = 25, CardHealthMax = 85;
        /// <summary>二手贩子带来的卡的健康度范围，按老实人、中间商、骗子：越不老实卡越旧，平均每张卡约 0.5 / 1.5 / 2.5 项成色问题（叶片、金手指、挡板、封条）。</summary>
        public static readonly double[] UsedHealthMin = { 72, 55, 42 }, UsedHealthMax = { 97, 85, 72 };
        /// <summary>外观折价（占价值的比例）：叶片积灰 / 缺角，金手指发暗 / 氧化，挡板划痕，封条缺失。</summary>
        public const double CardFanDust = 0.04, CardFanChipped = 0.12, CardFingersDark = 0.04, CardFingersOxidized = 0.12, CardBracketScratch = 0.02, CardSealMissing = 0.06;
        /// <summary>非主交易对每晚被拉回参考价的比例，及其深度占主交易对的比例。</summary>
        public const double SideArbRate = 0.75, SideDepthShare = 0.3, SparkDepthShare = 0.15;
        /// <summary>Poinbase 的 SPARK/CRC 订单簿深度占主池深度的比例。</summary>
        public const double PoinSparkDepthShare = 0.5;
        /// <summary>Web3 黑市：开放所需黑市声望，中 / 高档货所需声望，现货每周刷新的天数与基础件数（每高 BmStockPerRep 声望多一件），现货溢价。</summary>
        public const int BmOpenRep = 10, BmMidRep = 40, BmHighRep = 80, BmRefreshDays = 7, BmStockBase = 4;
        public const double BmStockPerRep = 40, BmPremium = 0.30;
        /// <summary>Ponero 兑换处：开放所需黑市声望、每次兑入 / 兑出的手续费、每天可兑换的额度（信用点）、对 PSC 的价格比。</summary>
        public const int PoneroOpenRep = 30;
        public const double PoneroFee = 0.06, PoneroDailyCap = 1000, PoneroPscRatio = 0.5;
        /// <summary>黑市订单：订单价相对游戏基础价的折扣、托管费、提前放款再折扣、提前放款所需卖家成交数、上门的天数、
        /// 上门被截获的概率（镇压期间再加）、骗子卖家占比、每晚平台跑路基础概率与每万信用点托管资金加的概率（封顶）、跑路后关闭的天数。</summary>
        public const double BmEscrowFee = 0.05, BmAdvanceDiscount = 0.10, BmInterceptDoor = 0.08, BmInterceptCrackdown = 0.15;
        public const double BmScamSellerShare = 0.15, BmRugBase = 0.002, BmRugPerTenK = 0.004, BmRugMax = 0.05;
        public const int BmAdvanceMinTrades = 30, BmDoorDays = 3, BmRugClosedDays = 10;
        /// <summary>蜜罐：每晚平台被执法接管的基础概率、每点治安声望加的概率、封顶、持续天数；之后订单被追查的延迟天数；
        /// 订单被截获时变成受控交付的概率；每晚某个近期卖家落网的概率。</summary>
        public const double BmHoneypotBase = 0.01, BmHoneypotPerSecRep = 0.0004, BmHoneypotMax = 0.06, BmControlled = 0.35, BmSellerBust = 0.008;
        public const int BmHoneypotDays = 6, BmTrapDelay = 4;
        /// <summary>商家货架的随机细节：现货每种的库存上限、每件价格的随机抖动（±）、全新矿卡的库存比常规少多少；尖端店铺里卖显卡的概率与显卡档次下限；
        /// 个人卖家里卖显卡的概率与其中全新的比例；骗子 / 诚实卖家是新号（成交少、好评随意）的概率；新号与老号的成交数、好评范围；论坛点名骗子的概率；
        /// 托管资金推高跑路概率超过基础的几倍时论坛出现"提币变慢"。</summary>
        public const int BmSpotQtyMax = 3, BmNewGpuQtyMinCut = 1, BmNewGpuQtyMaxCut = 2, BmEliteGpuTier = 3;
        public const double BmPriceJitter = 0.03, BmEliteGpuChance = 0.4, BmPersonalGpuChance = 0.15, BmPersonalGpuNew = 0.3, BmScamFresh = 0.6, BmHonestFresh = 0.25, BmForumScamHint = 0.5, BmForumRugFactor = 3;
        public const int BmFreshTradesMax = 12, BmOldTradesMin = 25, BmOldTradesMax = 220, BmScamOldRatingMin = 94, BmScamOldRatingMax = 99, BmFreshRatingMin = 70, BmFreshRatingMax = 100, BmOldRatingMin = 90, BmOldRatingMax = 99;
        /// <summary>追查记罪证：记入的比例（货款 × 比例）、同一次蜜罐（或一次落网）累计最多记多少。</summary>
        public const double BmTrapShare = 0.4, BmTrapWindowCap = 400;
        /// <summary>货架上除违禁品外的两类货：高科技物品（需中档声望）和显卡的出现概率、加价（在现货溢价 / 订单价之外再加），显卡里夹假卡的比例、是全新卡（成色 100、封条完好、不会是假卡）的概率。</summary>
        public const double BmTechChance = 0.30, BmGpuChance = 0.30, BmTechMarkup = 0.5, BmGpuMarkup = 0.2, BmGpuFake = 0.20, BmGpuNew = 0.15;
        /// <summary>玩家上架出售：标价倍率范围（相对参考价）、上架费、成交抽成、成交后货款托管天数、假卡被买家看穿的概率；
        /// 倍率 ≤0.9 / 1.0 / 1.8 时的期望成交天数（之间插值）；货款带污点的概率与比例随倍率从下限涨到上限；卖家好评的初始值、看穿一次扣的点数、同时上架的件数上限。</summary>
        public const double BmListFee = 0.05, BmCommission = 0.08, BmSellMin = 0.6, BmSellMax = 1.8, BmFakeDetect = 0.5;
        /// <summary>出售成交天数的倍率界线：不高于 BmSellFastMult 倍用 BmSellDaysFast 天，BmSellDay1Mult 倍用 BmSellDays1 天，之间线性，再往上指数到 BmSellMax。</summary>
        public const double BmSellFastMult = 0.9, BmSellDay1Mult = 1.0;
        /// <summary>出售页可选的标价倍率档位。</summary>
        public static readonly double[] BmSellMults = { 0.6, 0.8, 0.9, 1.0, 1.2, 1.5, 1.8 };
        public const double BmSellDaysFast = 1, BmSellDays1 = 2, BmSellDaysMax = 14;
        public const double BmSaleTaintChanceMin = 0.2, BmSaleTaintChanceMax = 0.9, BmSaleTaintRatioMin = 0.2, BmSaleTaintRatioMax = 0.8;
        public const int BmSaleHold = 3, BmRatingStart = 90, BmRatingFakeLoss = 10;
        /// <summary>同时上架的货：最多几种（按商品标识），每种最多几件。</summary>
        public const int BmMaxKinds = 6, BmMaxStack = 5;
        /// <summary>收款币的买家权重：每接受一种币，夜间多一轮成交判定，轮次概率 = 成交概率 × 权重（CRT 为 1 即原有节奏）。</summary>
        public const double BmWeightPsc = 0.8, BmWeightCrt = 1, BmWeightCra = 0.6, BmWeightPonero = 0.4;
        /// <summary>PLEDGER 区块高度每晚自然增长的范围（区块数，含两端）。</summary>
        public const int BlocksPerDayMin = 120, BlocksPerDayMax = 170;
        /// <summary>商家货架：每周的商家数（其中 2 家从矿卡 / 矿架 / 模组 4 类里抽，其余从其他类型里抽）、每家的货种数、每种货的库存范围；大宗商家出现概率、库存范围、送货天数；
        /// 各类商家的价格系数范围（订单价 = 基础价 × 系数）；批量折扣档（件数、折扣）。</summary>
        public const int BmMerchantCount = 7, BmCoreMerchants = 2, BmMerchantListings = 3, BmQtyMin = 2, BmQtyMax = 6, BmBulkQtyMin = 10, BmBulkQtyMax = 25, BmBulkDays = 6;
        public const double BmBulkChance = 0.25, BmFactorMin = 0.70, BmFactorMax = 0.95, BmFactorNewMin = 0.85, BmFactorNewMax = 1.00, BmFactorUsedMin = 0.65, BmFactorUsedMax = 0.90, BmFactorBulkMin = 0.55, BmFactorBulkMax = 0.75;
        /// <summary>商家货架上单件基础价的上限（过贵的货不上，免得一件把周转量撑歪）。</summary>
        public const long BmMaxUnitValue = 4000;
        /// <summary>尖端店铺：每周出现概率、货种数、库存范围、单件基础价下限、价格系数范围、送货天数。</summary>
        public const double BmEliteChance = 0.15, BmFactorEliteMin = 0.85, BmFactorEliteMax = 1.00;
        public const int BmEliteListings = 2, BmEliteQtyMax = 2, BmEliteDays = 5;
        public const long BmEliteMinValue = 2000;
        /// <summary>个人卖家：每周家数、货种数、库存上限、每件的价格系数范围（各件独立随机）、是骗子的概率、混进尖端货的概率。</summary>
        public const int BmPersonalCount = 2, BmPersonalListings = 3, BmPersonalQtyMax = 2;
        public const double BmPersonalFactorMin = 0.50, BmPersonalFactorMax = 1.20, BmPersonalScam = 0.25, BmPersonalEliteShare = 0.10;
        public const int BmBulk1Count = 5, BmBulk2Count = 10;
        public const double BmBulk1Discount = 0.05, BmBulk2Discount = 0.10;
        /// <summary>出售量越大货款越容易带污点：近 7 天成交额达到参考量时，污点概率与比例各加最多 BmVolumeTaintMax。</summary>
        public const double BmVolumeRef = 14000, BmVolumeTaintMax = 0.30;
        /// <summary>黑市人脉升级的价格。</summary>
        public const int BmUnlockPrice = 600;
        /// <summary>单个金融违规事件记入的罪证上限。</summary>
        public const double CrimeEventCap = 400;

        /// <summary>硬件钱包：最多几个（含主钱包）；杰克逊博士的售价与每次进货上架的概率；Web3 商城的价格范围、每周平台现货上架的概率与件数上限；商城货里被投毒的比例。</summary>
        public const int WalletMax = 5, WalletJacksonPrice = 240, WalletMallMin = 150, WalletMallMax = 180, WalletMallQtyMax = 2;
        public const double WalletJacksonChance = 0.5, WalletMallChance = 0.5, WalletPoisonShare = 0.2;
        /// <summary>被投毒的钱包：币的总价值达到 DrainMin 起，每晚有概率被转空；价值越高越快：期望天数从门槛处的 SlowDays（7 天）按价值线性降到 DrainFull 处的 1 天，每晚概率 = 1 / 期望天数。</summary>
        public const double WalletDrainMin = 3000, WalletDrainFull = 20000, WalletDrainSlowDays = 7;

        /// <summary>转账手续费（信用点等值，按转出的币扣）：慢 / 普通 / 快；拥堵时乘以倍数。</summary>
        public const double TxFeeSlow = 0.5, TxFeeNormal = 1.5, TxFeeFast = 4, TxCongestedFeeMult = 4;
        /// <summary>Gas 档位的确认速度倍数（时间乘它）：Gas 越高确认越快、进度条走得越快；Poinbase 出金固定普通档。</summary>
        public const double TxSpeedSlow = 1.5, TxSpeedNormal = 1, TxSpeedFast = 0.5;
        /// <summary>PLEDGER 按住 ✓ 签名的基础秒数（普通档），乘档位速度倍数：快档更短、慢档更长。</summary>
        public const double TxHoldSecs = 1.2;
        /// <summary>Gas 价格指数：以 GasPeriod 天为周期正弦涨跌，振幅 GasAmp（0.5×–1.5×），每天再乘 1 ± GasNoise 的随机波动。</summary>
        public const int TxGasPeriod = 7;
        public const double TxGasAmp = 0.5, TxGasNoise = 0.15;
        /// <summary>网络拥堵：每晚开始拥堵的概率、持续天数上限；拥堵时普通档、慢档的确认间隔倍数，慢档被挤出内存池的概率。</summary>
        public const double TxCongestionChance = 0.12, TxCongestedNormalSlow = 2, TxCongestedSlowSlow = 3.5, TxSlowDrop = 0.3;
        public const int TxCongestionDaysMax = 2;
        /// <summary>确认数：默认 3；金额（信用点）≥ MidValue 时 6，≥ BigValue 时 12。</summary>
        public const int TxConfirmBase = 3, TxConfirmMid = 6, TxConfirmBig = 12;
        public const double TxConfirmMidValue = 2000, TxConfirmBigValue = 10000;
        /// <summary>地址投毒：每笔转出后被盯上的概率，0 币转入在 1–2 天后出现。剪贴板木马（被投毒的钱包）：粘贴时换成相似地址的概率。钓鱼公告：每晚出现的概率。</summary>
        public const double TxPoisonChance = 0.35, TxHijackChance = 0.3, TxPhishChance = 0.05;
        public const int TxPoisonDelayMax = 2;

        /// <summary>永续合约：开平仓手续费、强平费、维持保证金比例；保险基金初值（约 20 天的全网产出）、手续费进基金的比例；
        /// 基金见底时盈利仓位自动减仓的盈利折扣；资金费系数与上下限（每晚）；欠款日息、没收宽限天数、核销后拉黑天数；两个品种的最高杠杆与单仓名义价值上限。</summary>
        public const double PerpFee = 0.001, PerpLiqFee = 0.02, PerpMaint = 0.10;
        public const double PerpFund0 = 200000, PerpFeeToFund = 0.3, PerpAdlHaircut = 0.5;
        /// <summary>NPC 永续仓位（每晚按标记价的涨跌结算，进出保险基金）：亏损一侧的持仓占现货池深的倍数（SPARK 再乘 SparkDepthShare）；
        /// 涨跌幅达到 PerpNpcLiqScale 时亏损一侧全部爆仓，之前按比例爆仓；爆仓里穿仓的比例 = 基础 + 斜率 × (涨跌幅 − 免穿仓幅度)，封顶；
        /// 正常爆仓交给基金的剩余保证金（占名义价值）；穿仓的缺口（占名义价值）= 涨跌幅 × 系数。</summary>
        public const double PerpNpcOi = 1.0, PerpNpcLiqScale = 0.25, PerpNpcBankBase = 0.05, PerpNpcBankSlope = 1.5, PerpNpcBankFree = 0.12, PerpNpcBankMax = 0.95;
        public const double PerpNpcLeft = 0.005, PerpNpcGap = 0.3;
        /// <summary>爆仓新闻：标记价单日涨跌达到多少上报纸；NPC 平均每个仓位的名义价值（折算人数）。</summary>
        public const double PerpNewsMove = 0.15, PerpNpcAvgPos = 1500;
        public const double PerpFundCoef = 0.10, PerpFundCap = 0.003;
        /// <summary>永续的成交深度占现货主池的比例：开平仓的平均滑点 = 名义价值 / (深度倍数 × 池深)，和现货一样大单更贵。</summary>
        public const double PerpDepthMult = 5.0;
        /// <summary>永续成交深度的基数（与现货分开；原来 50,000，减半滑点后 100,000，校准见 research/perpsim）。</summary>
        public const double PerpDepth0 = 100000;
        /// <summary>开仓时按现价算权益至少要高出强平线多少（占名义价值的比例），否则拒绝（滑点太大，开了就被强平）。</summary>
        public const double PerpOpenBuffer = 0.02;
        public const double PerpDebtInterest = 0.02;
        public const int PerpDebtDays = 7, PerpBanDays = 30;
        public const int PerpPscLev = 5, PerpSparkLev = 3;
        public const double PerpPscCap = 20000, PerpSparkCap = 5000;

        /// <summary>RatDigg 矿机（ASIC）：黑市矿机商每周出现的概率；M1 / M2 / M3 需要的黑市声望与出现权重；新机占比与二手的健康度范围。</summary>
        public const double AsicChance = 0.25, AsicNewShare = 0.7;
        public const int AsicRepM1 = 40, AsicRepM2 = 60, AsicRepM3 = 80;
        public const double AsicWeightM1 = 1.0, AsicWeightM2 = 0.6, AsicWeightM3 = 0.4;
        public const int AsicUsedHealthMin = 40, AsicUsedHealthMax = 85;
        /// <summary>修好之后健康扣的点数；损坏的机器剩余价值占机价的比例。</summary>
        public const int AsicRepairHealthCut = 20;
        public const double AsicDeadValue = 0.15;
        /// <summary>"矿机时代"：第几天到第几天之间随机发生一次（永久），提前几天上报纸；NPC 矿工的耗电乘数（耗电效率翻倍 = 0.5）。</summary>
        public const int EraMin = 50, EraMax = 90, EraWarnMin = 4, EraWarnMax = 7;
        public const double EraKjMult = 0.5;

        /// <summary>预测市场：游戏第几天起开放；手续费；单盘额度 = 基础 + 每天增量 × 天数；所有钱包在单盘合计的份额上限（额度的倍数）；
        /// 同时开放的盘口数量范围、每天最多新开几个；按信用点额折诈骗罪证的比例与单次上限（永续被判操纵、Tumbler 泄露用）；
        /// 派系行动的日均开始概率（先验）；先验概率的夹紧范围；结算记录保留条数。</summary>
        public const int PredictUnlockDay = 8;
        public const double PredictFee = 0.01, PredictLoss0 = 800, PredictLossPerDay = 25, PredictCap = 2;
        /// <summary>预测盘的流动性：LMSR 的 b = 倍数 × 单盘额度 / ln 2，与先验无关（低先验盘不再更浅）。单盘额度只决定份额上限和消息价。</summary>
        public const double PredictDepth = 1.8;
        public const int PredictOpenMin = 3, PredictOpenMax = 6, PredictNewPerDay = 2;
        public const double PredictFraudShare = 0.5, PredictFraudCap = 400;
        public const double PredictFactionDaily = 0.06, PredictPMin = 0.03, PredictPMax = 0.97;
        public const int PredictHistory = 8;
        /// <summary>gas 盘的公开价里"不知道相位"的权重：越大，数清七天周期的玩家越赚。金融审查（agy_fin/subsim）发现 0.5 时 gas 盘胜率 96%、每周净赚约 1,400，调到 0.15。</summary>
        public const double PredictGasBlind = 0.15;
        /// <summary>盘口对消息的反应：每晚向"公开信息下的合理概率"靠拢的比例，以及盘口 logit 上的随机噪声（NPC 流量）。</summary>
        public const double PredictDrift = 0.5, PredictNoise = 0.35;
        /// <summary>夜里价格变化超过多少（概率）写进晨间日志。</summary>
        public const double PredictMoveLog = 0.15;
        /// <summary>情报贩子：局势简报价格（CRT）、三档内幕消息的价格（占盘口最大亏损的比例）与准确率、假情报概率（claim 与真相相反）、需要的黑市声望。</summary>
        public const double IntelBriefPrice = 120, IntelFake = 0.10;
        public static readonly double[] IntelTipPrice = { 0.02, 0.05, 0.12 }, IntelTipAcc = { 0.60, 0.80, 0.95 };
        public const int IntelRep = 20;
        /// <summary>操纵检出：玩家自己的大单把价格推离"没有你时的水平"的对数幅度（取绝对值）低于 ManipFree 不查；
        /// 超过后被查概率 = ManipBase + ManipSlope × (幅度 − ManipFree)，封顶 ManipMax；被查后照常兑付，赚到的钱记污点。</summary>
        public const double ManipFree = 0.16, ManipBase = 0.25, ManipSlope = 3, ManipMax = 0.95;

        /// <summary>高息理财（庞氏）：第几天起开放、开张的宣传费（CRC）、可选日息、每晚新存入的基数（随天数）和对日息的敏感指数、
        /// 池子上限（随天数）、每晚被取走的比例、每晚被举报概率（基础 + 日息系数 + 池子占上限的比例系数）、卷走或崩盘时记的罪证。</summary>
        public const int PonziMinDay = 15;
        public const double PonziAdCost = 800;
        public static readonly double[] PonziRates = { 0.02, 0.04, 0.06, 0.08, 0.10 };
        public const double PonziBase0 = 300, PonziBasePerDay = 15, PonziRateExp = 1.5;
        public const double PonziCap0 = 6000, PonziCapPerDay = 200, PonziWithdraw = 0.03;
        public const double PonziReport0 = 0.02, PonziReportRate = 0.5, PonziReportSize = 0.04;
        public const double PonziCrime = 400;

        /// <summary>插针：每天开门时出现的概率、幅度范围（对数价格的绝对值）、当晚回去的比例范围、不回去（真跌真涨）的概率。</summary>
        public const double WickChance = 0.07, WickMin = 0.15, WickMax = 0.30, WickRevertMin = 0.6, WickRevertMax = 1.0, WickReal = 0.3;
        /// <summary>空投：每晚出现新空投的概率、有效天数、骗局占比、正规空投奖励范围（信用点等值）、钓鱼授权后被转走的延迟（晚）。</summary>
        public const double AirdropChance = 0.35, AirdropScam = 0.4, AirdropRewardMin = 20, AirdropRewardMax = 120;
        public const int AirdropDays = 2, AirdropDrainMin = 1, AirdropDrainMax = 2;
        /// <summary>第三方收益池：同时最多几个、每晚新开的概率；日息档位；按日息的骗局概率，时间锁、审计各减多少；骗局池每晚跑路概率；
        /// 单池存款上限（随天数）；正规池的储备（日息 × 存款上限 × 天数）；时间锁取款等待天数。</summary>
        public const int YieldPoolMax = 3, YieldLockDays = 2;
        public const double YieldPoolChance = 0.25, YieldRugNightly = 0.18, YieldCap0 = 4000, YieldCapPerDay = 120, YieldReserveDays = 25;
        public static readonly double[] YieldRates = { 0.01, 0.02, 0.03 }, YieldScamByRate = { 0.15, 0.40, 0.75 };
        public const double YieldScamTimelock = 0.25, YieldScamAudit = 0.20;
        /// <summary>换币客户（场外换币台）：人脉升级价格；来卖币的比例（其余来买币）。</summary>
        public const int OtcUnlockPrice = 500;
        public const double OtcSellShare = 0.5;
        /// <summary>卖币客户：有相关事件时出手事件里来的币（全额污点）的概率，Ponero 兑换处开放后出手 Ponero 的概率；币的市值（房租的倍数）；
        /// 其他币的污点比例范围；现金价占市值的比例（污点比例 0 时、1 时，之间线性；Ponero 单独一档）；便衣的概率与镇压期间的加成；客户地址来历的最多笔数。</summary>
        public const double OtcEventChance = 0.7, OtcPoneroChance = 0.15, OtcValueMinRents = 4, OtcValueMaxRents = 15;
        public const double OtcTaintMin = 0.2, OtcTaintMax = 0.9, OtcCutClean = 0.80, OtcCutDirty = 0.45, OtcPoneroCutMin = 0.70, OtcPoneroCutMax = 0.80;
        public const double OtcSting = 0.05, OtcStingCrackdown = 0.10;
        public const int OtcHistoryMax = 3;
        /// <summary>买币客户：要买的币的市值（房租的倍数）；出价相对市价的随机溢价范围（可能低于市价，成不成交由玩家决定）。</summary>
        public const double OtcBuyMinRents = 2, OtcBuyMaxRents = 8;
        public const double OtcBidMin = -0.05, OtcBidMax = 0.30;
        /// <summary>
        /// 自营混币器（买下场外换币台就开放）：默认费率、可选费率；口碑（近期出款剩下的污点比例）跟出款走的速度、客户把剩下的污点比例折成费率的权重、Tumbler 关停时客户不混的额外成本、匿名集的半饱和值（每轮清洗率 = 基准 × 匿名集 /(匿名集 + 它)）、平均轮数、客户在途的币计入匿名集的比例；
        /// 基础需求（信用点，随天数增长）、黑市声望 0 时的需求系数、客流对成本差的灵敏度、每晚需求的随机幅度、单笔存入的平均额；
        /// 客户存入的币的污点比例范围；各币种的需求权重（PSC、CRC、CRT、CRA）；营业时每晚有便衣存入的概率和记罪证的比例（按一笔存入的额）；
        /// 风声每晚保留的比例和累积的标度、池子越大越难追查的半值（风声累积 = 存入 / 标度 / (1 + 匿名集 / 它)）、按风声的查封概率系数与上限、镇压期间的倍数、查封记罪证的比例、停业天数、风声提示的两档界线；卷款跑路记的诈骗罪证和黑市声望损失。
        /// </summary>
        public const double OwnMixFeeDefault = 0.03;
        public static readonly double[] OwnMixFees = { 0.01, 0.02, 0.03, 0.05, 0.08, 0.12 };
        public const double OwnMixRatingSpeed = 0.2, OwnMixTaintWeight = 0.1, OwnMixRivalGone = 0.03, OwnMixAnon0 = 20000, OwnMixAvgRounds = 3, OwnMixPendingWeight = 0.25;
        public const double OwnMixDemand0 = 40000, OwnMixDemandPerDay = 1400, OwnMixRepMin = 0.3, OwnMixFeeScale = 0.015, OwnMixNoise = 0.3, OwnMixTicket = 800;
        public const double OwnMixInTaintMin = 0.4, OwnMixInTaintMax = 1.0;
        public static readonly double[] OwnMixAssetWeights = { 0.3, 0.1, 0.4, 0.2 };
        public const double OwnMixSting = 0.03, OwnMixStingCrime = 0.1;
        public const double OwnMixHeatKeep = 0.9, OwnMixHeatScale = 100000, OwnMixHeatAnon0 = 160000, OwnMixSeizeRate = 0.002, OwnMixSeizeMax = 0.5, OwnMixSeizeCrackdown = 2, OwnMixSeizeCrime = 0.01, OwnMixHeatMid = 0.01, OwnMixHeatHigh = 0.03;
        public const int OwnMixSeizeDays = 7, OwnMixRugRep = 25;
        public const double OwnMixRugCrime = 400;

        /// <summary>假客服：每天上门的概率；伪造转账：用币付款的顾客里是骗子的比例；两者都从第几天起。</summary>
        public const double FakeSupportChance = 0.04, FakeTransferShare = 0.08;
        public const int ScamMinDay = 10;

        /// <summary>Web3 内置自动换币：商家不收手里的币时，按这个比例多付（DEX 手续费 0.3% 加上高滑点），省得自己去换。</summary>
        public const double BmAutoSwapSlip = 0.08;

        /// <summary>剧情联动：塞尔皮科证据交给革命军（CRA 补贴池注资、螺旋概率倍率与期限、SPARK 几晚内涨到的倍数）、交给黑市（CRT 脱锚与被出卖的倍率）、
        /// 交给治安部（期限内混币器每轮清洗率、制裁、操纵查处的倍率）；埃莉诺泄露数据盘（CRC 托管减记倍率、SPARK 涨幅）、交回（CRC 脱锚倍率）；
        /// 伦警官被捕（期限与倍率：操纵查处、换币客户里的便衣）；SPARK 推涨分几晚走完。</summary>
        public const double StoryRevSubsidy = 10000, StoryCraSpiralMult = 0.5, StorySerpicoSpark = 1.2;
        public const double StoryBmCrtMult = 0.5;
        public const int StorySecDays = 30;
        public const double StorySecMixClean = 0.7, StorySecSanctionMult = 1.5, StorySecEnforceMult = 1.3;
        public const double StoryLeakHaircutMult = 2, StoryLeakSpark = 1.15, StoryReturnCrcMult = 0.5;
        public const int StoryLunDays = 14, StorySparkNights = 5;
        /// <summary>混币器运营者 Tumbler：第几天起开始；帮他藏好密钥盘后的混币器手续费、匿名集倍率和谢礼（CRT）；举报的赏金（信用点）和治安部声望；
        /// 卖给黑市得的 CRT（一半带污点）、黑市声望、之后混币器的手续费和跑路倍率；密钥盘的价值（违禁品，搜到按它记罪证）。</summary>
        public const int TumblerMinDay = 30;
        public const double TumblerKeptFee = 0.5, TumblerKeptVolume = 1.5, TumblerGift = 1000;
        public const int TumblerBounty = 1200, TumblerSecRep = 8, TumblerBmRep = 5;
        public const double TumblerSellCrt = 3000, TumblerSoldFee = 1.5, TumblerSoldRug = 4;
        public const int TumblerDriveValue = 600;
        /// <summary>卖给黑市得的 CRT 里带污点的比例。</summary>
        public const double TumblerSellTaint = 0.5;
        /// <summary>Tumbler 的后续剧情：混币器出款记录保留几天、最多几条（泄露时这些出款洗掉的污点加回原钱包）；
        /// 治安部探员：配合时的治安部声望，之后搜走密钥盘记的违禁品罪证。</summary>
        public const int TumblerLeakDays = 30, TumblerReturnsMax = 300, TumblerAgentSecRep = 5, TumblerSearchCrime = 300;
        /// <summary>审判盘：举报后第几天开盘、第几天宣判；定罪的概率（举报时掷出）；每晚庭审新闻偏向真实判决的概率。</summary>
        public const int TumblerTrialOpen = 2, TumblerTrialDays = 7;
        public const double TumblerTrialConvict = 0.7, TumblerTrialHintAcc = 0.7;
        /// <summary>报复：获释或黑市跑路后几天动手；给当前钱包植入木马（否则地址投毒）的概率；地址投毒每个钱包最多几笔。</summary>
        public const int TumblerRevengeDelay = 3, TumblerRevengePoison = 3;
        public const double TumblerRevengeWalletShare = 0.5;
        /// <summary>卖给黑市后，黑市卷款跑路在第几天（随机，含两端）。</summary>
        public const int TumblerBmRugMin = 10, TumblerBmRugMax = 20;
        /// <summary>换皮重开：混币器关停后几天；是治安部钓鱼混币器的概率；钓鱼混币器的费率倍率。</summary>
        public const int TumblerRebrandDays = 15;
        public const double TumblerHoneypot = 0.25, TumblerHoneypotFee = 0.5;
        public const double StoryLunMult = 1.3;

        /// <summary>行情图的 NPC 成交量：每天的基础量占主池深度的比例（按币种），价格波动越大成交越多（|涨跌幅| × 倍数），再乘 0.6–1.4 的随机。</summary>
        public const double ChartVolPsc = 0.12, ChartVolCrc = 0.10, ChartVolCrt = 0.06, ChartVolCra = 0.04, ChartVolSpark = 0.03, ChartVolPonero = 0.01, ChartVolMove = 8;

        /// <summary>主交易对（信用点一侧）的深度随天数增长。</summary>
        public static double PoolDepth(int day) => PoolDepth0 * (1 + day / 60.0);

        /// <summary>永续的成交深度，同样随天数增长。</summary>
        public static double PerpDepth(int day) => PerpDepth0 * (1 + day / 60.0);
    }
}
