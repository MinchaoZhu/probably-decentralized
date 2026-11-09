using System;
using System.Collections.Generic;
using System.Linq;
using Decentralized.Domain;

// 纯逻辑单元测试（不需要游戏）。每个 Test 是一个场景，失败时打印原因，最后返回非零退出码。
// 涉及数值的检查以离线经济模拟的结果为对照。
internal static class Program
{
    private static int _failed, _passed;

    private static double BlackMarketTestRug() => Tuning.BmRugBase * 3;

    private static void Check(bool ok, string what)
    {
        if (ok) _passed++;
        else { _failed++; Console.WriteLine("  ✗ " + what); }
    }

    private static void Test(string name, Action body)
    {
        Console.WriteLine("• " + name);
        try { body(); }
        catch (Exception ex) { _failed++; Console.WriteLine("  ✗ 异常: " + ex); }
    }

    private static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

    /// <summary>生成 TuningTable.cs：占位符取值走查表而不是运行时反射（游戏的模组扫描会把反射算作未声明的权限）。</summary>
    private static string GenerateTuningTable()
    {
        var fields = typeof(Tuning).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
        var sb = new System.Text.StringBuilder();
        sb.Append("// 由测试项目生成：dotnet run --project tests/Decentralized.Domain.Tests -c Release -- --gen-tuning-table\n");
        sb.Append("// Tuning 增删数值后要重新生成（单元测试会核对）。\n");
        sb.Append("using System;\nusing System.Collections.Generic;\n\nnamespace Decentralized.Domain\n{\n");
        sb.Append("    internal static class TuningTable\n    {\n");
        sb.Append("        public static readonly Dictionary<string, double> Scalars = new Dictionary<string, double>\n        {\n");
        foreach (var f in fields.Where(f => !f.FieldType.IsArray)) sb.Append($"            [\"{f.Name}\"] = Tuning.{f.Name},\n");
        sb.Append("        };\n\n        public static readonly Dictionary<string, double[]> Arrays = new Dictionary<string, double[]>\n        {\n");
        foreach (var f in fields.Where(f => f.FieldType.IsArray)) sb.Append($"            [\"{f.Name}\"] = Array.ConvertAll(Tuning.{f.Name}, v => (double)v),\n");
        sb.Append("        };\n    }\n}\n");
        return sb.ToString();
    }

    private static string FindRoot() => System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    private static int Main(string[] args)
    {
        if (args.Contains("--gen-tuning-table"))
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(FindRoot(), "src/Decentralized.Domain/TuningTable.cs"), GenerateTuningTable().Replace("\r", ""));
            Console.WriteLine("TuningTable.cs 已生成");
            return 0;
        }
        Test("占位符查表（TuningTable）和 Tuning 一致", () =>
        {
            var path = System.IO.Path.Combine(FindRoot(), "src/Decentralized.Domain/TuningTable.cs");
            Check(System.IO.File.ReadAllText(path).Replace("\r", "") == GenerateTuningTable().Replace("\r", ""), "TuningTable.cs 已过期，重新生成：dotnet run --project tests/Decentralized.Domain.Tests -c Release -- --gen-tuning-table");
            var src = System.IO.File.ReadAllText(System.IO.Path.Combine(FindRoot(), "src/Decentralized.Domain/TuningText.cs"));
            Check(!src.Contains("System.Reflection") && !src.Contains("GetField"), "TuningText 不用反射");
        });
        Test("随机数：同种子同序列；状态可复制后继续", () =>
        {
            var a = new Rng(42);
            var b = new Rng(42);
            Check(Enumerable.Range(0, 100).All(_ => a.NextUInt() == b.NextUInt()), "同种子序列相同");
            var c = new Rng { S0 = a.S0, S1 = a.S1, S2 = a.S2, S3 = a.S3 };
            Check(Enumerable.Range(0, 100).All(_ => a.NextDouble() == c.NextDouble()), "复制状态后序列相同（存档读档）");
            var r = new Rng(7);
            double mean = Enumerable.Range(0, 20000).Select(_ => r.NextDouble()).Average();
            Check(Near(mean, 0.5, 0.01), $"均匀分布均值约 0.5：{mean:0.000}");
            double g = Enumerable.Range(0, 20000).Select(_ => r.Gauss()).Select(x => x * x).Average();
            Check(Near(g, 1.0, 0.05), $"正态分布方差约 1：{g:0.000}");
        });

        Test("资产登记：Ponero 不可追踪、不上合规交易所；稳定币挂钩", () =>
        {
            Check(!Assets.Get(Assets.Ponero).Traceable && !Assets.Get(Assets.Ponero).Compliant, "Ponero");
            Check(Assets.Get(Assets.CRC).Stable && Assets.Get(Assets.CRC).Freezable, "CRC 可冻结");
            Check(Assets.Get(Assets.CRA).Stable && !Assets.Get(Assets.CRA).Freezable, "CRA 不可冻结");
            Assets.Register(new AssetSpec { Id = "NPC_TEST", Issued = true });
            Check(Assets.Get("NPC_TEST")?.Issued == true, "新增币种只是登记一条");
        });

        Test("交易对：恒定乘积、手续费、500 信用点约 2% 价格冲击", () =>
        {
            var pool = Pool.Create(Venue.Dex, Assets.PSC, Assets.CRC, 100, 50000, Tuning.DexFee);
            Check(Near(pool.Price, 100, 1e-9), "初始价格 100");
            double k = pool.BaseReserve * pool.QuoteReserve;
            Check(Near(pool.ImpactOfBuy(500), 0.02, 0.001), $"500 信用点买入的冲击约 2%：{pool.ImpactOfBuy(500):P2}");
            double got = pool.BuyBase(500);
            Check(got > 4.9 && got < 4.97, $"500 信用点买到约 4.93 PSC：{got:0.000}");
            Check(pool.BaseReserve * pool.QuoteReserve >= k, "手续费留在池里，k 不减少");
            double back = pool.SellBase(got);
            Check(back < 500 && back > 496, $"马上卖回只损失手续费：{back:0.00}");
        });

        Test("交易对：夜间向目标价回归 15%（对数空间）并重设深度", () =>
        {
            var pool = Pool.Create(Venue.Dex, Assets.PSC, Assets.CRC, 80, 50000, Tuning.DexFee);
            pool.Revert(100, Tuning.Revert, 60000);
            double expect = Math.Exp(Math.Log(80) + 0.15 * (Math.Log(100) - Math.Log(80)));
            Check(Near(pool.Price, expect, 1e-9), $"回归后价格 {pool.Price:0.00}，期望 {expect:0.00}");
            Check(Near(pool.QuoteReserve, 60000, 1e-9), "深度重设");
        });

        Test("地址：每种币记脏枚数，转出、花掉、兑换按这种币的脏比例带走；Ponero 不带污点", () =>
        {
            Func<string, double> px = a => a == Assets.PSC ? 100 : 1;
            var a = new Wallet { Address = "A" };
            a.AddDirty(Assets.CRT, 1000, 300);
            var b = new Wallet { Address = "B" };
            Wallet.Transfer(a, b, Assets.CRT, 250);
            Check(Near(b.DirtyOf(Assets.CRT), 75, 1e-9) && Near(a.DirtyOf(Assets.CRT), 225, 1e-9), $"转出 1/4，脏枚数带走 1/4：{b.DirtyOf(Assets.CRT)}");
            a.Add(Assets.PSC, 5);
            a.Add(Assets.CRT, -150);
            Check(Near(a.DirtyOf(Assets.CRT), 180, 1e-9) && a.DirtyOf(Assets.PSC) == 0 && Near(a.Taint(px), 180, 1e-9), "花掉 150 CRT：按 30% 带走 45 枚脏的；干净的 PSC 不受影响；按现价的脏币价值 180");
            a.Convert(Assets.CRT, 100, Assets.PSC, 1);
            Check(Near(a.DirtyOf(Assets.PSC), 0.3, 1e-9) && Near(a.DirtyOf(Assets.CRT), 150, 1e-9), "兑换：30% 脏的 CRT 换来的 PSC 也是 30% 脏");
            a.Convert(Assets.CRT, 100, Assets.Ponero, 50, clean: true);
            Check(a.DirtyOf(Assets.Ponero) == 0 && Near(a.DirtyOf(Assets.CRT), 120, 1e-9), "兑入 Ponero：兑掉的脏币消失，Ponero 是干净的");
            var sc = new Wallet(); sc.AddDirty(Assets.CRC, 1000, 400); sc.Scale(Assets.CRC, 0.5);
            Check(Near(sc.Get(Assets.CRC), 500, 1e-9) && Near(sc.DirtyOf(Assets.CRC), 200, 1e-9), "CRC 减记：脏枚数同比例缩水");
            Check(Near(sc.MarkDirty(Assets.CRC, 400), 100, 1e-9) && Near(sc.DirtyOf(Assets.CRC), 500, 1e-9), "补记脏币不超过干净的枚数，返回没记上的");
            var c = new Wallet { Address = "C" };
            b.Add(Assets.Ponero, 10);
            Wallet.Transfer(b, c, Assets.Ponero, 10);
            Check(c.Taint(px) == 0, "Ponero 转入的地址不带污点");
            bool threw = false;
            try { a.Add(Assets.CRT, -10000); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "余额不足时拒绝");
        });

        Test("挖矿：开局 1 H 每晚约 26.9 信用点；减半在第 90 天", () =>
        {
            var net = new Network();
            double perH = net.Mine(1) * Tuning.PscStartPrice;
            Check(Near(perH, 26.9, 0.2), $"1 H 每晚约 26.9 信用点（抽成后）：{perH:0.00}");
            for (int d = 1; d < Tuning.HalvingDays; d++) net.Night(d, 100, 0);
            Check(net.Reward == Tuning.BlockReward, "减半日前一天还没减半");
            net.Night(Tuning.HalvingDays, 100, 0);
            Check(net.Reward == Tuning.BlockReward / 2, "减半日减半");
        });

        Test("挖矿：NPC 算力在有利可图时扩张，币价低时关机", () =>
        {
            var up = new Network();
            up.Night(1, 100, 0);
            Check(up.NpcHash > Tuning.NpcHash0, $"币价 100：扩张到 {up.NpcHash:0.0}");
            var down = new Network();
            down.Night(1, 30, 0);
            Check(down.NpcHash < Tuning.NpcHash0 && down.NpcHash >= Tuning.NpcHash0 * 0.95, $"币价 30：关机，最多 −5%：{down.NpcHash:0.0}");
        });

        Test("显卡：型号、显示单位、健康度与算力衰减", () =>
        {
            Check(Hardware.Specs["RTX70"].DisplayHash == 50 && Hardware.Specs["RX80"].DisplayHash == 40, "RTX70 50 MH/s，RX80 40 MH/s");
            Check(Near(Hardware.Specs["M3"].DisplayHash, 3, 1e-9), "RatDigg M3 3 GH/s");
            var u = new Unit { Model = "RTX70" };
            Check(u.Hash(ModuleBonus.None) == 5, "满健康 5 H");
            u.Health = 25;
            Check(Near(u.Hash(ModuleBonus.None), 4.0, 1e-9), "健康 25：5 × (0.6 + 0.4 × 0.5) = 4 H");
            u.Health = 0;
            Check(Near(u.Hash(ModuleBonus.None), 3.0, 1e-9), "健康 0：60%");
            Check(Near(new Unit { Model = "RTX70" }.Hash(ModuleBonus.From(36, -44, 0)), 5 * 1.09, 1e-9), "一个超频模块（+36 性能）：算力 +9%");
            var fake = new Unit { Model = "RTX90", FakeOf = "RTX60" };
            Check(fake.Hash(ModuleBonus.None) == Hardware.Specs["RTX60"].Hash && fake.Label.Model == "RTX90", "假卡标着 RTX90，算力按 RTX60");
            Check(Near(fake.DeathChance(ModuleBonus.None), new Unit { Model = "RTX90" }.DeathChance(ModuleBonus.None) * 2, 1e-12), "假卡暴毙率 ×2");
            var r = new Unit { Model = "RTX70", Health = 50 };
            Check(r.Refurbish() && r.Health == 80 && !r.Refurbish(), "翻新一次 +30，不能再翻新");
        });

        Test("显卡外观：5 个维度 243 种；真卡没有破绽类取值，假卡至少一处；老化随健康度；价值函数统一", () =>
        {
            var rng = new Rng(99);
            int n = 6000;
            int fakeNoSign = 0, realSign = 0, newWorn = 0, oldWorn = 0, fakeSigns = 0;
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < n; i++)
            {
                var real = CardLook.Generate(rng, 100, false);
                var oldReal = CardLook.Generate(rng, 20 + (i % 40), false);
                var fake = CardLook.Generate(rng, 20 + (i % 80), true);
                if (real.HasFakeSign || oldReal.HasFakeSign) realSign++;
                if (!fake.HasFakeSign) fakeNoSign++;
                fakeSigns += (fake.Print != 0 ? 1 : 0) + (fake.Serial != 0 ? 1 : 0);
                if (real.Fan > 0 || real.Fingers > 0) newWorn++;
                if (oldReal.Fan > 0 || oldReal.Fingers > 0) oldWorn++;
                seen.Add($"{fake.Fan}{fake.Fingers}{fake.Bracket}{fake.Print}{fake.Sticker}");
                seen.Add($"{oldReal.Fan}{oldReal.Fingers}{oldReal.Bracket}{oldReal.Print}{oldReal.Sticker}");
            }
            Check(realSign == 0, "真卡从不出现破绽类取值（丝印不对、序列号不合规）");
            Check(oldWorn > newWorn * 1.8, $"低健康度的真卡明显更旧：{oldWorn / (double)n:P0} 对 {newWorn / (double)n:P0}");
            Check(seen.Count >= 60, $"抽样见到 {seen.Count} 种不同组合（上限 243）");
            Check(Hardware.Specs["RTX60"].Price == 220 && Hardware.Specs["RTX90"].Price == 5000 && Hardware.Specs["RX900"].Price == 4000, "价格已抬高到约 1.8 倍");
            var u = new Unit { Model = "RTX90", FakeOf = "RTX60", Health = 80 };
            Check(CardValue.True(u) == CardValue.Of("RTX60", 80) && CardValue.Of("RTX90", 80) > CardValue.True(u) * 8, "真实价值按芯片型号，标称型号的价远高于真值");
            var worn = new Unit { Model = "RTX70", Health = 100, Look = new CardLook { Fan = 2, Fingers = 2, Bracket = 1, Sticker = 2 } };
            Check(CardValue.True(worn) == (long)Math.Round(800 * (1 - 0.12 - 0.12 - 0.02 - 0.06)), "各标签折扣相加进真实价值：风扇缺角 + 金手指氧化 + 划痕 + 封条缺失");
            var clean = new Unit { Model = "RTX70", Health = 80 };
            var dusty = new Unit { Model = "RTX70", Health = 80, Look = new CardLook { Fan = 2, Fingers = 2 } };
            Check(dusty.DeathChance(ModuleBonus.None) > clean.DeathChance(ModuleBonus.None) * 1.5 && Near(CardLook.Decode("20000").WearFactor, 1.3, 1e-9), "风扇缺角、金手指氧化抬高暴毙概率与损耗");
            Check(CardValue.Of("RTX70", 100) == 800 && CardValue.Of("RTX70", 0) == 200 && CardValue.Of("RTX70", 50) == 500, "健康 100 满价、0 剩 25%、50 为 62.5%");
        });

        Test("矿架槽位：默认 2 个，投入电子元件逐个升级到最多 5 个；只收下一级需要的；形状随槽数变化", () =>
        {
            int slots = RigSlots.Base, prog = 0;
            Check(slots == 2 && RigSlots.Next(2) == ("common_electronic", 4), "默认 2 槽，升到第 3 槽要 4 个普通电子元件");
            Check(RigSlots.Insert(ref slots, ref prog, "rare_electronic", 1) == 0 && slots == 2 && prog == 0, "第 3 槽不收稀有元件");
            Check(RigSlots.Insert(ref slots, ref prog, "common_electronic", 3) == 3 && slots == 2 && prog == 3, "先投 3 个：进度 3/4");
            Check(RigSlots.Insert(ref slots, ref prog, "common_electronic", 3) == 1 && slots == 3 && prog == 0, "再投 3 个只收 1 个，解锁第 3 槽，多的 2 个退回");
            Check(RigSlots.Next(3) == ("rare_electronic", 1) && RigSlots.Insert(ref slots, ref prog, "rare_electronic", 5) == 1 && slots == 4, "第 4 槽：1 个稀有元件");
            Check(RigSlots.Next(4) == ("rare_electronic", 2) && RigSlots.Insert(ref slots, ref prog, "rare_electronic", 1) == 1 && slots == 4 && prog == 1, "第 5 槽：2 个稀有元件，先投 1 个进度 1/2");
            Check(RigSlots.Insert(ref slots, ref prog, "rare_electronic", 1) == 1 && slots == 5, "投满解锁第 5 槽");
            Check(RigSlots.Next(5) == null && RigSlots.Insert(ref slots, ref prog, "rare_electronic", 1) == 0, "满 5 槽后不再收");
            Check(RigSlots.Shape(2) == "000000" + "000000" + new string('1', 18) && RigSlots.Shape(5) == new string('0', 30) && RigSlots.Shape(3).Length == 30, "形状：宽 3、共 10 行，前 2 × 槽数行可放");
        });

        Test("模块：折算系数 4，不设上限，只有防出错的下限；性能加成提高损耗与暴毙", () =>
        {
            var perf = ModuleBonus.From(16, 0, 0);
            Check(Near(perf.Hash, 1.04, 1e-9) && Near(perf.Wear, 1.04, 1e-9) && Near(perf.Death, 1.04, 1e-9), "一个性能模块（+16）：算力 +4%，损耗和暴毙 +4%");
            var cores = ModuleBonus.From(200 * 3, -100 * 3, 100 * 3);
            Check(Near(cores.Hash, 2.5, 1e-9) && Near(cores.Death, 2.5, 1e-9), $"三个无限制神经核心：算力 ×2.5，暴毙率同样 ×2.5：{cores.Hash:0.00}");
            Check(Near(cores.Kj, 1.75, 1e-9), $"神经核心的负效率照常增加耗电：×{cores.Kj:0.00}");
            Check(Near(cores.Wear, 0.25 * 2.5, 1e-9), "品质 +300 → 损耗 −75%，再乘性能带来的发热");
            var nine = ModuleBonus.From(200 * 9, -100 * 9, 100 * 9);
            Check(Near(nine.Hash, 5.5, 1e-9) && Near(nine.Kj, 3.25, 1e-9) && nine.Wear == Tuning.ModuleWearFloor, "九个核心：算力 ×5.5、耗电 ×3.25，损耗降到下限");
            var eco = ModuleBonus.From(-16 * 9, 32 * 9, 0);
            Check(Near(eco.Kj, 1 - 32 * 9 / 400.0, 1e-9) && Near(eco.Hash, 1 - 16 * 9 / 400.0, 1e-9), "九个节能模块：省电 72%，负性能照常降低算力");
            Check(ModuleBonus.From(0, 400 * 2, 0).Kj == Tuning.ModuleKjFloor && ModuleBonus.From(-400 * 2, 0, 0).Hash == Tuning.ModuleHashFloor, "耗电、算力不低于下限");
            var none = ModuleBonus.From(0, 0, 0);
            Check(none.Hash == 1 && none.Kj == 1 && none.Wear == 1 && none.Death == 1, "没有模块时不变");
        });

        Test("显卡：累计暴毙概率与模拟器一致（第 50 / 100 / 150 天）", () =>
        {
            double[] Run(string model, ModuleBonus b, int n)
            {
                var rng = new Rng(2026);
                var deadBy = new int[3];
                for (int i = 0; i < n; i++)
                {
                    var u = new Unit { Model = model };
                    for (int d = 1; d <= 150; d++)
                    {
                        u.Night(b, rng);
                        if (u.Dead)
                        {
                            if (d <= 50) deadBy[0]++;
                            if (d <= 100) deadBy[1]++;
                            deadBy[2]++;
                            break;
                        }
                    }
                }
                return deadBy.Select(x => (double)x / n).ToArray();
            }
            var p = Run("RTX70", ModuleBonus.None, 20000);
            Check(Near(p[0], 0.11, 0.02) && Near(p[1], 0.53, 0.02) && Near(p[2], 0.84, 0.02),
                $"Probidia 11% / 53% / 84%：{p[0]:P0} / {p[1]:P0} / {p[2]:P0}");
            var q = Run("RX80", ModuleBonus.None, 20000);
            Check(Near(q[0], 0.20, 0.02) && Near(q[1], 0.76, 0.02) && Near(q[2], 0.95, 0.02),
                $"PMD 20% / 76% / 95%：{q[0]:P0} / {q[1]:P0} / {q[2]:P0}");
        });

        Test("市场：价格锚受停电、镇压影响；夜间向锚回归并限制在 0.3–3 倍", () =>
        {
            var m = Market.CreateDefault(1);
            var none = new MarketEvent[0];
            Check(Near(m.Anchor(none), 100, 1e-9) && Near(m.Anchor(new[] { MarketEvent.Blackout }), 110, 1e-9)
                  && Near(m.Anchor(new[] { MarketEvent.Crackdown }), 92, 1e-9), "锚：基准 100，停电 ×1.1，镇压 ×0.92");
            Check(Market.Volatility(new[] { MarketEvent.GangWar }) == Tuning.PscVol * 2, "火并时波动 ×2");
            m.Main.SetPrice(60, Tuning.PoolDepth(1));
            var rng = new Rng(5);
            double sum = 0;
            for (int i = 0; i < 400; i++)
            {
                var mm = Market.CreateDefault(1);
                mm.Main.SetPrice(60, Tuning.PoolDepth(1));
                mm.Night(none, new Rng((ulong)i), 1, a => 1);
                sum += Math.Log(mm.Price);
            }
            double expect = Math.Log(60) + 0.15 * (Math.Log(100) - Math.Log(60));
            Check(Near(sum / 400, expect, 0.01), $"从 60 起一晚平均回到 {Math.Exp(expect):0.0} 附近：{Math.Exp(sum / 400):0.0}");
            var hi = Market.CreateDefault(1);
            hi.Main.SetPrice(1000, Tuning.PoolDepth(1));
            hi.Night(none, rng, 1, a => 1);
            Check(hi.Price <= hi.Anchor(none) * Tuning.PriceCeiling + 1e-9, $"上限是中枢的 3 倍：{hi.Price:0.0}");
            var late = Market.CreateDefault(1);
            late.Supply = 5;
            late.Main.SetPrice(1000, Tuning.PoolDepth(1));
            late.Night(none, rng, 1, a => 1);
            Check(late.Price > 300, $"多次减半后币价不被固定价封住：{late.Price:0.0}");
        });

        Test("钱包：买入均价按买入记录算，卖出和转出按比例减少，挖出的币不计入", () =>
        {
            var w = new Wallet { Address = "x" };
            w.Add("PSC", 10);                       // 挖出的 10 个，无买入记录
            Check(w.AvgBuy("PSC") == null, "没有买入记录时没有均价");
            w.Add("PSC", 5); w.RecordBuy("PSC", 5, 400);
            w.Add("PSC", 5); w.RecordBuy("PSC", 5, 600);
            Check(Near(w.AvgBuy("PSC").Value, 100, 1e-9), "买 5 个花 400 + 买 5 个花 600：均价 100");
            w.Add("PSC", -10);                      // 卖掉一半（20 个里的 10 个）
            Check(Near(w.AvgBuy("PSC").Value, 100, 1e-9) && Near(w.BuyQty["PSC"], 5, 1e-9), "卖出后均价不变，买入记录按比例减少");
            w.Add("PSC", -10);
            Check(w.AvgBuy("PSC") == null, "清仓后记录清除");
        });

        Test("市场：减半抬高价格锚（随机幅度，逐日靠拢），减半前一周有预期行情", () =>
        {
            var m = new Market();
            Check(Near(m.SupplyTarget(10), 1.0, 1e-9), "减半前很远：目标因子 1");
            double before = m.SupplyTarget(Tuning.HalvingDays - 1), start = m.SupplyTarget(Tuning.HalvingDays - 7);
            Check(start > 1.0 && before > start && before <= 1 + Tuning.HalvingRamp + 1e-9, $"减半前 7 天逐日抬高，最多 +8%：{start:0.000} → {before:0.000}");
            double lo = 1e9, hi = 0;
            for (int seed = 1; seed <= 200; seed++)
            {
                var x = new Market(); x.OnHalving(new Rng((ulong)seed));
                lo = Math.Min(lo, x.HalvingLog); hi = Math.Max(hi, x.HalvingLog);
            }
            Check(lo >= Tuning.HalvingPassMin && hi <= Tuning.HalvingPassMax && hi - lo > 0.5, $"传导指数在区间内且确有变化：{lo:0.00}–{hi:0.00}");
            m.OnHalving(new Rng(7UL));
            double after = m.SupplyTarget(Tuning.HalvingDays + 1);
            Check(Near(after, Math.Pow(2, m.HalvingLog), 1e-9), $"减半后目标因子 2^{m.HalvingLog:0.00} = {after:0.000}");
            var n = new Market { HalvingLog = m.HalvingLog };
            for (int i = 0; i < 7; i++) n.UpdateSupply(91 + i);
            Check(n.Supply > 1.0 && n.Supply < after, "逐晚靠拢目标，不一次跳到位");
            for (int i = 0; i < 40; i++) n.UpdateSupply(100 + i % 5);
            Check(Near(n.Supply, after, 0.02), "几十晚后基本到位");
            Check(n.Anchor(new MarketEvent[0]) > Tuning.PscStartPrice * 1.05, "锚随之上移");
        });

        Test("市场：其他交易对每晚被套利拉向参考价，价差收敛", () =>
        {
            var m = Market.CreateDefault(1);
            var dexPsc = m.DexPool(Assets.PSC);
            dexPsc.SetPrice(130, dexPsc.QuoteReserve);
            for (int d = 1; d <= 5; d++) m.Night(new MarketEvent[0], new Rng((ulong)d), 1, a => 1);
            double gap = Math.Abs(dexPsc.Price / m.Price - 1);
            Check(gap < 0.03, $"五晚后 DEX 的 PSC/CRC 与参考价相差 {gap:P1}");
            var crtCrc = m.DexPool(Assets.CRT);
            m.Night(new MarketEvent[0], new Rng(9), 1, a => a == Assets.CRT ? 0.9 : 1);
            Check(crtCrc.Price < 0.98, $"CRT 脱锚时 CRT/CRC 向 0.9 靠拢：{crtCrc.Price:0.000}");
            var m2 = Market.CreateDefault(1);
            var spark = m2.DexPool(Assets.SPARK);
            m2.Night(new MarketEvent[0], new Rng(11), 1, a => a == Assets.SPARK ? 20 : 1);
            Check(spark.Price > Tuning.SparkStart * 1.3, $"SPARK/CRC 向 SPARK 价格靠拢：{spark.Price:0.00}");
        });

        Test("市场：任意两币互换经 CRC 路由，两跳付两次手续费", () =>
        {
            var m = Market.CreateDefault(1);
            var oneHop = m.Quote(Assets.CRC, Assets.PSC, 100);
            var twoHop = m.Quote(Assets.CRA, Assets.PSC, 100);
            Check(oneHop != null && oneHop.Hops == 1 && twoHop != null && twoHop.Hops == 2, "CRC 在一端走一个池，否则两个池");
            Check(m.Quote(Assets.PSC, Assets.PSC, 1) == null && m.Quote(Assets.CRC, Assets.CRC, 1) == null && m.Quote(Assets.CRC, Assets.Ponero, 1) == null, "同币、没有池没有路由");
            Check(twoHop.Impact > oneHop.Impact && twoHop.Impact > Tuning.DexFee + Tuning.StableFee - 1e-9, $"两跳损失更大（稳定币池一跳 + PSC 池一跳）：{oneHop.Impact:P2} 对 {twoHop.Impact:P2}");
            double expect = m.DexPool(Assets.PSC).BaseOut(m.DexPool(Assets.CRA).QuoteOut(100));
            Check(Near(twoHop.Out, expect, 1e-9), "两跳的数量 = 先卖成 CRC 再买");
            double got = m.Swap(Assets.CRA, Assets.PSC, 100);
            Check(Near(got, twoHop.Out, 1e-9), "成交数量与报价一致");
            Check(m.DexPool(Assets.CRA).Price < 1 && m.DexPool(Assets.PSC).Price > Tuning.PscStartPrice, "成交推动两个池的价格");
            var big = Market.CreateDefault(1).Quote(Assets.CRC, Assets.SPARK, Tuning.PoolDepth(1) * Tuning.SparkDepthShare / 4);
            Check(big.Impact > 0.2, $"SPARK 池比较薄，大单滑点大：{big.Impact:P0}");
        });

        Test("稳定币：CRC 脱锚与减记，CRT 脱锚与冻结概率", () =>
        {
            int depegs = 0, haircuts = 0;
            for (int i = 0; i < 2000; i++)
            {
                var s = new Stablecoins();
                var rng = new Rng((ulong)i);
                s.OnEventStart(MarketEvent.RevRaidUpper, 10, rng);
                if (s.Crc < 1)
                {
                    depegs++;
                    Check(s.Crc >= 0.88 && s.Crc <= 0.93 && s.CrcDepegEnd >= 12 && s.CrcDepegEnd <= 13, "CRC 脱锚深度与持续");
                    double f = 1;
                    for (int d = 10; d <= s.CrcDepegEnd; d++) f *= s.Night(d, rng, 0);
                    if (f < 1) haircuts++;
                    Check(s.Crc == 1, "到期恢复");
                }
            }
            Check(Near(depegs / 2000.0, 0.30, 0.03), $"革命军袭击上层时 30% 脱锚：{depegs / 2000.0:P0}");
            Check(Near(haircuts / (double)depegs, 0.15, 0.04), $"其中 15% 减记：{haircuts / (double)depegs:P0}");
            Check(Near(Stablecoins.CrtFreezeChance(0), 0.40, 1e-9) && Stablecoins.CrtFreezeChance(50) == 0 && Near(Stablecoins.CrtFreezeChance(25), 0.2, 1e-9),
                "CRT 冻结概率：声望 0 为 40%，25 为 20%，50 为 0");
        });

        Test("稳定币：CRA 垫子告急即为预兆；储备吃空必崩，螺旋三天 0.85 / 0.30 / 0.02", () =>
        {
            var s = new Stablecoins { Reserve = Tuning.CraCrashRatio * Tuning.CraS0 + Stablecoins.Cushion0 * 0.19 };
            Check(s.CraWarning(1) && s.CraRate == Tuning.CraRateLow, "垫子低于 20%：预兆出现、利率降到 0.2%");
            var t = new Stablecoins { Reserve = Tuning.CraS0 * 0.2 };
            var rng = new Rng(3);
            t.Night(1, rng, 100000);
            Check(t.CraWarnDay == 1 && t.CraEventDay >= 5 && t.CraEventDay <= 8, $"垫子吃空触发预兆，提前 4–7 天：第 {t.CraEventDay} 天挪用");
            int ev = t.CraEventDay;
            var path = new List<double>();
            for (int d = 2; d <= ev + 3; d++) { t.Night(d, rng, 0); if (d >= ev) path.Add(t.Cra); }
            Check(t.CraCrashDay == ev && path.Take(3).SequenceEqual(new[] { 0.85, 0.30, 0.02 }), $"崩盘路径：{string.Join(" / ", path)}");
            Check(!t.CraWarning(ev + 1), "崩盘开始后不再算预兆");
        });

        Test("稳定币：挪用后 R/S 低于崩盘线才崩，否则只是折价；劫掠上层补回储备", () =>
        {
            int crashes = 0, discounts = 0;
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var s = new Stablecoins { Supply = 100000, Reserve = 100000, C = 0.8, CraWarnDay = 1, CraEventDay = 2 };
                s.Night(2, new Rng(seed), 0);
                double r = s.Ratio;
                Check(r >= 1 - Tuning.CraEmbezzleMax - 0.01 && r <= 1 - Tuning.CraEmbezzleMin + 0.01 && s.CraEventDay < 0 && s.CraWarnDay < 0, $"储备被拿走 40%–80%：R/S {r:0.00}");
                if (r < Tuning.CraCrashRatio) { crashes++; Check(s.CraCrashDay == 2 && s.Cra == Tuning.CraSpiralPath[0], "R/S 跌破崩盘线：当天进入螺旋"); }
                else { discounts++; Check(s.CraCrashDay < 0 && Near(s.Cra, Stablecoins.PriceOfRatio(r), 1e-9), $"折价：{s.Cra:0.000}"); }
            }
            Check(crashes > 5 && discounts > 5, $"两种结果都会出现：崩盘 {crashes}、折价 {discounts}");
            var t = new Stablecoins(); double before = t.Reserve;
            t.OnEventStart(MarketEvent.RevRaidUpper, 3, new Rng(1));
            Check(Near(t.Reserve, before + Tuning.CraLoot * t.Supply, 1e-6), "劫掠上层把流通量 × 9% 的 CRC 并入储备");
            Check(Stablecoins.PriceOfRatio(Tuning.CraSafeRatio) == 1 && Near(Stablecoins.PriceOfRatio(Tuning.CraCrashRatio), 1 - Tuning.CraDiscountMax, 1e-9), "价格曲线：安全线 1，崩盘线折价 15%");
        });

        Test("浏览器账本：最新在前、按地址过滤、超过上限丢最旧", () =>
        {
            var l = new Ledger();
            string a = ChainAddress.Make(1), b = ChainAddress.Make(2), c = ChainAddress.Make(3);
            l.Add(new LedgerEntry { Block = 1, From = a, To = b, Asset = "PSC", Amount = 1 });
            l.Add(new LedgerEntry { Block = 2, From = b, To = c, Asset = "CRC", Amount = 5, Fee = 1 });
            l.Add(new LedgerEntry { Block = 3, From = c, To = a, Asset = "PSC", Amount = -1 });
            l.Add(new LedgerEntry { Block = 4, From = c, To = a, Asset = "PSC", Amount = 0 });
            Check(l.Entries.Count == 3 && l.Entries[0].Block == 4 && l.Entries[1].Block == 2, "负数不记，0 币转入（地址投毒）照记；最新的在前");
            Check(l.Of(b, 10).Count == 2 && l.Of(a, 10).Count == 2 && l.Of(a.ToUpperInvariant(), 10).Count == 2 && l.Of("", 10).Count == 3, "按地址过滤，大小写不敏感，空地址是最近的全部");
            for (int i = 0; i < Tuning.LedgerMax + 30; i++) l.Add(new LedgerEntry { Block = 100 + i, From = a, To = b, Asset = "PSC", Amount = 1 });
            Check(l.Entries.Count == Tuning.LedgerMax && l.Entries[0].Block == 100 + Tuning.LedgerMax + 29 && l.Of("", 5).Count == 5, "上限 200 条");
            var back = Newtonsoft.Json.JsonConvert.DeserializeObject<Ledger>(Newtonsoft.Json.JsonConvert.SerializeObject(l));
            Check(back.Entries.Count == l.Entries.Count && back.Entries[0].Block == l.Entries[0].Block, "存档往返");
        });

        Test("CRA 长期行为：200 天里有的局崩盘有的不崩，质押越多崩得越多，崩盘前有预兆", () =>
        {
            Func<double, (double crash, double lead)> run = staked =>
            {
                int crashes = 0, n = 300; double leadSum = 0; int leads = 0;
                for (int i = 0; i < n; i++)
                {
                    var s = new Stablecoins(); var rng = new Rng((ulong)(i + 1));
                    int warn = -1;
                    for (int d = 1; d <= 200 && s.CraCrashDay < 0; d++)
                    {
                        if (rng.Chance(0.03)) s.OnEventStart(MarketEvent.RevRaidUpper, d, rng);
                        s.Night(d, rng, staked);
                        if (warn < 0 && s.CraWarnDay >= 0) warn = s.CraWarnDay;
                        if (s.CraWarnDay < 0 && s.CraCrashDay < 0) warn = -1;
                    }
                    if (s.CraCrashDay >= 0) { crashes++; if (warn >= 0) { leadSum += s.CraCrashDay - warn; leads++; } }
                }
                return (crashes / (double)n, leads > 0 ? leadSum / leads : 0);
            };
            var lo = run(0); var hi = run(100000);
            Check(lo.crash > 0.02 && lo.crash < 0.9, $"无质押 200 天崩盘比例 {lo.crash:P0}");
            Check(hi.crash >= lo.crash, $"质押 10 万：{hi.crash:P0} ≥ 无质押 {lo.crash:P0}");
            Check(lo.lead >= Tuning.CraWarnLeadMin - 0.01 && lo.lead <= Tuning.CraWarnLeadMax + 0.01, $"预兆到崩盘平均 {lo.lead:0.0} 天");
        });

        Test("CRA 一级市场：铸造按抵押率存 CRC 加 SPARK，排队后进流通量与储备；销毁按 min(c, R/S) 兑付", () =>
        {
            var s = new Stablecoins(); var rng = new Rng(2);
            var w = new Wallet { Address = "w" };
            s.Collateral(1000, out double crc, out double spark);
            Check(Near(crc, 1000 * Tuning.CraC0, 1e-9) && Near(spark * s.Spark, 1000 * (1 - Tuning.CraC0), 1e-9), $"抵押：CRC {crc:0}，SPARK {spark:0.##}");
            Check(s.RequestMint(w, 1000, 1, rng) == null, "钱包里没有币：拒绝");
            w.Add(Assets.CRC, crc); w.Add(Assets.SPARK, spark);
            double S0 = s.Supply, R0 = s.Reserve;
            var o = s.RequestMint(w, 1000, 1, rng);
            Check(o != null && w.Get(Assets.CRC) == 0 && w.Get(Assets.SPARK) == 0 && o.Due >= 2 && o.Due <= 3, "存入后排队 1～2 天");
            var evs = s.NightOrders(1, rng);
            Check(evs.Count == 0 && s.Supply == S0, "没到期不结算");
            evs = s.NightOrders(o.Due, rng);
            Check(evs.Count == 1 && evs[0].Kind == CraOrderKind.Minted && Near(s.Supply, S0 + 1000, 1e-9) && Near(s.Reserve, R0 + crc, 1e-9), "到期：流通量加 1000，储备加 CRC 部分");
            w.Add(Assets.CRA, 500);
            double ratio = s.Ratio;
            var b = s.RequestBurn(w, 500, 5);
            Check(b != null && b.Due == 5 + Tuning.CraUnlockDays && w.Get(Assets.CRA) == 0, "销毁排队解锁期");
            evs = s.NightOrders(b.Due, rng);
            Check(evs.Count == 1 && evs[0].Kind == CraOrderKind.Burned && Near(evs[0].Crc, 500 * Math.Min(Tuning.CraC0, ratio), 1e-6) && evs[0].Spark > 0, $"兑付 CRC {evs[0].Crc:0}、SPARK {evs[0].Spark:0.##}");
            var low = new Stablecoins { Supply = 1000, Reserve = 200 };
            low.Redeem(100, 0.8, out double c2, out double sp2);
            Check(Near(c2, 20, 1e-9) && Near(sp2 * low.Spark, 80, 1e-9), "储备只有 R/S = 0.2：CRC 份额降到 0.2，其余用 SPARK 补");
        });

        Test("CRA 一级市场：官方跑路概率 = 系数 × (1 − CRA 价格)，命中整笔丢失", () =>
        {
            var s = new Stablecoins();
            Check(s.RugChance == 0, "价格 1 时不会跑路");
            s.Cra = 0.9;
            Check(Near(s.RugChance, Tuning.CraRugPerDepeg * 0.1, 1e-9), "价格 0.9 时概率 10%");
            int lost = 0, n = 4000;
            for (int i = 0; i < n; i++)
            {
                var t = new Stablecoins { Cra = 0.9, Supply = 1000, Reserve = 800 };
                var w = new Wallet { Address = "w" }; w.Add(Assets.CRA, 10);
                t.RequestBurn(w, 10, 1);
                var ev = t.NightOrders(1, new Rng((ulong)i));
                if (ev.Count == 1 && ev[0].Kind == CraOrderKind.Lost) { lost++; Check(t.Orders.Count == 0 && w.Get(Assets.CRA) == 0, "丢失不退款"); }
            }
            Check(Math.Abs(lost / (double)n - 0.1) < 0.03, $"命中率 {lost / (double)n:P1}");
        });

        Test("Poinbase 只收干净的币：每种币最多充干净的枚数、脏的留在钱包；出金不审查、不收税", () =>
        {
            Func<string, double> px = a => 1;
            var w = new Wallet { Address = "w" };
            w.AddDirty(Assets.CRC, 1000, 500);
            Check(Near(w.CleanAvail(Assets.CRC), 500, 1e-9), "1000 CRC、500 枚脏：能充 500");
            var pb = new Wallet { Address = "pb" };
            double moved = Wallet.TransferClean(w, pb, Assets.CRC, 800);
            Check(Near(moved, 500, 1e-9) && Near(w.DirtyOf(Assets.CRC), 500, 1e-9) && Near(w.Get(Assets.CRC), 500, 1e-9) && pb.Taint(px) == 0,
                "要充 800 只转了干净的 500：脏的留在钱包（剩 500 CRC 全脏）");
            Check(w.CleanAvail(Assets.CRC) == 0, "剩下的全是脏的，一枚也充不进去");
            w.Add(Assets.CRC, 300);
            Check(Near(w.CleanAvail(Assets.CRC), 300, 1e-9), "转进干净的 300 只能再充 300");
            var two = new Wallet(); two.AddDirty(Assets.CRT, 100, 100); two.Add(Assets.PSC, 2);
            Check(Near(two.CleanAvail(Assets.PSC), 2, 1e-9) && two.CleanAvail(Assets.CRT) == 0, "按币种分开：CRT 全脏不影响干净的 PSC");
            var fz = new Wallet(); fz.AddDirty(Assets.CRT, 1000, 600); fz.Freeze(Assets.CRT, 0.5, 9);
            Check(Near(fz.CleanAvail(Assets.CRT), 400, 1e-9), "冻结的不能充：取可动用和干净的较小者");
            var acct = new Wallet();
            acct.Add(Assets.CRC, 1000);
            var r = new Exchange().Cashout(acct, 1000, px);
            Check(Near(r.Received, 1000 * (1 - Tuning.ExchangeFee), 1e-6), "出金全额到账（扣手续费）");
            var dep = new Wallet();
            dep.Add(Assets.CRC, 1000);
            Check(Near(new Exchange().Cashout(dep, 1000, a => 0.9).Received, 900 * (1 - Tuning.ExchangeFee), 1e-6), "CRC 脱锚时按市价兑出");
            // 存档迁移：旧的整钱包污点额按现价摊成各币脏枚数；Poinbase 和永续仓位的挪到 1 号钱包一起摊，超出的作废
            Func<string, double> px2 = a => a == Assets.PSC ? 100 : 1;
            var old = new Wallet { LegacyTaint = 300 }; old.Add(Assets.CRC, 1000);
            var pos = new List<PerpPosition> { new PerpPosition { Product = "PSC", LegacyTaint = 200 } };
            var w1 = new Wallet { Address = "w1", LegacyTaint = 100 }; w1.Add(Assets.CRC, 400); w1.Add(Assets.PSC, 2); w1.Add(Assets.Ponero, 50);
            var w2 = new Wallet { Address = "w2", LegacyTaint = 50 }; w2.Add(Assets.CRT, 100);
            var mixer = new Mixer(); mixer.Pending.Add(new MixDeposit { Asset = Assets.PSC, Amount = 3, LegacyTaint = 150 });
            var book = new PredictBook(); book.Positions.Add(new PredictPos { Cost = 100, LegacyTaint = 40 });
            double was = TaintMigration.Run(new List<Wallet> { w1, w2 }, old, pos, mixer, new BlackMarket(), new Staking(), new Scams(), book, new OwnMixer(), px2);
            Check(old.LegacyTaint == 0 && pos[0].LegacyTaint == 0 && w1.LegacyTaint == 0 && Near(w1.DirtyOf(Assets.CRC), 400, 1e-9) && Near(w1.DirtyOf(Assets.PSC), 2, 1e-9) && w1.DirtyOf(Assets.Ponero) == 0,
                "1 号钱包：自己 100 + Poinbase 300 + 永续 200 = 600，超过可追踪币的价值 600 封顶，全部记脏；Ponero 不摊");
            Check(Near(w2.DirtyOf(Assets.CRT), 50, 1e-9), "2 号钱包：污点 50 / 价值 100 → CRT 一半脏");
            Check(Near(mixer.Pending[0].Dirty, 1.5, 1e-9) && Near(book.Positions[0].Dirty, 40, 1e-9) && Near(was, 840, 1e-9), "托管：混币器在途按价格换成脏枚数，预测持仓同理；日志总额 840");
        });

        Test("二手贩子：老实人不报价；中间商加价；骗子夹带假卡；指出问题按维度扣价、拆穿假货按真实芯片算", () =>
        {
            var rng = new Rng(7);
            int fakes = 0, cards = 0;
            for (int i = 0; i < 300; i++)
                foreach (var c in UsedDealers.Cards(DealerKind.Honest, 40, rng))
                    Check(c.FakeOf == null, "老实人的卡是真卡");
            for (int i = 0; i < 300; i++)
                foreach (var c in UsedDealers.Cards(DealerKind.Scammer, 40, rng))
                {
                    cards++;
                    if (c.FakeOf == null) continue;
                    fakes++;
                    Check(Hardware.Specs[c.FakeOf].Price * 2 <= Hardware.Specs[c.Model].Price, "假卡的芯片至少便宜一半");
                }
            Check(fakes > cards * 0.2 && fakes < cards * 0.5, $"骗子的假卡占两成多（入门卡便宜到没法冒充）：{fakes}/{cards}");
            Check(UsedDealers.Pool(5).All(m => Dealer.Basic.Contains(m)) && UsedDealers.Pool(30).Contains("RTX90"), "型号按天数放开");
            // 标签：假 RTX 70（芯片 RTX 50）、健康 60、风扇缺角、金手指氧化、丝印印着真实型号
            var u = new Unit { Model = "RTX70", FakeOf = "RTX60", Health = 60, Look = new CardLook { Fan = 2, Fingers = 2, Print = 1 } };
            var claim = CardLabels.DealerClaim(u, DealerKind.Scammer, new Rng(3));
            Check(claim.Model == "RTX70" && claim.Fan == 0 && claim.Fingers == 0 && claim.Print == 0, "骗子的说法：标称型号，外观全是没问题");
            long hidden = CardLabels.Value(u, claim, 0), real = CardValue.True(u);
            Check(hidden == CardValue.Of("RTX70", 60) && real == (long)Math.Round(CardValue.Of("RTX60", 60) * 0.76), "没指出时按说法算（标称型号满价），真实价值按芯片和外观算");
            int ex = 0;
            Check(CardLabels.Point(u, claim, CardLabels.RowFan, ref ex) == 1 && CardLabels.Value(u, claim, ex) == (long)Math.Round(CardValue.Of("RTX70", 60) * 0.88), "指出风扇缺角：扣 12%");
            Check(CardLabels.Point(u, claim, CardLabels.RowPrint, ref ex) == 2 && (ex & CardLabels.RowSpec) != 0, "指出丝印印着真实型号：拆穿假货，型号标签一并拆穿");
            Check(CardLabels.Point(u, claim, CardLabels.RowFingers, ref ex) == 1 && CardLabels.Value(u, claim, ex) == real, "全部指出之后价格回到真实价值");
            int ex2 = 0;
            var honestUnit = new Unit { Model = "RTX70", Health = 60, Look = new CardLook { Fan = 1 } };
            Check(CardLabels.Point(honestUnit, CardLabels.DealerClaim(honestUnit, DealerKind.Honest, new Rng(1)), CardLabels.RowFan, ref ex2) == 0, "老实人说的是实话，指出去没用");
            Check(CardLabels.Lies(u, claim, 0) == (CardLabels.RowSpec | CardLabels.RowFan | CardLabels.RowFingers | CardLabels.RowPrint), "骗子隐瞒了型号、丝印、叶片、金手指");
            Check(CardLabels.Lies(u, claim, ex) == 0, "指出之后不再算隐瞒");
            Check(Claim.Decode(claim.Encode()).Encode() == claim.Encode(), "说法能存取");
        });

        Test("序列号与供电风扇：四档实物特征；序列号按牌子和档次鉴定；指出供电风扇自动改回真实型号", () =>
        {
            Check(Hardware.FanCount.SequenceEqual(new[] { 1, 2, 3, 3 }) && Hardware.PowerPins.SequenceEqual(new[] { "6", "8", "8", "8+8" }), "低端 6 针单风扇、中端 8 针双风扇、高端 8 针三风扇、旗舰双 8 针三风扇");
            Check(Hardware.Specs.Values.Where(x => !x.Asic && !x.Scrap).All(x => Hardware.Specs.Values.Count(y => !y.Asic && !y.Scrap && y.Brand == x.Brand && y.Tier == x.Tier) == 1), "每个牌子每个档次只有一个型号（废片不算）");
            var rngF = new Rng(99UL); bool lowFake = false, midScrap = false;
            for (int i = 0; i < 400; i++)
            {
                foreach (var c in UsedDealers.Cards(DealerKind.Scammer, 60, rngF))
                {
                    if (c.FakeOf == null) continue;
                    if (Hardware.Specs[c.Model].Tier == 0 && Hardware.Specs[c.FakeOf].Scrap) lowFake = true;
                    if (Hardware.Specs[c.Model].Tier > 0 && Hardware.Specs[c.FakeOf].Scrap) midScrap = true;
                }
            }
            Check(lowFake && !midScrap, "低档次的卡也会被冒充（背后是废片），中高档的假卡不用废片");
            var real = new Unit { Model = "RX80", Health = 100, Look = new CardLook { Seed = 7 } };
            var t = CardLabels.SerialText(real);
            Check(t.StartsWith("MD2-") && CardLabels.Factories("PMD").Contains(int.Parse(t.Substring(4, 2))) && t.Length == 11, $"真卡的序列号：牌子 + 档次码 + 合法工厂码 + 四位流水：{t}");
            var forged = new Unit { Model = "RTX90", FakeOf = "RTX60", Health = 100, Look = new CardLook { Seed = 7, Serial = 1 } };
            var f = CardLabels.SerialText(forged);
            Check(f.StartsWith("PB4-") && (!CardLabels.Factories("Probidia").Contains(int.Parse(f.Substring(4, 2))) || f.Length == 10), $"补贴的序列号：档次码对，工厂码不合法或位数不对：{f}");
            var original = new Unit { Model = "RTX90", FakeOf = "RTX60", Health = 100, Look = new CardLook { Seed = 7, Serial = 2, CountBad = 1, PinBad = 1 } };
            Check(CardLabels.SerialText(original).StartsWith("PB1-"), "贴的是原芯片的序列号：档次码是真实芯片的档次");
            var claim = CardLabels.DealerClaim(original, DealerKind.Scammer, new Rng(1));
            int ex = 0;
            Check(CardLabels.Point(original, claim, CardLabels.RowCount, ref ex) == 2 && (ex & CardLabels.RowSpec) != 0 && CardLabels.Value(original, claim, ex) == CardValue.True(original), "指出供电与风扇：型号自动改回真实型号，价格回到真实价值");
        });

        Test("假卡的四项真伪依据（风扇数、供电口、丝印、序列号）：各自独立露出，至少一项，平均约 1.5 项；真卡全部符合", () =>
        {
            var rng = new Rng(2024);
            int n = 20000; double sum = 0; int[] per = new int[4]; int none = 0;
            for (int i = 0; i < n; i++)
            {
                var label = new[] { "RTX70", "RTX80", "RTX90", "RX80", "RX800", "RX900" }[rng.Range(0, 5)];
                var cheaper = Hardware.Specs.Values.Where(x => !x.Asic && !x.Scrap && x.Brand == Hardware.Specs[label].Brand && x.Tier < Hardware.Specs[label].Tier).ToArray();
                var chip = cheaper[rng.Range(0, cheaper.Length - 1)];
                var look = CardLook.Generate(rng, 70, true, Hardware.Specs[label].Tier, chip.Tier);
                var u = new Unit { Model = label, FakeOf = chip.Model, Health = 70, Look = look };
                var real = Claim.Truth(u); var said = new Claim { Model = label };
                int c = 0;
                int k = 0;
                foreach (int r in new[] { CardLabels.RowCount, CardLabels.RowPin, CardLabels.RowPrint, CardLabels.RowSerial })
                { if (said.Get(r) != real.Get(r)) { c++; per[k]++; } k++; }
                if (c == 0) none++;
                sum += c;
            }
            double mean = sum / n;
            Check(none == 0, "假卡至少有一项依据不符");
            Check(mean > 1.4 && mean < 1.6, $"假卡平均露出 {mean:0.00} 项依据（目标约 1.5）");
            Check(per.All(x => x > n * 0.1), "四项都会单独露出：" + string.Join(" ", per));
            var genuine = new Unit { Model = "RTX80", Health = 70, Look = CardLook.Generate(rng, 70, false) };
            var gt = Claim.Truth(genuine); var gs = new Claim { Model = "RTX80" };
            Check(new[] { CardLabels.RowCount, CardLabels.RowPin, CardLabels.RowPrint, CardLabels.RowSerial }.All(r => gs.Get(r) == gt.Get(r)), "真卡四项依据全部符合");
        });

        Test("旧存档兼容：6 位旧外观的假卡补上风扇数、供电口破绽，真卡和新格式不动", () =>
        {
            var old = CardLook.Decode("000000");
            var fake = CardLook.Legacy("000000", old, "RTX90", "RTX60");
            var u = new Unit { Model = "RTX90", FakeOf = "RTX60", Health = 80, Look = fake };
            Check(fake.CountBad == 1 && fake.PinBad == 1 && u.ObservedCount == 1 && u.ObservedPin == 0, "旧版的低档冒充高档：风扇数和供电口都对不上");
            var claim = new Claim { Model = "RTX90" };
            Check(claim.Get(CardLabels.RowCount) != Claim.Truth(u).Get(CardLabels.RowCount) && claim.Get(CardLabels.RowPin) != Claim.Truth(u).Get(CardLabels.RowPin), "说成标称型号时两项依据都与实物不符");
            var sameCount = CardLook.Legacy("000000", old, "RTX80", "RTX60");
            Check(sameCount.CountBad == 1 && sameCount.PinBad == 1, "RTX 80 冒充：风扇数 3 对 1、供电口 8 针对 6 针都不同");
            var same = CardLook.Legacy("000000", old, "RTX80", "RX800");
            Check(same.CountBad == 0 && same.PinBad == 0, "档次相同（风扇数、供电口一样）的假卡补不出破绽，也不会误加");
            Check(CardLook.Legacy("000000", old, "RTX90", null).CountBad == 0, "真卡不动");
            var newer = new CardLook { CountBad = 0, PinBad = 1, Print = 2 };
            Check(CardLook.Legacy(newer.Encode(), CardLook.Decode(newer.Encode()), "RTX90", "RTX60").CountBad == 0, "新格式（8 位）不再补");
            Check(CardLook.Decode(newer.Encode()).PinBad == 1 && CardLook.Decode("012012").Fan == 0 && CardLook.Decode("01201").Sticker == 1 && CardLook.Decode("01201").Serial == 0, "新旧格式都能读");
        });

        Test("外观随损耗重算：越旧外观越差；种子固定；算力受风扇与金手指影响", () =>
        {
            var rng = new Rng(5);
            int worse = 0, total = 400;
            for (int i = 0; i < total; i++)
            {
                var look = CardLook.Generate(rng, 100, false);
                var a = look.Appearance(100); var b = look.Appearance(20);
                Check(b.Fan >= a.Fan && b.Fingers >= a.Fingers && b.Bracket >= a.Bracket, "健康度下降时外观只会变差");
                if (b.Fan + b.Fingers + b.Bracket > a.Fan + a.Fingers + a.Bracket) worse++;
                Check(look.Appearance(60).Encode() == look.Appearance(60).Encode(), "同一健康度外观固定");
            }
            Check(worse > total * 0.9, $"健康度从 100 掉到 20，九成以上的卡外观变差：{worse}/{total}");
            var fake = CardLook.Generate(rng, 80, true);
            Check(fake.HasFakeSign && fake.Appearance(10).HasFakeSign, "假卡的破绽不随健康度变化");
            var clean = new Unit { Model = "RTX70", Health = 100 };
            var bad = new Unit { Model = "RTX70", Health = 100, Look = new CardLook { Fan = 2, Fingers = 2 } };
            Check(Near(bad.Hash(ModuleBonus.None) / clean.Hash(ModuleBonus.None), 0.92 * 0.94, 1e-9), "风扇缺角 + 金手指氧化：算力 ×0.92 ×0.94");
        });

        Test("杰克逊博士：没有矿架必上一个；基础卡每次两张；进阶第 10 天起、旗舰第 30 天起；特供货第 20 天起必有旗舰", () =>
        {
            var rng = new Rng(11);
            var first = Dealer.Stock(1, false, false, rng);
            Check(first.Count(k => k == Dealer.Rig) == 1 && first.Count(k => Dealer.Basic.Contains(k)) == 2 && first.Count == 3, $"第 1 天：矿架 + 两张基础卡：{string.Join(",", first)}");
            int adv = 0, flag = 0, rigs = 0, n = 4000;
            for (int i = 0; i < n; i++)
            {
                var s9 = Dealer.Stock(9, true, false, rng);
                Check(!s9.Any(k => Dealer.Advanced.Contains(k) || Dealer.Flagship.Contains(k)), "第 9 天没有进阶与旗舰");
                var s40 = Dealer.Stock(40, true, false, rng);
                if (s40.Any(k => Dealer.Advanced.Contains(k))) adv++;
                if (s40.Any(k => Dealer.Flagship.Contains(k))) flag++;
                if (s40.Contains(Dealer.Rig)) rigs++;
            }
            Check(Near(adv / (double)n, 0.60, 0.03) && Near(flag / (double)n, 0.25, 0.03) && Near(rigs / (double)n, 0.30, 0.03),
                $"第 40 天：进阶 {adv / (double)n:P0}（60%），旗舰 {flag / (double)n:P0}（25%），已有矿架时再上 {rigs / (double)n:P0}（30%）");
            Check(Dealer.Stock(20, true, true, rng).Any(k => Dealer.Flagship.Contains(k)) && !Dealer.Stock(19, true, true, new Rng(1)).Any(k => Dealer.Flagship.Contains(k)),
                "特供货：第 20 天起必有旗舰，第 19 天没有");
            Check(Dealer.Basic.Concat(Dealer.Advanced).Concat(Dealer.Flagship).All(m => Hardware.Specs.ContainsKey(m) && !Hardware.Specs[m].Asic), "上架的型号都是登记过的显卡");
        });

        Test("存档：市场、稳定币、全网、钱包、交易所、随机数序列化后读回，之后的结果一致", () =>
        {
            var m = Market.CreateDefault(3);
            var st = new Stablecoins();
            var net = new Network();
            var w = new Wallet { Address = "hot" };
            w.AddDirty(Assets.PSC, 5, 1.5);
            var ex = new Exchange();
            var rng = new Rng(77);
            for (int d = 1; d <= 5; d++) { m.Night(new MarketEvent[0], rng, d, a => 1); st.Night(d, rng, 0); net.Night(d, m.Price, 10); }
            var bundle = new SaveBundle { Market = m, Stable = st, Network = net, Hot = w, Exchange = ex, Rng = rng };
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(bundle);
            var back = Newtonsoft.Json.JsonConvert.DeserializeObject<SaveBundle>(json);
            Check(Near(back.Market.Price, m.Price, 1e-9) && back.Market.Pools.Count == m.Pools.Count && back.Market.Pools[1].Id == m.Pools[1].Id, "交易对与价格");
            Check(back.Hot.Get(Assets.PSC) == 5 && back.Hot.DirtyOf(Assets.PSC) == 1.5, "钱包余额、脏枚数");
            Check(Near(back.Network.NpcHash, net.NpcHash, 1e-9) && Near(back.Stable.Reserve, st.Reserve, 1e-9), "全网算力、CRA 储备");
            for (int d = 6; d <= 10; d++)
            {
                m.Night(new MarketEvent[0], rng, d, a => 1);
                back.Market.Night(new MarketEvent[0], back.Rng, d, a => 1);
            }
            Check(Near(back.Market.Price, m.Price, 1e-9), "读回后继续推进，行情与原来完全相同");
        });

        Test("用币付款：派系对应的币、按币价换算、污点", () =>
        {
            var w = new Wallet { Address = "t" };
            Check(CoinPayment.AssetFor("FACTION_BLACK_MARKET") == Assets.CRT && CoinPayment.AssetFor("FACTION_SECURITY") == null, "派系对应的币");
            double q = CoinPayment.Receive(w, Assets.CRT, 200, 0.5, 40);
            Check(Near(q, 400 * (1 + Tuning.CoinPayPremium), 1e-9) && Near(w.Get(Assets.CRT), q, 1e-9), "按币价换算数量，含溢价");
            Check(Near(w.DirtyOf(Assets.CRT), 80 / 0.5, 1e-9), "脏的 = 货值 × 热度% 那部分的币（160 枚），溢价干净");
            Check(CoinPayment.Receive(w, Assets.CRC, 0, 1, 10) == 0, "零金额不收");
        });

        Test("混币器：按这种币的脏比例带走脏枚数、轮数即天数、手续费与清洗、匿名集、制裁期间整笔脏", () =>
        {
            Func<string, double> px = a => a == Assets.PSC ? 10 : 1;
            var w = new Wallet { Address = "t" };
            w.AddDirty(Assets.CRC, 1000, 200); w.AddDirty(Assets.PSC, 100, 20);   // 总价值 2000，各 20% 脏
            var m = new Mixer();
            Check(m.Deposit(w, Assets.CRC, 1000, 9, 1, px) == null && m.Deposit(w, Assets.CRC, 50, 1, 1, px) == null, "轮数超限、低于最小额不收");
            var d = m.Deposit(w, Assets.CRC, 1000, 2, 1, px);
            Check(d != null && Near(d.Dirty, 200, 1e-9) && w.DirtyOf(Assets.CRC) == 0 && Near(w.DirtyOf(Assets.PSC), 20, 1e-9) && Near(w.Get(Assets.CRC), 0, 1e-9), "存入 1000 CRC：带走 200 枚脏的，PSC 不受影响");
            var rng = new Rng(3);
            Check(m.Night(w, 2, px, rng).Count == 0, "未到期不提回");
            var res = m.Night(w, 3, px, rng);
            Check(res.Count == 1 && !res[0].Rugged, "到期提回");
            double rate = Mixer.RoundRate(1000);
            Check(Near(w.Get(Assets.CRC), 1000 * (1 - Tuning.MixerFee) * (1 - Tuning.MixerFee), 1e-6) && Near(res[0].TaintAfter, 200 * (1 - rate) * (1 - rate), 1e-6), "两轮：手续费与污点按轮数递减");
            Check(Mixer.RoundRate(100000) < Mixer.RoundRate(1000) / 3, "存入越大匿名集占比越大，清洗越差");
            m.OnEvent(MarketEvent.Crackdown, 5, new Rng(1));
            int hit = 0; for (int i = 0; i < 400; i++) { var t = new Mixer(); t.OnEvent(MarketEvent.Crackdown, 1, new Rng((ulong)i + 1)); if (t.Sanctioned(2)) hit++; }
            Check(hit > 150 && hit < 250, $"镇压时约一半概率被制裁：{hit}/400");
            var w2 = new Wallet { Address = "u" }; w2.Add(Assets.CRC, 500);
            var m2 = new Mixer { SanctionedUntil = 10 };
            m2.Deposit(w2, Assets.CRC, 500, 1, 1, px);
            var fr = m2.Night(w2, 2, px, new Rng(5)).Single();
            Check(fr.Flagged && Near(w2.DirtyOf(Assets.CRC), w2.Get(Assets.CRC), 1e-6), $"制裁期间提出：不洗，整笔都是脏的（{w2.DirtyOf(Assets.CRC):0} / {w2.Get(Assets.CRC):0}）");
        });

        Test("CRT 冻结：冻一半、可动用余额减少、到期解冻；声望越高越不容易被冻", () =>
        {
            var w = new Wallet { Address = "f" };
            w.Add(Assets.CRT, 1000);
            double fz = w.Freeze(Assets.CRT, Tuning.CrtFreezeShare, 10);
            Check(Near(fz, 500, 1e-9) && Near(w.Avail(Assets.CRT), 500, 1e-9) && Near(w.Get(Assets.CRT), 1000, 1e-9), "冻结一半：余额不变，可动用 500");
            Check(Near(w.Freeze(Assets.CRT, 1, 12), 500, 1e-9) && Near(w.Avail(Assets.CRT), 0, 1e-9), "再冻一次只冻可动用部分");
            Check(w.Thaw(9).Count == 0 && w.Thaw(12).Count == 1 && Near(w.Avail(Assets.CRT), 1000, 1e-9), "到期（取较晚的日子）全部解冻");
            Check(new Mixer().Deposit(w, Assets.CRT, 2000, 1, 1, a => 1) == null, "余额不足不能存入混币器");
            int low = 0, high = 0;
            for (int i = 0; i < 2000; i++) { if (Stablecoins.CrtFrozenByCrackdown(0, new Rng((ulong)i + 1))) low++; if (Stablecoins.CrtFrozenByCrackdown(50, new Rng((ulong)i + 1))) high++; }
            Check(low > 700 && low < 900 && high == 0, $"声望 0 约 40% 被冻：{low}/2000；声望 50 不会被冻：{high}");
        });

        Test("黑市上新日与 NPC 买家", () =>
        {
            Func<int, BlackMarket, bool> go = (day, m) => m.Refresh(day, 45, new Rng(1), id => 100);
            var sch = new BlackMarket();
            Check(go(10, sch) && !go(11, sch) && !go(13, sch), "周四早上上新后，到周日夜里之前不再刷新");
            Check(go(14, sch) && !go(16, sch) && go(17, sch), "周日夜里、周三夜里结算，周一、周四早上看到新货");
            var late = new BlackMarket();
            Check(go(10, late) && go(19, late), "错过的上新日在下一晚补上");
            var npc = new BlackMarket();
            int units = 20000;
            npc.Catalog.Add(new BmListing { Item = "x", Qty = units });
            npc.Offers.Add(new BmOffer { Item = "y", Qty = 1 });
            npc.Catalog.Add(new BmListing { Item = "z", Qty = 1 });
            int sold = npc.NpcBuys(new Rng(3));
            Check(Near(sold / (double)units, Tuning.BmNpcBuyChance, 0.01), $"每件约 {Tuning.BmNpcBuyChance:P0} 被买走：{sold}/{units}");
            var empty = new BlackMarket();
            empty.Offers.Add(new BmOffer { Item = "y", Qty = 2 });
            empty.Catalog.Add(new BmListing { Item = "z", Qty = 2 });
            var r = new Rng(4);
            for (int i = 0; i < 400; i++) empty.NpcBuys(r);
            Check(empty.Offers.Count == 0 && empty.Catalog.Count == 0, "卖完下架");
        });

        Test("Web3 黑市", () =>
        {
            Func<string, double> price = a => a == Assets.Ponero ? 50 : a == Assets.PSC ? 100 : 1;
            var bm = new BlackMarket();
            Check(!bm.Refresh(10, 5, new Rng(1), id => 100) && bm.Offers.Count == 0, "声望不够不开放");
            Check(bm.Refresh(10, 45, new Rng(1), id => 100) && bm.Offers.Count > 0 && bm.Offers.Where(o => o.Kind == GoodKind.Contraband).All(o => o.Level <= 2 && o.Price == 130), "声望 45 开放中档，溢价 30%");
            Check(!bm.Refresh(12, 45, new Rng(2), id => 100) && bm.Refresh(17, 45, new Rng(2), id => 100), "上新日才刷新");
            var w = new Wallet { Address = "t" };
            w.Add(Assets.PSC, 10); w.AddDirty(Assets.CRT, 1000, 400);   // 总价值 2000
            var offer = new BmOffer { Item = "x", Qty = 1, Price = 500, Level = 1 };
            bm.Offers.Add(offer);
            Check(!bm.Buy(offer, w, Assets.CRC, price), "不收 CRC");
            Check(bm.Buy(offer, w, Assets.CRT, price) && Near(w.Get(Assets.CRT), 500, 1e-9) && Near(w.DirtyOf(Assets.CRT), 200, 1e-6) && !bm.Offers.Contains(offer), "付币：付出的 CRT 按 40% 带走脏的，卖完下架");
            w = new Wallet { Address = "t" };
            w.AddDirty(Assets.PSC, 10, 5);   // 总价值 1000，PSC 价 100，一半脏
            double loss = bm.DeskLoss(500, 0.5, 20);
            double got = bm.ToPonero(w, Assets.PSC, 5, 20, price);   // 价值 500
            Check(Near(got, 500 * (1 - loss) / 50, 1e-9) && loss > Tuning.PoneroFee + Tuning.PoneroDirtyFee && Near(w.DirtyOf(Assets.PSC), 2.5, 1e-6) && w.DirtyOf(Assets.Ponero) == 0 && Near(w.Get(Assets.PSC), 5, 1e-9), "兑入 Ponero：扣脏币附加费和流动性损耗，兑掉的脏币消失，Ponero 干净");
            Check(Near(bm.DeskUsedNow(20), 500, 1e-9) && Near(bm.DeskUsedNow(21), 250, 1e-9), "累计兑换量每晚减半");
            Check(bm.ToPonero(w, Assets.PSC, 5.1, 20, price) < 0, "余额不足不能兑");
            var big = new BlackMarket(); var rich = new Wallet { Address = "r" }; rich.Add(Assets.CRT, 400000);
            Check(big.ToPonero(rich, Assets.CRT, 300000, 20, price) > 0, "没有每日额度，一次可以兑很多");
            Check(Near(BlackMarket.DeskFeeRate(0.049), Tuning.PoneroFee, 1e-9) && Near(BlackMarket.DeskFeeRate(0.05), Tuning.PoneroFee + Tuning.PoneroDirtyFee, 1e-9), "干净币只收基础费，带脏币另收附加费");
            Check(BlackMarket.DeskQuote(Assets.PSC, 130, 100, true) == 100 && BlackMarket.DeskQuote(Assets.PSC, 80, 100, true) == 80, "兑入：PSC 取开盘价和现价里低的");
            Check(BlackMarket.DeskQuote(Assets.PSC, 130, 100, false) == 130 && BlackMarket.DeskQuote(Assets.PSC, 80, 100, false) == 100, "兑出：PSC 取开盘价和现价里高的");
            Check(Near(BlackMarket.DeskQuote(Assets.Ponero, 130, 100, true), 100 * Tuning.PoneroPscRatio, 1e-9), "Ponero 按开盘价，不跟现价");
            double back = bm.FromPonero(w, Assets.CRT, 2, 22, price);
            Check(back > 0 && back < 2 * 50 * (1 - Tuning.PoneroFee) + 1e-9 && Near(w.Get(Assets.CRT), back, 1e-9), "兑出 Ponero：只收基础费和流动性损耗");
            // 100% 脏币当天兑入、次日兑出的总损耗，要高于混币器任意轮数洗掉同样污点的成本（单笔不超过 10 万）
            foreach (double v in new[] { 1000.0, 5000, 10000, 30000, 100000 })
                for (int rounds = 1; rounds <= Tuning.MixerMaxRounds; rounds++)
                {
                    var desk = new BlackMarket();
                    double inL = desk.DeskLoss(v, 1, 50);
                    var wl = new Wallet { Address = "d" }; wl.AddDirty(Assets.CRT, v, v);
                    Func<string, double> px = a => a == Assets.CRT ? 1 : 50;
                    double ponero = desk.ToPonero(wl, Assets.CRT, v, 50, px);
                    double outCr = desk.FromPonero(wl, Assets.CRT, ponero, 51, px);
                    double pLoss = 1 - outCr / v;
                    double rem = Math.Pow(1 - Mixer.RoundRate(v), rounds), mixLoss = (1 - Math.Pow(1 - Tuning.MixerFee, rounds)) / (1 - rem);
                    Check(pLoss > mixLoss && inL > 0, $"Ponero 损耗高于混币器：{v:0}、{rounds} 轮 {pLoss:P1} > {mixLoss:P1}");
                }
        });

        Test("黑市订单", () =>
        {
            Func<string, double> price = a => a == Assets.CRT ? 1 : 100;
            Func<string, long> value = id => 200;
            var bm = new BlackMarket();
            bm.RefreshOrders(10, 75, new Rng(5), value);
            Check(bm.Sellers.Count >= Tuning.BmMerchantCount && bm.Catalog.Count > 0 && bm.Catalog.All(l => l.Qty > 0 && l.Price >= 1) && bm.Catalog.Where(l => l.Kind != GoodKind.Gpu && bm.Sellers[l.Seller].Type != MerchantType.Personal).All(l => l.Price >= 200 * bm.Sellers[l.Seller].Factor * 0.97 - 1 && l.Price <= 200 * bm.Sellers[l.Seller].Factor * 1.03 + 1), "目录：商家与单价（基础价 × 商家系数）");
            Check(BlackMarket.Pay(170, PayMode.Escrow) == 170 + 9 && BlackMarket.Pay(170, PayMode.Advance) == 153 && BlackMarket.OrderTotal(100, 5) == 475 && BlackMarket.OrderTotal(100, 10) == 900 && BlackMarket.OrderTotal(100, 4) == 400, "托管多付 5%，提前放款打九折");
            var good = new BmSeller { Name = "g", Trades = 100, Rating = 95 };
            var bad = new BmSeller { Name = "b", Trades = 100, Rating = 95, Scam = true };
            bm.Sellers.Clear(); bm.Sellers.Add(good); bm.Sellers.Add(bad);
            var lg = new BmListing { Qty = 99, Seller = 0, Item = "x", Price = 1000 };
            var lb = new BmListing { Qty = 99, Seller = 1, Item = "x", Price = 1000 };
            var w = new Wallet { Address = "t" };
            w.AddDirty(Assets.CRT, 10000, 4000);
            var o = bm.Place(lb, 1, PayMode.Escrow, w, Assets.CRT, 10, price);
            Check(o != null && Near(w.Get(Assets.CRT), 10000 - 1050, 1e-9) && Near(o.Dirty, 420, 1e-6) && w.DirtyOf(Assets.CRT) < 4000 && bm.Place(lg, 1, PayMode.Escrow, w, Assets.CRC, 10, price) == null, "托管下单：扣币、脏的 420 枚留在订单上；不收 CRC");
            double tBefore = 4000;
            var ev = bm.Night(13, false, w, new Rng(1));
            Check(ev.Any(e => e.Kind == "scam_refund") && Near(w.Get(Assets.CRT), 10000, 1e-6) && Near(w.DirtyOf(Assets.CRT), tBefore, 1e-6), "骗子卖家 + 托管：退款，脏枚数一并还回（不能借退款洗钱）");
            var o2 = bm.Place(lb, 1, PayMode.Advance, w, Assets.CRT, 20, price);
            var ev2 = bm.Night(23, false, w, new Rng(2));
            Check(o2.State == OrderState.Lost && Near(w.Get(Assets.CRT), 10000 - 900, 1e-6), "骗子卖家 + 提前放款：钱没了");
            var o3 = bm.Place(lg, 1, PayMode.Escrow, w, Assets.CRT, 30, price);
            Check(bm.Night(31, false, w, new Rng(3)).Count == 0 && o3.State == OrderState.Pending, "没到期不动");
            int ready = 0, lost = 0;
            for (int i = 0; i < 400; i++)
            {
                var t = new BlackMarket(); t.Sellers.Add(good);
                var ww = new Wallet(); ww.Add(Assets.CRT, 5000);
                var oo = t.Place(new BmListing { Qty = 99, Seller = 0, Item = "x", Price = 100 }, 1, PayMode.Escrow, ww, Assets.CRT, 1, price);
                t.Night(oo.DueDay, false, ww, new Rng((ulong)i + 7));
                if (oo.State == OrderState.Ready) ready++; else lost++;
            }
            Check(ready > 340 && lost > 10, $"上门：约 {Tuning.BmInterceptDoor:P0} 被截获（跑路另算）：到货 {ready}，丢失 {lost}");
            var c = new BlackMarket(); c.Sellers.Add(good);
            var cw = new Wallet(); cw.Add(Assets.CRT, 5000);
            var co = c.Place(new BmListing { Qty = 99, Seller = 0, Item = "x", Price = 100 }, 1, PayMode.Escrow, cw, Assets.CRT, 1, price);
            c.Night(co.DueDay, true, cw, new Rng(1));
            Check(co.State == OrderState.Pending && co.DueDay == 5, "镇压期间到期顺延一天");
            // 蜜罐期间下单会被追查；受控交付签收记罪证、拒签不记
            var h = new BlackMarket(); h.Sellers.Add(good); h.HoneypotUntil = 12;
            var hw = new Wallet(); hw.Add(Assets.CRT, 5000);
            var ho = h.Place(new BmListing { Qty = 99, Seller = 0, Item = "x", Price = 100 }, 1, PayMode.Escrow, hw, Assets.CRT, 10, price);
            Check(h.Traps.Count == 1 && h.Traps[0].Day == 10 + Tuning.BmTrapDelay, "蜜罐期间下单：记一笔追查");
            Check(h.Night(h.Traps[0].Day, false, hw, new Rng(3)).Any(e => e.Kind == "trap" && Near(e.Amount, ho.Price * Tuning.BmTrapShare, 1e-9)) && h.Traps.Count == 0, "追查到期：产生罪证事件");
            int ctrl = 0;
            for (int i = 0; i < 600; i++)
            {
                var t2 = new BlackMarket(); t2.Sellers.Add(good);
                var w2 = new Wallet(); w2.Add(Assets.CRT, 5000);
                var o2b = t2.Place(new BmListing { Qty = 99, Seller = 0, Item = "x", Price = 100 }, 1, PayMode.Escrow, w2, Assets.CRT, 1, price);
                t2.Night(o2b.DueDay, false, w2, new Rng((ulong)i + 99), 0);
                if (o2b.Controlled) ctrl++;
            }
            Check(ctrl > 5 && ctrl < 50, $"被截获里一部分变成受控交付：{ctrl}/600");
            var co2 = new BmOrder { Item = "x", Price = 150, State = OrderState.Ready, Controlled = true };
            var cm = new BlackMarket();
            Check(Near(cm.Sign(co2, true), 150, 1e-9) && co2.State == OrderState.Lost && co2.Why == "seized" && Near(cm.Sign(new BmOrder { Price = 50 }, false), 0, 1e-9), "签收记罪证（金额）并查封，拒签不记");
            Check(BlackMarket.HoneypotChance(100) > BlackMarket.HoneypotChance(0) && BlackMarket.HoneypotChance(100000) <= Tuning.BmHoneypotMax, "治安声望越高越容易被接管，有封顶");
            // 没收到货的订单不会被追查：骗子退款后蜜罐追查取消
            var hb = new BlackMarket(); hb.Sellers.Add(bad); hb.HoneypotUntil = 50;
            var hw2 = new Wallet(); hw2.Add(Assets.CRT, 5000);
            var hl = new BmListing { Seller = 0, Item = "x", Price = 100, Qty = 9 };
            var ho2 = hb.Place(hl, 1, PayMode.Escrow, hw2, Assets.CRT, 10, price);
            Check(hb.Traps.Count == 1 && ho2.Id > 0, "蜜罐期间下单：先挂追查");
            hb.Night(ho2.DueDay, false, hw2, new Rng(4));
            Check(ho2.State == OrderState.Refunded && hb.Traps.Count == 0, "骗子卖家退款后追查取消（没拿到货不记罪证）");
            // 跑路：托管资金多则概率高；跑路后关闭
            var r = new BlackMarket(); r.Sellers.Add(good);
            var rw = new Wallet(); rw.Add(Assets.CRT, 1000000);
            for (int i = 0; i < 20; i++) r.Place(new BmListing { Qty = 99, Seller = 0, Item = "x", Price = 10000 }, 1, PayMode.Escrow, rw, Assets.CRT, 1, price);
            Check(r.RugChance > BlackMarketTestRug(), "托管资金越多越容易跑路");
            r.ClosedUntil = 40;
            Check(r.Closed(40) && !r.Closed(41) && r.Place(new BmListing { Qty = 99, Seller = 0, Item = "x", Price = 1 }, 1, PayMode.Escrow, rw, Assets.CRT, 30, price) == null, "关闭期间不能下单");
        });

        Test("黑市出售", () =>
        {
            Func<string, double> price = a => a == Assets.CRT ? 1 : 100;
            Check(Near(BlackMarket.ExpectedDays(0.6), 1, 1e-9) && Near(BlackMarket.ExpectedDays(0.9), 1, 1e-9) && Near(BlackMarket.ExpectedDays(1.0), 2, 1e-9) && Near(BlackMarket.ExpectedDays(1.8), 14, 1e-6), "期望成交天数：0.9 以下 1 天，1.0 两天，1.8 十四天");
            Check(BlackMarket.ExpectedDays(1.2) > BlackMarket.ExpectedDays(1.0) && BlackMarket.ExpectedDays(1.5) > BlackMarket.ExpectedDays(1.2), "倍率越高越慢");
            Check(BlackMarket.TaintChance(1.8) > BlackMarket.TaintChance(0.6) && BlackMarket.TaintRatio(1.8) > BlackMarket.TaintRatio(0.6) && Near(BlackMarket.TaintChance(0.6), Tuning.BmSaleTaintChanceMin, 1e-9), "倍率越高，带污点的概率和比例越高");
            var bm = new BlackMarket();
            var w = new Wallet(); w.Add(Assets.CRT, 1000);
            Check(bm.List("c4", null, null, 0, 500, 2.0, w, Assets.CRT, 1, price) == null && bm.List("c4", null, null, 0, 500, 0.5, w, Assets.CRT, 1, price) == null, "倍率超出 0.6–1.8 不能上架");
            var s = bm.List("c4", null, null, 0, 500, 1.0, w, Assets.CRT, 1, price);
            Check(s != null && s.ListPrice == 500 && Near(w.Get(Assets.CRT), 1000 - 25, 1e-9), "上架：标价 500，上架费 5%");
            int sold1 = 0, sold18 = 0;
            for (int i = 0; i < 2000; i++) { if (new Rng((ulong)i + 1).Chance(bm.SaleChance(0.8))) sold1++; if (new Rng((ulong)i + 1).Chance(bm.SaleChance(1.8))) sold18++; }
            Check(sold1 == 2000 && sold18 > 80 && sold18 < 220, $"每晚成交概率：0.8 倍必成交，1.8 倍约 1/14：{sold18}/2000");
            // 成交与到账：夜晚 1 成交（0.8 倍必成交），托管 3 天后到钱包
            var b2 = new BlackMarket();
            var w2 = new Wallet(); w2.Add(Assets.CRT, 1000);
            var s2 = b2.List("c4", null, null, 0, 500, 0.8, w2, Assets.CRT, 1, price);
            double crt0 = w2.Get(Assets.CRT);
            var e1 = b2.Night(2, false, w2, new Rng(7), 0, price);
            Check(s2.State == SaleState.Sold && s2.Proceeds == (int)Math.Round(400 * (1 - Tuning.BmCommission)) && s2.PayDay == 2 + Tuning.BmSaleHold && Near(w2.Get(Assets.CRT), crt0, 1e-9), "成交：扣抽成，货款先托管");
            b2.Night(4, false, w2, new Rng(8), 0, price);
            Check(s2.State == SaleState.Sold, "没到放款日不动");
            b2.Night(5, false, w2, new Rng(9), 0, price);
            Check(s2.State == SaleState.Paid && Near(w2.Get(Assets.CRT), crt0 + s2.Proceeds, 1e-9) && Near(w2.DirtyOf(Assets.CRT), s2.Taint, 1e-9) && (s2.Taint == 0 || Near(s2.Taint, s2.Proceeds * BlackMarket.TaintRatio(0.8), 1e-6)), "放款：货款进钱包，按污点比例记脏的（CRT 价 1）");
            // 赃物：货款按热度带污点，不走随机
            var bh = new BlackMarket();
            var wh = new Wallet(); wh.Add(Assets.CRT, 1000);
            var sh = bh.List("c4", null, null, 0, 500, 0.8, wh, Assets.CRT, 1, price, 60);
            bh.Night(2, false, wh, new Rng(7), 0, price);
            Check(sh.State == SaleState.Sold && Near(sh.Taint, sh.Proceeds * 0.6, 1e-9), $"热度 60% 的赃物：污点 {sh.Taint:0} = 货款 × 60%");
            // 假卡被看穿
            int fake = 0;
            for (int i = 0; i < 400; i++)
            {
                var b3 = new BlackMarket(); var w3 = new Wallet(); w3.Add(Assets.CRT, 1000);
                var s3 = b3.List("gpu:RTX70", "RTX70", "RTX60", 60, 500, 0.8, w3, Assets.CRT, 1, price);
                b3.Night(2, false, w3, new Rng((ulong)i + 3), 0, price);
                if (s3.State == SaleState.Delisted && s3.Why == "fake") { fake++; if (b3.Rating >= Tuning.BmRatingStart) throw new Exception("好评没降"); }
            }
            Check(fake > 150 && fake < 250, $"假卡约一半被看穿退货：{fake}/400");
            // 平台跑路：上架的货与未放款货款都没了
            var b4 = new BlackMarket(); var w4 = new Wallet(); w4.Add(Assets.CRT, 100000);
            for (int i = 0; i < 30; i++) b4.List("c" + (i % 6), null, null, 0, 5000, 1.8, w4, Assets.CRT, 1, price);
            bool rugged = false;
            for (int d = 2; d < 400 && !rugged; d++) { var ev = b4.Night(d, false, w4, new Rng((ulong)d * 31), 0, price); rugged = ev.Any(e => e.Kind == "sale_rug"); }
            Check(rugged && b4.Sales.All(x => x.State != SaleState.Listed && x.State != SaleState.Sold), "跑路：上架与未放款全部归零");
            var b6 = new BlackMarket(); var w6 = new Wallet(); w6.Add(Assets.CRT, 100000);
            int okStack = 0; for (int i = 0; i < 8; i++) if (b6.List("c4", null, null, 0, 500, 1.0, w6, Assets.CRT, 1, price) != null) okStack++;
            Check(okStack == Tuning.BmMaxStack && b6.ActiveKinds == 1 && b6.ActiveListings == 5, "同一种商品最多上架 5 件");
            int okKinds = 0; for (int k = 0; k < 9; k++) if (b6.List("k" + k, null, null, 0, 500, 1.0, w6, Assets.CRT, 1, price) != null) okKinds++;
            Check(b6.ActiveKinds == Tuning.BmMaxKinds && okKinds == Tuning.BmMaxKinds - 1, "同时最多上架 6 种");
            Check(b6.List("c4", null, null, 0, 500, 1.2, w6, Assets.CRT, 1, price) == null && b6.ListBlock(BmSale.KeyOf("c4", null, null, 0), 1.2, 1) != null && b6.ListBlock(BmSale.KeyOf("k0", null, null, 0), 1.2, 1) == "mult" && b6.ListBlock(BmSale.KeyOf("k0", null, null, 0), 1.0, 1) == null, "同一种商品倍率必须相同；种数满了新种不能上");
            b6.Delist(b6.Sales.First(x => x.Item == "k0"));
            Check(b6.ListBlock(BmSale.KeyOf("k0", null, null, 0), 1.2, 1) == null && b6.ListBlock(BmSale.KeyOf("zz", null, null, 0), 1.0, 1) == null, "下架后腾出名额，也可换倍率");
            // 收款币：每接受一种币多一轮成交判定，货款用命中的币付
            Func<int, string[], (int sold, int[] byCoin)> trial = (n, coins) =>
            {
                int sold = 0; var cnt = new int[4];
                for (int i = 0; i < n; i++)
                {
                    var bx = new BlackMarket(); var wx = new Wallet(); wx.Add(Assets.CRT, 1000);
                    bx.SellAccepts = coins.ToList();
                    var sx = bx.List("c4", null, null, 0, 500, 1.5, wx, Assets.CRT, 1, price);
                    bx.Night(2, false, wx, new Rng((ulong)i + 11), 0, price);
                    if (sx.State == SaleState.Sold) { sold++; cnt[Array.IndexOf(BlackMarket.SellCoins, sx.PayAsset)]++; }
                }
                return (sold, cnt);
            };
            var one = trial(4000, new[] { Assets.CRT });
            var all = trial(4000, BlackMarket.SellCoins);
            Check(all.sold > one.sold * 1.5 && one.byCoin.Skip(0).Where((c, i) => BlackMarket.SellCoins[i] != Assets.CRT).All(c => c == 0) && all.byCoin.All(c => c > 0), $"接受的币越多越容易成交，货款币种随机：{one.sold} → {all.sold}（{string.Join("/", all.byCoin)}）");
            var bt = new BlackMarket();
            Check(!bt.ToggleCoin(Assets.CRT) && bt.ToggleCoin(Assets.PSC) && bt.AcceptedCoins.Count() == 2 && bt.ToggleCoin(Assets.CRT) && !bt.ToggleCoin(Assets.PSC) && !bt.ToggleCoin(Assets.CRC), "收款币至少留一种，只有四种币可选");
            var b8 = new BlackMarket(); var w8 = new Wallet(); w8.Add(Assets.CRT, 1000);
            b8.SellAccepts = new List<string> { Assets.PSC };
            var s8 = b8.List("c4", null, null, 0, 500, 0.8, w8, Assets.CRT, 1, price);
            b8.Night(2, false, w8, new Rng(3), 0, price);
            double psc0 = w8.Get(Assets.PSC);
            b8.Night(5, false, w8, new Rng(4), 0, price);
            Check(s8.State == SaleState.Paid && s8.PayAsset == Assets.PSC && Near(w8.Get(Assets.PSC) - psc0, s8.Proceeds / 100.0, 1e-9), "货款以命中那一轮的币到账");
            // 镇压期间不推进
            var b5 = new BlackMarket(); var w5 = new Wallet(); w5.Add(Assets.CRT, 1000);
            var s5 = b5.List("c4", null, null, 0, 500, 0.8, w5, Assets.CRT, 1, price);
            b5.Night(2, true, w5, new Rng(1), 0, price);
            Check(s5.State == SaleState.Listed, "镇压期间平台下线，不成交");
        });

        Test("区块增长", () =>
        {
            var r = new Rng(5); int lo = int.MaxValue, hi = 0; long sum = 0;
            for (int i = 0; i < 2000; i++) { int n = TxRules.BlocksPerDay(r); lo = Math.Min(lo, n); hi = Math.Max(hi, n); sum += n; }
            Check(lo >= Tuning.BlocksPerDayMin && hi <= Tuning.BlocksPerDayMax && hi - lo > 30 && Near(sum / 2000.0, (Tuning.BlocksPerDayMin + Tuning.BlocksPerDayMax) / 2.0, 3), "每晚区块增量在范围内随机");
        });

        Test("黑市全新卡", () =>
        {
            int fresh = 0, total = 0, bad = 0;
            for (int i = 0; i < 300; i++)
            {
                var bm = new BlackMarket();
                bm.Refresh(10, 75, new Rng((ulong)i + 11), id => 100);
                foreach (var o in bm.Offers.Where(x => x.Kind == GoodKind.Gpu))
                {
                    total++;
                    if (o.Health >= 100) { fresh++; if (o.FakeOf != null) bad++; }
                }
            }
            Check(total > 300 && fresh > total * 0.08 && fresh < total * 0.25 && bad == 0, $"显卡里约 {Tuning.BmGpuNew:P0} 是全新卡，且不会是假卡：{fresh}/{total}，假 {bad}");
        });

        Test("黑市商家", () =>
        {
            double sum = 0; int bulk = 0, n = 300;
            var types = new System.Collections.Generic.HashSet<MerchantType>();
            for (int i = 0; i < n; i++)
            {
                var bm = new BlackMarket();
                bm.RefreshOrders(20, 75, new Rng((ulong)i + 5), id => 250);
                sum += bm.CatalogValue;
                if (bm.Sellers.Any(x => x.Type == MerchantType.Bulk)) bulk++;
                foreach (var s in bm.Sellers) types.Add(s.Type);
                if (bm.Sellers.Where(x => x.Type != MerchantType.Bulk && x.Type != MerchantType.Personal).Select(x => x.Type).Distinct().Count() != bm.Sellers.Count(x => x.Type != MerchantType.Bulk && x.Type != MerchantType.Personal)) throw new Exception("同类型商家重复");
            }
            double avg = sum / n;
            Check(avg > 13000 && avg < 67000, $"每次上新的货架总值（统一基础价 250）：平均 {avg:0}");
            Check(bulk > n * 0.15 && bulk < n * 0.35 && types.Count >= 20, $"大宗商家约 {Tuning.BmBulkChance:P0} 的上新出现率：{bulk}/{n}；七种类型都出现过");
            // 批量下单：折扣、库存扣减
            Func<string, double> price = a => 1;
            var m = new BlackMarket(); m.Sellers.Add(new BmSeller { Name = "a", Trades = 100, Type = MerchantType.Modules, Factor = 0.8 });
            var l = new BmListing { Seller = 0, Item = "x", Price = 100, Qty = 8 };
            m.Catalog.Add(l);
            var w = new Wallet(); w.Add(Assets.CRT, 5000);
            var o = m.Place(l, 5, PayMode.Advance, w, Assets.CRT, 1, price);
            Check(o != null && o.Count == 5 && o.Price == (int)Math.Round(475 * 0.9) && l.Qty == 3 && m.Catalog.Contains(l), "订 5 件：95 折，再按提前放款打折，库存扣 5");
            Check(m.Place(l, 4, PayMode.Escrow, w, Assets.CRT, 1, price) == null && m.Place(l, 3, PayMode.Escrow, w, Assets.CRT, 1, price) != null && !m.Catalog.Contains(l), "超过库存不能订；订完下架");
            var bs = new BmSeller { Type = MerchantType.Bulk };
            Check(bs.Days == Tuning.BmBulkDays && new BmSeller { Type = MerchantType.Modules }.Days == Tuning.BmDoorDays, "大宗商家送货更久");
            // 出售量推高污点
            var b2 = new BlackMarket();
            double v0 = b2.VolumeTaint(10);
            b2.Sales.Add(new BmSale { State = SaleState.Sold, Proceeds = 14000, PayDay = 12 });
            Check(Near(v0, 0, 1e-9) && Near(b2.VolumeTaint(10), Tuning.BmVolumeTaintMax, 1e-9) && b2.VolumeTaint(30) == 0, "近 7 天成交额推高污点，过期不算");
        });

        Test("尖端店铺与个人卖家", () =>
        {
            int elite = 0, n = 400, personalListings = 0; double lo = 9, hi = 0;
            for (int i = 0; i < n; i++)
            {
                var bm = new BlackMarket();
                bm.RefreshOrders(20, 75, new Rng((ulong)i + 77), id => id == "hypertech_bay" || id == "turbo_booster_hypertech" ? 10000 : id == "mind_reprogrammer" ? 5000 : 200);
                if (bm.Sellers.Any(x => x.Type == MerchantType.Elite)) elite++;
                if (bm.Sellers.Count(x => x.Type == MerchantType.Personal) != Tuning.BmPersonalCount) throw new Exception("个人卖家数不对");
                foreach (var l in bm.Catalog.Where(x => bm.Sellers[x.Seller].Type == MerchantType.Elite))
                    if (l.Kind != GoodKind.Gpu && l.Price < Tuning.BmEliteMinValue * Tuning.BmFactorMin - 1) throw new Exception("尖端店铺卖了便宜货：" + l.Item + " " + l.Price);
                foreach (var l in bm.Catalog.Where(x => bm.Sellers[x.Seller].Type == MerchantType.Personal && x.Kind == GoodKind.Contraband)) { personalListings++; double f = l.Price / 200.0; lo = Math.Min(lo, f); hi = Math.Max(hi, f); }
                foreach (var s in bm.Sellers.Where(x => x.Type == MerchantType.Personal)) if (s.Advance) throw new Exception("个人卖家不该接受提前放款");
            }
            Check(elite > n * 0.08 && elite < n * 0.25, $"尖端店铺约 {Tuning.BmEliteChance:P0} 的周出现：{elite}/{n}");
            Check(personalListings > 100 && lo >= Tuning.BmFactorPersonalMin - 0.01 && hi <= Tuning.BmFactorPersonalMax + 0.01 && hi - lo > (Tuning.BmFactorPersonalMax - Tuning.BmFactorPersonalMin) * 0.8, $"个人卖家每件价格系数分散：{lo:0.00}–{hi:0.00}");
            var every = new BlackMarket();
            every.RefreshOrders(20, 90, new Rng(5), id => 200, true);
            var goods = every.Catalog.Where(x => x.Kind != GoodKind.Gpu).ToList();
            Func<BmListing, bool> inRange = x =>
            {
                var t = every.Sellers[x.Seller].Type;
                double min = t == MerchantType.Bulk ? Tuning.BmFactorBulkMin : t == MerchantType.Personal ? Tuning.BmFactorPersonalMin : Tuning.BmFactorMin;
                double max = t == MerchantType.Bulk ? Tuning.BmFactorBulkMax : t == MerchantType.Personal ? Tuning.BmFactorPersonalMax : Tuning.BmFactorMax;
                return x.Price >= 200 * min - 1 && x.Price <= 200 * max + 1;
            };
            Check(goods.Count > 20 && goods.All(inRange), "各类商家的价格都在自己的系数范围内");
            var used = every.Catalog.Where(x => every.Sellers[x.Seller].Type == MerchantType.GpuUsed).ToList();
            Check(used.Count > 0 && used.All(x => x.Price >= CardValue.Of(x.Model, x.Health) * Tuning.BmFactorUsedMin - 1 && x.Price <= CardValue.Of(x.Model, x.Health) * Tuning.BmFactorUsedMax + 1), "二手矿卡按自己的系数范围定价");
        });

        Test("链上地址校验位", () =>
        {
            var rng = new Rng(5);
            int caught = 0, typos = 0;
            for (int i = 0; i < 2000; i++)
            {
                string a = ChainAddress.Make(rng.NextUInt());
                Check(ChainAddress.Valid(a) && ChainAddress.Valid(a.ToUpperInvariant().Replace("0X", "0x")) && ChainAddress.Valid(" " + a + " "), "生成的地址合法，大小写、空格不影响：" + a);
                int pos = 2 + rng.Range(0, ChainAddress.BodyLength + ChainAddress.CheckLength - 1);
                char c = a[pos];
                char t = "0123456789abcdef"[(Convert.ToInt32(c.ToString(), 16) + 1 + rng.Range(0, 14)) % 16];
                string typo = a.Substring(0, pos) + t + a.Substring(pos + 1);
                typos++;
                if (!ChainAddress.Valid(typo)) caught++;
            }
            Check(caught > typos * 0.98 && caught < typos, $"输错一位几乎都被校验位拦下，极少数碰巧合法：{caught}/{typos}");
            Check(!ChainAddress.Valid("0x12345") && !ChainAddress.Valid("0xzzzzzzzz") && !ChainAddress.WellFormed("12345678ab"), "格式不对的地址不合法");
        });

        Test("RatDigg 矿机：规格、维修、矿机时代、矿机商", () =>
        {
            var m1 = Hardware.Specs["M1"]; var m3 = Hardware.Specs["M3"];
            Check(m1.Asic && Math.Abs(m1.Hash - 50) < 1e-9 && m3.Hash == 300 && m1.Kj / m1.Hash < Hardware.Specs["RTX90"].Kj / Hardware.Specs["RTX90"].Hash, "矿机每 H 耗电低于最好的显卡");
            var u = new Unit { Model = "M2", Health = 100 };
            Check(Math.Abs(u.Hash(ModuleBonus.None) - 120) < 1e-9 && Math.Abs(u.Kj(ModuleBonus.None) - 110) < 1e-9, "M2 满健康 120 H、110 kJ");
            Check(Math.Abs(u.DeathChance(ModuleBonus.None) - Tuning.DeathBase * 0.5) < 1e-12 && Math.Abs(new Unit { Model = "RTX90", Health = 100 }.DeathChance(ModuleBonus.None) - Tuning.DeathBase * 0.7) < 1e-12, "矿机暴毙系数 0.5（Probidia 0.7）");
            Check(Math.Abs(CardValue.Of("M1", 100) - 11000) < 1 && CardValue.Of("M1", 0) < 11000 * 0.3, "矿机价值按健康折算，和显卡同一公式");
            // 维修：按顺序收元件，别的不收
            int step = 0, prog = 0;
            Check(AsicRepair.Insert("M1", ref step, ref prog, "rare_electronic", 1) == 0, "M1 先要普通元件，稀有的不收");
            Check(AsicRepair.Insert("M1", ref step, ref prog, "common_electronic", 3) == 3 && step == 0 && prog == 3, "收 3 个普通元件，进度 3/4");
            Check(AsicRepair.Insert("M1", ref step, ref prog, "common_electronic", 5) == 1 && step == 1 && prog == 0, "只再收 1 个，进下一步");
            Check(AsicRepair.Insert("M1", ref step, ref prog, "rare_electronic", 2) == 1 && AsicRepair.Done("M1", step), "再放 1 个稀有，修好；多的不收");
            double cost = new[] { "M1", "M2", "M3" }.Max(m => AsicRepair.Steps(m).Sum(x => x.Count * (x.Item == "common_electronic" ? 33 : x.Item == "rare_electronic" ? 350 : 2500)) / (double)Hardware.Specs[m].Price);
            Check(cost > 0.02 && cost < 0.06, $"修理约占机价 4%：最贵的 {cost:P1}");
            // 矿机时代：定一天、提前预兆、永久减半
            var net = new Network(); var rng = new Rng(3);
            double kj0 = net.NpcKj; int warns = 0, starts = 0, warnDay = -1, startDay = -1;
            for (int d = 1; d <= 120; d++) { var st = net.EraNight(d, rng); if (st == Network.EraStep.Warn) { warns++; warnDay = d; } if (st == Network.EraStep.Start) { starts++; startDay = d; } }
            Check(warns == 1 && starts == 1 && startDay >= Tuning.EraMin && startDay <= Tuning.EraMax && startDay - warnDay >= Tuning.EraWarnMin && startDay - warnDay <= Tuning.EraWarnMax, $"预兆第 {warnDay} 天，开始第 {startDay} 天");
            Check(Math.Abs(net.NpcKj - kj0 * Tuning.EraKjMult) < 1e-9 && net.EraNight(200, rng) == Network.EraStep.None, "NPC 耗电效率翻倍，只发生一次");
            var late = new Network(); Check(late.EraNight(200, new Rng(1)) == Network.EraStep.None && late.EraDay >= 200 + Tuning.EraWarnMax + 1, "存档很晚才接入：至少留出预兆天数");
            // 均衡：效率翻倍后 NPC 算力涨
            var a = new Network(); var b = new Network { NpcKj = Tuning.NpcKjPerH * Tuning.EraKjMult };
            for (int d = 1; d <= 200; d++) { a.Night(d, 100, 0); b.Night(d, 100, 0); a.Reward = b.Reward = 100; }
            Check(b.NpcHash > a.NpcHash * 1.5, $"耗电减半后均衡算力 {a.NpcHash:0} → {b.NpcHash:0}");
            // 矿机商：按声望放开型号
            int none = 0, m1only = 0, withM3 = 0, weeks = 600;
            for (int i = 0; i < weeks; i++)
            {
                foreach (var rep in new[] { 30, 50, 90 })
                {
                    var bm = new BlackMarket();
                    bm.RefreshOrders(10, rep, new Rng((ulong)(i * 7 + rep)), _ => 200);
                    var asic = bm.Catalog.Where(l => bm.Sellers[l.Seller].Type == MerchantType.Asic).ToList();
                    if (rep == 30 && asic.Count == 0) none++;
                    if (rep == 50 && asic.Count > 0 && asic.All(l => l.Model == "M1")) m1only++;
                    if (rep == 90 && asic.Any(l => l.Model == "M3")) withM3++;
                    foreach (var l in asic) if (l.Model == "M3" && rep < Tuning.AsicRepM3 || l.Model == "M2" && rep < Tuning.AsicRepM2) throw new Exception("声望不够不该出现：" + l.Model + " " + rep);
                }
            }
            Check(none == weeks && m1only > weeks * 0.05 && withM3 > weeks * 0.01, $"声望 30 不出现；50 只有 M1（{m1only}/{weeks} 周）；90 有 M3（{withM3}/{weeks} 周）");
        });

        Test("永续合约：开平仓、强平、基金与欠款、自动减仓、没收", () =>
        {
            Func<string, double> pr = a => a == Assets.PSC ? 100 : 1;
            Wallet NewAcct(double crc) { var w = new Wallet { Address = "pb" }; w.Add(Assets.CRC, crc); return w; }
            // 滑点：名义价值占永续深度的比例，买入往上、卖出往下
            var bs = new PerpBook();
            double pool = Tuning.PerpDepth(1);
            Check(Math.Abs(bs.Exec(100, 2000, true, 1) - 100 * (1 + 2000 / (Tuning.PerpDepthMult * pool))) < 1e-9 && bs.Exec(100, 2000, false, 1) < 100 && bs.Exec(100, 1e12, true, 1) <= 150.0001, "滑点：买入上滑、卖出下滑，封顶 50%");
            // 滑点吃掉了强平线之间的空间：开仓被拒
            var bt = new PerpBook { DepthMult = 0.05 }; var at = NewAcct(5000);
            Check(bt.Open(at, "PSC", true, 1000, 5, 100, 1) == PerpResult.Thin && bt.Positions.Count == 0 && at.Get(Assets.CRC) == 5000, "滑点大到一开就被强平的单子被拒，不扣钱");
            // 开仓与手续费、保证金（Poinbase 没有污点）
            var b = new PerpBook { DepthMult = 1e12 }; var a = NewAcct(2000);
            Check(b.Open(a, "PSC", true, 1000, 6, 100, 1) == PerpResult.Leverage && b.Open(a, "PSC", true, 3000, 3, 100, 1) == PerpResult.Funds, "杠杆超限、余额不足被拒");
            Check(b.Open(a, "PSC", true, 1000, 5, 100, 1) == PerpResult.Ok, "开 5× 多头");
            var pos = b.Of("PSC");
            double fee = 5000 * Tuning.PerpFee;
            Check(Math.Abs(pos.Size - 50) < 1e-9 && Math.Abs(a.Get(Assets.CRC) - (1000 - fee)) < 1e-9, $"名义 5000，手续费 {fee}");
            Check(b.Open(a, "PSC", false, 100, 2, 100, 1) == PerpResult.Opposite, "不能同时多空");
            Check(b.Open(a, "PSC", true, 100, 2, 100, 1) == PerpResult.Single && b.Open(a, "SPARK", true, 100, 1, 10, 1) == PerpResult.Single && b.Positions.Count == 1, "只有一个仓位：加仓、开别的标的都被拒，只能追加保证金");
            Check(Math.Abs(pos.LiqPrice - (50 * 100 - 1000) / (50 * (1 - Tuning.PerpMaint))) < 1e-9 && pos.LiqPrice > 85 && pos.LiqPrice < 90, $"强平价 {pos.LiqPrice:0.0}（约 −11%）");
            // 平仓盈利
            double paid = b.Close(a, "PSC", 110, 2, out double d0);
            double expect = 1000 + 50 * 10 - 50 * 110 * Tuning.PerpFee;
            Check(Math.Abs(paid - expect) < 1e-6 && d0 == 0 && b.Positions.Count == 0, $"盈利平仓 {paid:0.00}");
            // 平仓亏损
            var a2 = NewAcct(2000); var b2 = new PerpBook { DepthMult = 1e12 };
            b2.Open(a2, "PSC", true, 1000, 5, 100, 1);
            double paid2 = b2.Close(a2, "PSC", 95, 2, out _);
            Check(paid2 > 700 && paid2 < 760, $"亏损平仓 {paid2:0.0}");
            // 夜间强平：权益不到 10% 名义价值；剩余进基金
            var b3 = new PerpBook { DepthMult = 1e12 }; var a3 = NewAcct(5000);
            b3.Open(a3, "PSC", true, 1000, 5, 100, 1);
            double fund0 = b3.InsuranceFund;
            var ev = b3.Night(2, new Dictionary<string, double> { ["PSC"] = 88 }, a3);
            Check(ev.Any(e => e.Kind == "liq") && b3.Positions.Count == 0 && b3.Debt == 0, "−12% 被强平，没有欠款");
            Check(b3.InsuranceFund > fund0, "剩余权益进保险基金");
            var b3b = new PerpBook { DepthMult = 1e12 }; var a3b = NewAcct(5000);
            b3b.Open(a3b, "PSC", true, 1000, 5, 100, 1);
            Check(!b3b.Night(2, new Dictionary<string, double> { ["PSC"] = 95 }, a3b).Any(e => e.Kind == "liq") && b3b.Positions.Count == 1, "−5% 不强平");
            // 穿仓：基金先垫，不够记欠款；有欠款不能开仓；账户 CRC 每晚先还
            var b4 = new PerpBook { DepthMult = 1e12, InsuranceFund = 100 }; var a4 = NewAcct(1100);
            b4.Open(a4, "SPARK", true, 1000, 3, 10, 1);
            double crc4 = a4.Get(Assets.CRC);
            b4.Night(2, new Dictionary<string, double> { ["SPARK"] = 5 }, a4);
            Check(b4.Positions.Count == 0 && b4.Debt > 0 && b4.InsuranceFund < 1, $"大跌穿仓，基金见底，欠款 {b4.Debt:0}");
            Check(b4.Open(a4, "SPARK", true, 10, 1, 5, 2) == PerpResult.Debt, "有欠款不能开仓");
            double debt0 = b4.Debt; a4.Add(Assets.CRC, 100);
            b4.Night(3, new Dictionary<string, double> { ["SPARK"] = 5 }, a4);
            Check(Math.Abs(b4.Debt - (debt0 * (1 + Tuning.PerpDebtInterest) - (crc4 + 100))) < 1e-6 || b4.Debt < debt0 * (1 + Tuning.PerpDebtInterest), "欠款计息，账户 CRC 先还");
            // 逾期没收、核销、拉黑
            var b5 = new PerpBook { DepthMult = 1e12, Debt = 500, DebtSince = 1 }; var a5 = NewAcct(0);
            Check(!b5.Night(5, new Dictionary<string, double> { ["PSC"] = 100 }, a5).Any(e => e.Kind == "seize"), "没到 7 天不没收");
            Check(b5.Night(8, new Dictionary<string, double> { ["PSC"] = 100 }, a5).Any(e => e.Kind == "seize"), "逾期触发没收");
            double wo = b5.ApplySeizure(200, 8);
            Check(wo > 300 && b5.Debt == 0 && b5.Banned(20) && !b5.Banned(8 + Tuning.PerpBanDays + 1), $"没收抵掉 200，核销 {wo:0}，拉黑 {Tuning.PerpBanDays} 天");
            // 资金费：价格高于 7 日均价时多头付、空头收
            var b6 = new PerpBook { DepthMult = 1e12 }; var a6 = NewAcct(5000);
            for (int d = 1; d <= 5; d++) b6.Night(d, new Dictionary<string, double> { ["PSC"] = 100 }, a6);
            b6.Open(a6, "PSC", true, 1000, 2, 100, 5);
            double m0 = b6.Of("PSC").Margin;
            b6.Night(6, new Dictionary<string, double> { ["PSC"] = 106 }, a6);
            Check(b6.Rate("PSC") > 0 && b6.Of("PSC").Margin < m0, $"溢价时多头付资金费 {b6.Rate("PSC"):P2}");
            // 自动减仓：NPC 大行情穿仓抽干基金，盈利的空头只拿到一部分
            var b7 = new PerpBook { DepthMult = 1e12, InsuranceFund = 500 }; var a7 = NewAcct(5000);
            b7.Open(a7, "SPARK", false, 1000, 3, 10, 1);
            b7.Night(2, new Dictionary<string, double> { ["SPARK"] = 10 }, a7);
            var ev7 = b7.Night(3, new Dictionary<string, double> { ["SPARK"] = 3 }, a7);
            Check(ev7.Any(e => e.Kind == "adl") && b7.Positions.Count == 0, "基金见底，盈利空头被自动减仓");
            // NPC 爆仓：小行情爆仓的剩余保证金进基金，大行情穿仓吃基金；涨跌达到新闻线记一条 wipe（含玩家）
            double oi = PerpBook.NpcOi("PSC", 30);
            Check(PerpBook.NpcFlow(0.02, oi, out double l2) > 0 && l2 > 0 && PerpBook.NpcFlow(-0.04, oi, out _) > 0, $"2%–4% 的行情基金净流入 {PerpBook.NpcFlow(0.02, oi, out _):0}");
            Check(PerpBook.NpcFlow(0.25, oi, out double l25) < 0 && Math.Abs(l25 - oi) < 1e-6, $"25% 的行情亏损一侧全部爆仓、基金净流出 {PerpBook.NpcFlow(0.25, oi, out _):0}");
            Check(-PerpBook.NpcFlow(0.25, oi, out _) < Tuning.PerpFund0 / 5, "一次 25% 的行情吃不掉基金初值的五分之一");
            var b8 = new PerpBook { DepthMult = 1e12 }; var a8 = NewAcct(5000);
            b8.Night(1, new Dictionary<string, double> { ["PSC"] = 100 }, a8);
            b8.Open(a8, "PSC", true, 1000, 5, 100, 1);
            var ev8 = b8.Night(2, new Dictionary<string, double> { ["PSC"] = 85 }, a8);
            var wipe = ev8.FirstOrDefault(e => e.Kind == "wipe");
            PerpBook.NpcFlow(-0.15, PerpBook.NpcOi("PSC", 2), out double npc8);
            Check(wipe != null && wipe.Long && wipe.Count == (int)Math.Round(npc8 / Tuning.PerpNpcAvgPos) + 1 && wipe.Amount > npc8, $"−15%：多头爆仓 {wipe?.Count} 人（含玩家）");
            Check(!b8.Night(3, new Dictionary<string, double> { ["PSC"] = 87 }, a8).Any(e => e.Kind == "wipe"), "小行情不上新闻");
        });

        Test("CRA 质押池：计息、解锁期、脏枚数随币、螺旋", () =>
        {
            Func<string, double> price = a => a == Assets.PSC ? 100 : 1;
            var w = new Wallet { Address = "0xa" }; w.AddDirty(Assets.CRA, 1000, 400); w.Add(Assets.PSC, 10);
            var st = new Staking();
            Check(!st.Stake(w, 2000), "余额不足不能质押");
            Check(st.Stake(w, 500) && Math.Abs(w.Get(Assets.CRA) - 500) < 1e-9, "质押 500，钱包剩 500");
            double lotTaint = st.Of("0xa").Dirty;
            Check(Math.Abs(lotTaint - 200) < 1e-6 && Math.Abs(w.DirtyOf(Assets.CRA) - 200) < 1e-6, $"CRA 40% 脏，质押 500 带走 200 枚脏的：{lotTaint}");
            var e1 = st.Night(1, Tuning.CraRate, _ => w);
            Check(Math.Abs(st.Of("0xa").Amount - 500 * (1 + Tuning.CraRate)) < 1e-9 && e1.Count == 1, "每晚计息并入本金");
            Check(!st.RequestUnstake("0xa", 9999, 1) && st.RequestUnstake("0xa", 200, 1), "申请取出 200");
            double a0 = st.Of("0xa").Amount;
            st.Night(2, Tuning.CraRate, _ => w);
            Check(Math.Abs(st.Of("0xa").Amount - a0 * (1 + Tuning.CraRate)) < 1e-9, "排队的部分不计息");
            Check(w.Get(Assets.CRA) < 501, "解锁期没到不能取");
            for (int d = 3; d <= 1 + Tuning.CraUnlockDays; d++) st.Night(d, Tuning.CraRate, _ => w);
            Check(Math.Abs(w.Get(Assets.CRA) - 700) < 1e-6, $"到期自动回钱包 200：{w.Get(Assets.CRA):0.##}");
            Check(w.DirtyOf(Assets.CRA) > 200 + 1 && w.DirtyOf(Assets.CRA) < 200 + 80 + 1e-6, $"脏枚数按比例回来（利息干净）：{w.DirtyOf(Assets.CRA):0.##}");
            // 螺旋：质押的 CRA 取回后只值当时的价格
            var s = new Stablecoins(); s.CraCrashDay = 5;
            for (int d = 5; d <= 7; d++) s.Night(d, new Rng(1), 0);
            Check(s.Cra < 0.05 && s.Spark < Tuning.SparkStart * 0.1, $"螺旋后 CRA {s.Cra}，SPARK {s.Spark:0.00}");
        });

        Test("SPARK：随机游走有界，铸销事件冲击", () =>
        {
            double lo = 99, hi = 0;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var s = new Stablecoins(); var rng = new Rng(seed);
                for (int d = 1; d <= 150 && s.CraWarnDay < 0; d++) { s.Night(d, rng, 0); lo = Math.Min(lo, s.Spark); hi = Math.Max(hi, s.Spark); }   // 螺旋前
            }
            Check(lo >= Tuning.SparkStart * Tuning.SparkMinMult - 1e-9 && hi <= Tuning.SparkStart * Tuning.SparkMaxMult + 1e-9 && hi / lo > 1.5, $"螺旋前的范围 {lo:0.0}–{hi:0.0}");
            var s2 = new Stablecoins();
            double before = s2.Spark; s2.OnEventStart(MarketEvent.RevRaidUpper, 1, new Rng(1));
            Check(Math.Abs(s2.Spark - before * Tuning.SparkRaidShock) < 1e-9, "袭击上层 +15%");
        });

        Test("相似地址、手续费、确认数", () =>
        {
            var rng = new Rng(21);
            int sameShort = 0;
            for (int i = 0; i < 300; i++)
            {
                string a = ChainAddress.Make(rng.NextUInt());
                string f = ChainAddress.Lookalike(a, rng);
                Check(ChainAddress.Valid(f) && !ChainAddress.Same(a, f) && f.Substring(0, 5) == a.Substring(0, 5), $"相似地址合法、不同、前缀相同：{a} / {f}");
                if (ChainAddress.Short(a) == ChainAddress.Short(f)) sameShort++;
            }
            Check(sameShort > 290, $"缩写看起来一样：{sameShort}/300");
            Check(TxRules.FeeCredits(FeeTier.Slow, false) < TxRules.FeeCredits(FeeTier.Normal, false) && TxRules.FeeCredits(FeeTier.Normal, false) < TxRules.FeeCredits(FeeTier.Fast, false), "慢 < 普通 < 快");
            Check(Math.Abs(TxRules.FeeCredits(FeeTier.Fast, true) - Tuning.TxFeeFast * Tuning.TxCongestedFeeMult) < 1e-9, "拥堵时手续费翻倍");
            Check(TxRules.Slowdown(FeeTier.Fast, false) < TxRules.Slowdown(FeeTier.Normal, false) && TxRules.Slowdown(FeeTier.Normal, false) < TxRules.Slowdown(FeeTier.Slow, false) && Near(TxRules.Slowdown(FeeTier.Normal, false), 1, 1e-9) && TxRules.Slowdown(FeeTier.Fast, true) < TxRules.Slowdown(FeeTier.Normal, true), "Gas 越高确认越快，拥堵时快档仍最快");
            Check(TxRules.Confirmations(100) == 3 && TxRules.Confirmations(5000) == 6 && TxRules.Confirmations(50000) == 12, "大额要更多确认");
            Check(TxRules.DropChance(FeeTier.Slow, true) > 0 && TxRules.DropChance(FeeTier.Slow, false) == 0 && TxRules.DropChance(FeeTier.Fast, true) == 0, "只有拥堵时的慢档会被丢");
            double lo = 9, hi = 0;
            for (int dd = 0; dd < 7; dd++) { double g = TxRules.GasCycle(dd, 2); lo = Math.Min(lo, g); hi = Math.Max(hi, g); }
            Check(Math.Abs(TxRules.GasCycle(3, 2) - TxRules.GasCycle(10, 2)) < 1e-9 && lo < 0.6 && hi > 1.4, $"gas 7 天一个周期，0.5×–1.5×：{lo:0.00}–{hi:0.00}");
            var gr = new Rng(4); bool inRange = true;
            for (int dd = 0; dd < 200; dd++) { double g = TxRules.Gas(dd, 0, gr); if (g < 0.5 * 0.85 - 1e-9 || g > 1.5 * 1.15 + 1e-9) inRange = false; }
            Check(inRange, "加上随机波动后仍在 0.425–1.725");
            Check(Math.Abs(TxRules.FeeCredits(FeeTier.Normal, false, 1.5) - Tuning.TxFeeNormal * 1.5) < 1e-9, "手续费乘 gas");
            var h = new List<TxRecord>();
            for (int i = 0; i < 30; i++) TxRules.Record(h, new TxRecord { Day = i });
            Check(h.Count == TxRules.HistoryMax && h[0].Day == 29, "记录最新在前、有上限");
        });

        Test("被投毒的钱包：门槛以下不转，越多越快，转空", () =>
        {
            Check(Wallet.DrainChance(Tuning.WalletDrainMin - 1) == 0, "门槛以下不会被转走");
            Check(Math.Abs(Wallet.DrainChance(Tuning.WalletDrainMin) - 1 / 7.0) < 1e-9, "刚到门槛：每晚 1/7（平均 7 天）");
            Check(Wallet.DrainChance(Tuning.WalletDrainFull) == 1 && Wallet.DrainChance(Tuning.WalletDrainFull * 3) == 1, "到满额当晚就转走");
            Check(Wallet.DrainChance(10000) > Wallet.DrainChance(5000), "钱越多越快");
            double mid = (Tuning.WalletDrainMin + Tuning.WalletDrainFull) / 2;
            Check(Math.Abs(Wallet.DrainDays(mid) - 4) < 1e-9 && Math.Abs(Wallet.DrainChance(mid) - 0.25) < 1e-9, "中间值：期望 4 天，每晚 25%");
            var rng = new Rng(9); double sum = 0; int n = 4000;
            for (int i = 0; i < n; i++) { int d = 1; while (!rng.Chance(Wallet.DrainChance(mid))) d++; sum += d; }
            Check(Math.Abs(sum / n - 4) < 0.25, $"模拟平均 {sum / n:0.00} 天被转走（期望 4）");
            var w = new Wallet { Address = "a" };
            w.Add(Assets.PSC, 50); w.AddDirty(Assets.CRT, 1000, 300); w.Freeze(Assets.CRT, 0.5, 9);
            double v = w.Drain(a => a == Assets.PSC ? 100 : 1);
            Check(Math.Abs(v - 6000) < 1e-6 && w.Balances.Count == 0 && w.Frozen.Count == 0 && w.Dirty.Count == 0, $"全部转走（含冻结），脏枚数清零：{v}");
        });

        Test("多钱包：退款、出售回款、混币提回都回到原来的钱包", () =>
        {
            Func<string, double> price = a => a == Assets.PSC ? 100 : 1;
            var a = new Wallet { Address = "0xa" }; var b = new Wallet { Address = "0xb" };
            a.Add(Assets.CRT, 5000); b.Add(Assets.CRT, 5000);
            Func<string, Wallet> pick = addr => addr == "0xa" ? a : b;
            var mx = new Mixer();
            var dep = mx.Deposit(a, Assets.CRT, 1000, 1, 1, price);
            Check(dep.Wallet == "0xa", "混币存入记下钱包地址");
            mx.Night(pick, 2, price, new Rng(3));
            Check(mx.Pending.Count == 0 ? a.Get(Assets.CRT) > 4900 && b.Get(Assets.CRT) == 5000 : true, $"混币提回 A：A {a.Get(Assets.CRT):0}，B {b.Get(Assets.CRT):0}");
            var bm = new BlackMarket();
            bm.RefreshOrders(5, 80, new Rng(11), _ => 300);
            var seller = bm.Sellers.FindIndex(x => !x.Scam);
            bm.Sellers[seller].Scam = true;
            var l = bm.Catalog.First(x => x.Seller == seller);
            double bBefore = b.Get(Assets.CRT);
            var o = bm.Place(l, 1, PayMode.Escrow, b, Assets.CRT, 5, price);
            Check(o.Wallet == "0xb" && b.Get(Assets.CRT) < bBefore, "订单记下付款钱包");
            double aBefore = a.Get(Assets.CRT);
            for (int d = 6; d < 30 && o.State == OrderState.Pending; d++) bm.Night(d, false, pick, new Rng((ulong)d), 0, price);
            Check(o.State == OrderState.Refunded && Math.Abs(b.Get(Assets.CRT) - bBefore) < 1e-6 && a.Get(Assets.CRT) == aBefore, $"骗子托管退款回 B：{o.State}");
        });

        Test("Poinbase 现货：订单簿冲击曲线、按标的数量反算要付的计价币", () =>
        {
            var m = Market.CreateDefault(38);
            var p = m.Main;
            Check(p.Book, "Poinbase 主池是订单簿");
            double q = p.QuoteIn(2);
            Check(Near(p.BaseOut(q), 2, 1e-6), $"买 2 PSC 要付 {q:0.00} CRC，反算回来是 2 个");
            double impact = q * (1 - p.Fee) / 2 / p.Price - 1;
            Check(impact > 0.0008 && impact < 0.0012, $"第 38 天买 2 PSC 的滑点（不含手续费）约 0.1%：{impact * 100:0.000}%");
            double sell = 1 - p.QuoteOut(200) / (1 - p.Fee) / 200 / p.Price;
            Check(sell > 0.035 && sell < 0.045, $"第 38 天卖 200 PSC 的滑点约 4%：{sell * 100:0.00}%");
            double p0 = p.Price, paid = p.QuoteIn(50);
            double got = p.BuyBase(paid), back = p.SellBase(got);
            Check(Near(got, 50, 1e-6) && Near(p.Price, p0, 1e-9) && back < paid && Near(back, paid * (1 - p.Fee) * (1 - p.Fee), paid * 1e-6), $"买进再卖出：价格回到原值，只损失两次手续费（付 {paid:0.00}，回 {back:0.00}）");
            var one = Market.CreateDefault(38).Main; var split = Market.CreateDefault(38).Main;
            double a1 = one.QuoteIn(100), a2 = 0;
            for (int k = 0; k < 10; k++) { double c = split.QuoteIn(10); split.BuyBase(c); a2 += c; }
            Check(Near(a1, a2, a1 * 1e-6), $"拆单不占便宜：一次买 100 付 {a1:0.00}，拆十次付 {a2:0.00}");
            var dex = m.DexPool(Assets.PSC);
            Check(double.IsPositiveInfinity(dex.QuoteIn(dex.BaseReserve)) && dex.QuoteIn(0) == 0, "DEX 恒定乘积池：超出储备为无穷，0 个为 0");
        });

        Test("稳定币池（StableSwap）：挂钩附近几乎没有滑点，脱锚变差，崩盘被抽干", () =>
        {
            var m = Market.CreateDefault(38);
            var crt = m.DexPool(Assets.CRT);
            Check(crt.Stable && m.DexPool(Assets.CRA).Stable && !m.DexPool(Assets.PSC).Stable, "CRT、CRA 对 CRC 是稳定币池，PSC 不是");
            Check(Near(crt.Price, 1, 1e-9), "平衡时价格等于挂钩价");
            double small = m.Quote(Assets.CRT, Assets.CRC, 998).Impact, big = m.Quote(Assets.CRT, Assets.CRC, 50000).Impact;
            Check(small < Tuning.StableFee + 0.0002 && big < 0.002, $"998 CRT 损耗 {small * 100:0.000}%，5 万 {big * 100:0.000}%");
            double got = crt.SellBase(998), back = crt.BuyBase(got);
            Check(back < 998 && back > 998 * (1 - 2 * Tuning.StableFee) - 0.01 && Near(crt.Price, 1, 1e-6), $"来回成交只损失两次手续费：{back:0.000}");
            m.Night(new MarketEvent[0], new Rng(1), 38, a => a == Assets.CRT ? 0.92 : a == Assets.CRA ? 0.30 : a == Assets.SPARK ? 10 : 1);
            Check(m.DexPool(Assets.CRT).Amp == Tuning.StableAmpDepeg && m.DexPool(Assets.CRA).Amp == Tuning.StableAmpBroken, "偏离 8% 是脱锚，偏离 70% 是崩盘");
            double dep = m.Quote(Assets.CRT, Assets.CRC, 50000).Impact, brk = m.Quote(Assets.CRA, Assets.CRC, 50000).Impact;
            Check(dep > big && brk > dep * 5, $"5 万的损耗：脱锚 {dep * 100:0.00}%，崩盘 {brk * 100:0.00}%");
            // 旧存档的恒定乘积池按当前价换成稳定币池
            var old = Market.CreateDefault(38);
            int i = old.Pools.FindIndex(x => x.Venue == Venue.Dex && x.Base == Assets.CRT);
            old.Pools[i] = Pool.Create(Venue.Dex, Assets.CRT, Assets.CRC, 0.95, 1000, Tuning.DexFee);
            old.UpgradeDexPools(38);
            Check(old.DexPool(Assets.CRT).Stable && Near(old.DexPool(Assets.CRT).Price, 0.95, 1e-9), "旧存档的池子升级后保持当前价");
        });

        Test("Poinbase 上架的币：PSC、CRC、SPARK；SPARK/CRC 订单簿", () =>
        {
            Check(Exchange.Listed.SequenceEqual(new[] { Assets.PSC, Assets.CRC, Assets.SPARK }), "只收 PSC、CRC、SPARK");
            var m = Market.CreateDefault(38);
            var sp = m.Find(Venue.Exchange, Assets.SPARK, Assets.CRC);
            Check(sp != null && sp.Book && Near(sp.Price, Tuning.SparkStart, 1e-9), "SPARK/CRC 是订单簿，起始价同 SPARK");
            m.Pools.Remove(sp); m.AddPoinSpark(12, 38); m.AddPoinSpark(13, 38);
            Check(m.Pools.Count(x => x.Venue == Venue.Exchange && x.Base == Assets.SPARK) == 1 && Near(m.Find(Venue.Exchange, Assets.SPARK, Assets.CRC).Price, 12, 1e-9), "旧存档补上一次，按当前价");
            m.Night(new MarketEvent[0], new Rng(1), 38, a => a == Assets.SPARK ? 20 : 1);
            double ps = m.Find(Venue.Exchange, Assets.SPARK, Assets.CRC).Price;
            Check(ps > 12 && ps < 20, $"夜里向 SPARK 参考价靠拢：{ps:0.00}");
        });

        Test("换币客户：卖币的事件币种、污点比例与折扣、收下后的污点；买币的随机出价；部分成交；成交统计", () =>
        {
            Func<string, double> pr = a => a == Assets.Ponero ? 50 : 1;
            var none = new List<MarketEvent>();
            var rng = new Rng(3);
            int ev = 0, sides = 0;
            for (int i = 0; i < 4000; i++)
            {
                var o = Otc.MakeSell(new List<MarketEvent> { MarketEvent.GangWar }, false, pr, 500, rng);
                if (o.Asset == Assets.CRT && o.TaintRatio == 1) ev++;
                if (Otc.PickSide(rng) == OtcSide.Sell) sides++;
            }
            Check(Math.Abs(ev / 4000.0 - Tuning.OtcEventChance) < 0.03, $"帮派火并期间多是 CRT、全额污点：{ev}/4000");
            Check(Math.Abs(sides / 4000.0 - Tuning.OtcSellShare) < 0.03, $"卖币、买币各占一半：{sides}/4000");
            Check(Otc.EventAsset(new List<MarketEvent> { MarketEvent.RevRaidUpper }, new Rng(1)) == Assets.PSC && Otc.EventAsset(none, new Rng(1)) == null, "革命军袭击上层是 PSC；没有事件时没有事件币");
            // 平时：PSC/CRT/CRA 随机，污点比例在区间内，越脏越便宜
            for (int i = 0; i < 200; i++)
            {
                var o = Otc.MakeSell(none, false, pr, 500, rng);
                Check(Array.IndexOf(Otc.SellAssets, o.Asset) >= 0 && o.TaintRatio >= Tuning.OtcTaintMin && o.TaintRatio <= Tuning.OtcTaintMax, "平时的币种与污点比例");
                Check(o.Value >= 500 * Tuning.OtcValueMinRents && o.Value <= 500 * Tuning.OtcValueMaxRents && Math.Abs(o.Cash - o.Value * Otc.CutFor(o.TaintRatio)) <= 1, $"市值与现金价：{o.Value:0} / {o.Cash}");
            }
            Check(Otc.CutFor(0) == Tuning.OtcCutClean && Otc.CutFor(1) == Tuning.OtcCutDirty && Otc.CutFor(0.3) > Otc.CutFor(0.7), "折扣按污点比例线性降低");
            // 收下：币进钱包，其中 数量 × 污点比例 枚是脏的
            var oc = new OtcOffer { Side = OtcSide.Sell, Asset = Assets.CRT, Qty = 1000, Value = 1000, TaintRatio = 0.4, Cash = 700 };
            var w = new Wallet();
            Otc.Receive(w, oc);
            Check(Near(w.Get(Assets.CRT), 1000, 1e-9) && Near(w.DirtyOf(Assets.CRT), 400, 1e-9), "1000 CRT 里 400 枚脏");
            // Ponero：兑换处开放后偶尔有，不带污点
            int pon = 0;
            for (int i = 0; i < 4000; i++) { var o = Otc.MakeSell(none, true, pr, 500, rng); if (o.Asset == Assets.Ponero) { pon++; Check(o.TaintRatio == 0 && o.Cash >= o.Value * Tuning.OtcPoneroCutMin - 1, "Ponero 不带污点"); } }
            Check(Math.Abs(pon / 4000.0 - Tuning.OtcPoneroChance) < 0.02, $"兑换处开放后的 Ponero：{pon}/4000");
            int stings = 0, stingsCd = 0;
            for (int i = 0; i < 4000; i++)
            {
                if (Otc.MakeSell(none, false, pr, 500, rng).Sting) stings++;
                if (Otc.MakeSell(new List<MarketEvent> { MarketEvent.Crackdown }, false, pr, 500, rng).Sting) stingsCd++;
            }
            Check(stingsCd > stings * 2, $"镇压期间便衣更多：{stings} → {stingsCd}");
            // 买币：市值按房租，出价 = 市值 × (1 + 随机溢价)，溢价在区间内
            for (int i = 0; i < 200; i++)
            {
                var ob = Otc.MakeBuy(pr, 500, rng);
                Check(Array.IndexOf(Otc.BuyAssets, ob.Asset) >= 0 && ob.Value >= 500 * Tuning.OtcBuyMinRents && ob.Value <= 500 * Tuning.OtcBuyMaxRents && Near(ob.Qty * pr(ob.Asset), ob.Value, 1e-6), "买币的币种与数量");
                Check(ob.Cash >= ob.Value * (1 + Tuning.OtcBidMin) - 1 && ob.Cash <= ob.Value * (1 + Tuning.OtcBidMax) + 1, $"出价在区间内：{ob.Cash} / {ob.Value:0}");
            }
            // 部分成交：现金、市值按数量比例折算，超过报价按全部算
            var whole = new OtcOffer { Side = OtcSide.Sell, Asset = Assets.PSC, Qty = 20, Value = 2000, TaintRatio = 0.5, Cash = 1200, Sting = true };
            var half = Otc.Part(whole, 10);
            Check(Near(half.Qty, 10, 1e-9) && Near(half.Value, 1000, 1e-9) && half.Cash == 600 && half.TaintRatio == 0.5 && half.Sting, "收一半：现金、市值减半，污点比例和便衣不变");
            Check(Near(Otc.Part(whole, 50).Qty, 20, 1e-9) && Otc.Part(whole, 50).Cash == 1200, "超过报价的数量按全部算");
            Check(Otc.Part(whole, 0) == null && Otc.Part(whole, -1) == null, "数量不合法");
            // 来历：脏的来源按污点比例出现，数量占比合计为 1
            var hist = Otc.History(new OtcOffer { TaintRatio = 1 }, new[] { "dirty" }, new[] { "clean" }, new Rng(2));
            Check(hist.Count >= 1 && hist.Count <= Tuning.OtcHistoryMax && hist.TrueForAll(kv => kv.Key == "dirty") && Near(hist.Sum(kv => kv.Value), 1, 1e-9), "全脏的币来历都是脏来源");
            var histC = Otc.History(new OtcOffer { TaintRatio = 0 }, new[] { "dirty" }, new[] { "clean" }, new Rng(2));
            Check(histC.TrueForAll(kv => kv.Key == "clean"), "干净的币来历都是干净来源");
            var desk = new OtcDesk(); desk.Record(700); desk.Record(300);
            Check(desk.Volume == 1000 && desk.Deals == 2, "成交统计");
        });

        Test("自营混币器：注资取回的污点、匿名集与清洗率、客流、出款、查封、卷款跑路", () =>
        {
            Func<string, double> pr = a => 1;
            var w = new Wallet { Address = "w" }; w.Add(Assets.CRT, 30000);
            var m = new OwnMixer();
            Check(m.Inject(w, Assets.CRT, 20000) && m.Pool(Assets.CRT).Capital == 20000 && m.Pool(Assets.CRT).CapDirty == 0, "干净的币注资，本金不带污点");
            Check(!m.Inject(w, Assets.CRT, 1e6) && !m.Inject(w, Assets.SPARK, 1), "余额不够、币种不支持被拒");
            Check(OwnMixer.RoundRate(40000) > OwnMixer.RoundRate(10000) && OwnMixer.RoundRate(1e9) <= Tuning.MixerCleanRate + 1e-9 && OwnMixer.RoundRate(0) == 0, "匿名集越大清洗率越高，封顶基准清洗率");
            m.Start(pr);
            Check(m.Open && Near(m.Rating, OwnMixer.Remain(OwnMixer.RoundRate(20000)), 1e-9), "开张时口碑按匿名集估");
            double rr = 0.29;
            var cheap = new OwnMixer { Fee = 0.01, Rating = 0.3 }; var dear = new OwnMixer { Fee = 0.08, Rating = 0.3 }; var dirty = new OwnMixer { Fee = 0.01, Rating = 0.9 };
            Check(cheap.Share(0.03, rr, true) > dear.Share(0.03, rr, true) && cheap.Share(0.03, rr, true) > dirty.Share(0.03, rr, true), "费率低、出款干净的分到的客流多");
            Check(cheap.Share(0.03, rr, false) > cheap.Share(0.03, rr, true), "对手关停时客流更多");
            // 营业几晚：有存入、有手续费，到期的出款
            var rng = new Rng(4);
            double fees0 = m.FeesTotal;
            int deposits = 0, payouts = 0;
            for (int day = 40; day < 50; day++)
                foreach (var e in m.Night(day, pr, 60, Tuning.MixerFee, rr, true, 1, false, rng))
                {
                    if (e.Kind == "deposit") deposits++;
                    if (e.Kind == "payout") payouts++;
                    if (e.Kind == "seized") Check(m.Pools.Count == 0 && m.Pending.Count == 0, "查封清空池子");
                }
            Check(deposits > 0 && payouts > 0 && m.FeesTotal > fees0, $"营业中有存入和出款：{deposits} / {payouts}，手续费 {m.FeesTotal - fees0:0}");
            Check(m.Pending.All(x => x.ReadyDay >= 50), "到期的都出款了");
            // 取回：收入整笔是脏的，本金按本金的脏比例
            var m2 = new OwnMixer(); var w2 = new Wallet { Address = "w2" }; w2.AddDirty(Assets.PSC, 1000, 500);
            m2.Inject(w2, Assets.PSC, 1000);
            Check(Near(m2.Pool(Assets.PSC).CapDirty, 500, 1e-9) && w2.DirtyOf(Assets.PSC) < 1e-9, "一半脏的币注资，脏枚数跟着进本金");
            m2.Pool(Assets.PSC).Earned = 100;
            var w3 = new Wallet { Address = "w3" }; w3.Add(Assets.CRC, 10000);
            Check(m2.Withdraw(w3, Assets.PSC, 100) && Near(w3.DirtyOf(Assets.PSC), 100, 1e-9) && w3.DirtyOf(Assets.CRC) == 0, "先取收入：整笔脏，别的币不受影响");
            Check(m2.Withdraw(w3, Assets.PSC, 500) && Near(w3.DirtyOf(Assets.PSC), 100 + 250, 1e-9), "再取本金：按本金的脏比例");
            Check(!m2.Withdraw(w3, Assets.PSC, 1000), "不能超过可取回的数量");
            m2.Pending.Add(new OwnMixDeposit { Asset = Assets.PSC, Amount = 300, Taint = 200, Rounds = 2, ReadyDay = 99 });
            Check(Near(m2.Free(Assets.PSC), 500, 1e-9) && Near(m2.Total(Assets.PSC), 800, 1e-9), "客户在途的币不能取");
            // 卷款跑路：连同客户的币全部卷走、整笔脏，永久关闭
            var w4 = new Wallet { Address = "w4" };
            var got = m2.Rug(w4);
            Check(Near(w4.Get(Assets.PSC), 800, 1e-9) && Near(w4.DirtyOf(Assets.PSC), 800, 1e-9) && m2.Closed && !m2.Open && got.Count == 1, "卷走全部、整笔脏、关闭");
            Check(m2.Night(100, pr, 60, Tuning.MixerFee, rr, true, 1, false, new Rng(1)).Count == 0, "关闭后不再有夜间事件");
            // 查封：风声足够大时被查封，停业若干天
            OwnMixer m3 = null; List<OwnMixEvent> ev = null;
            for (ulong seed = 1; seed <= 64 && (ev == null || !ev.Any(e => e.Kind == "seized")); seed++)   // 查封概率封顶 50%，换种子直到触发
            {
                m3 = new OwnMixer { Heat = 1e6 }; m3.Inject(new Wallet { Address = "x", Balances = { [Assets.CRT] = 5000 } }, Assets.CRT, 5000); m3.Start(pr);
                ev = m3.Night(60, pr, 60, Tuning.MixerFee, rr, true, 1, false, new Rng(seed));
            }
            Check(ev.Any(e => e.Kind == "seized" && e.Crime > 0 && e.Crime <= Tuning.CrimeEventCap) && m3.Seized(60 + Tuning.OwnMixSeizeDays) && !m3.Seized(61 + Tuning.OwnMixSeizeDays) && !m3.Open, "风声大时被查封、记罪证、停业");
        });

        Test("预测市场：LMSR 定价、最大亏损、买卖", () =>
        {
            // 流动性：第 38 天在 5% 的盘口押 50，均价比现价贵约 10%（与先验无关，50% 盘口押 50 约 0.5%）
            var lo = PredictMarket.Create(1, PredictKind.Event, "x", "t", 0, 38, 39, 40, 0.05);
            double slipLo = 50 / lo.SharesFor(true, 50) / 0.05 - 1;
            var mid = PredictMarket.Create(1, PredictKind.Event, "x", "t", 0, 38, 39, 40, 0.5);
            double slipMid = 50 / mid.SharesFor(true, 50) / 0.5 - 1;
            Check(slipLo > 0.08 && slipLo < 0.12 && slipMid < 0.01, $"押 50 的滑点：5% 盘口 {slipLo * 100:0.0}%，50% 盘口 {slipMid * 100:0.0}%");
            foreach (double p0 in new[] { 0.5, 0.08, 0.9 })
            {
                var m = PredictMarket.Create(1, PredictKind.Event, "x", "t", 0, 10, 11, 12, p0);
                Check(Math.Abs(m.PYes - p0) < 1e-9, $"初始价 = 先验 {p0}：{m.PYes:0.0000}");
                Check(Math.Abs(m.Loss - PredictMarket.LossFor(10)) < 1e-6 && Math.Abs(m.B - Tuning.PredictDepth * m.Loss / Math.Log(2)) < 1e-6, "单盘额度等于预设，流动性与先验无关");
                double cost = 100, sh = m.SharesFor(true, cost);
                Check(Math.Abs(m.BuyCost(true, sh) - cost) < 1e-6, $"闭式反解和成本函数一致（{p0}）");
                Check(Math.Abs(m.SellValue(true, 0)) < 1e-9, "卖 0 份收回 0");
                // 份额上限按额度：买满一边，系统最多亏份额上限
                double paid = m.BuyCost(true, m.ShareCap);
                Check(m.ShareCap - paid <= Tuning.PredictCap * m.Loss + 1e-6, $"买满上限系统亏 {m.ShareCap - paid:0.0}");
                // 买了再卖：只损失手续费之外的价差为 0
                var m3 = PredictMarket.Create(1, PredictKind.Event, "x", "t", 0, 10, 11, 12, p0);
                double s3 = m3.SharesFor(true, 50);
                m3.QYes += s3;
                Check(Math.Abs(m3.SellValue(true, s3) - 50) < 1e-6, "原路卖回收回成本");
            }
        });

        Test("预测市场：买入、卖出、手续费、脏枚数、持仓限制", () =>
        {
            Func<string, double> pr = a => 1;
            var acct = new Wallet { Address = "w" };
            acct.AddDirty(Assets.CRC, 1000, 400);
            var bk = new PredictBook();
            var m = PredictMarket.Create(++bk.Seq, PredictKind.Purity, "gutter", "t", 8500, 5, 6, 6, 0.5);
            bk.Markets.Add(m);
            Check(bk.Buy(acct, m, true, 0, 5, false, out _) == PredictResult.Amount, "金额为 0 被拒");
            Check(bk.Buy(acct, m, true, 2000, 5, false, out _) == PredictResult.Funds, "钱不够被拒");
            Check(bk.Buy(acct, m, true, 100, 6, false, out _) == PredictResult.Closed, "窗口开始后封盘");
            double spend = 100 * (1 + Tuning.PredictFee);
            var res = bk.Buy(acct, m, true, spend, 5, false, out double sh);
            Check(res == PredictResult.Ok && sh > 0, "买入成功");
            var pos = bk.PosOf(m.Id, acct.Address);
            Check(Math.Abs(pos.Cost - 100) < 1e-6 && Math.Abs(acct.Get(Assets.CRC) - (1000 - spend)) < 1e-6, $"成本 100（另付手续费）：{pos.Cost:0.00}");
            Check(Math.Abs(pos.Dirty - 0.4 * spend) < 1e-6 && Math.Abs(acct.DirtyOf(Assets.CRC) - (400 - 0.4 * spend)) < 1e-6, $"CRC 40% 脏，花掉的按比例带进持仓：{pos.Dirty:0.00}");
            Check(bk.Buy(acct, m, false, 10, 5, false, out _) == PredictResult.Opposite, "不能同盘反方向");
            // 卖出：价差为 0，只付手续费
            double before = acct.Get(Assets.CRC);
            Check(bk.SellAll(acct, m, 5, pr, out double got) == PredictResult.Ok && Math.Abs(got - 100 * (1 - Tuning.PredictFee)) < 1e-6, $"原价卖回只扣手续费：{got:0.00}");
            Check(Math.Abs(acct.Get(Assets.CRC) - before - got) < 1e-6 && bk.PosOf(m.Id, acct.Address) == null, "回到账户");
            Check(acct.DirtyOf(Assets.CRC) < 400 && acct.DirtyOf(Assets.CRC) > 359.2, "卖回时脏枚数按退回的比例回来");
            // 持仓上限
            var big = new Wallet { Address = "x" }; big.Add(Assets.CRC, 1e7);
            Check(bk.Buy(big, m, true, 1e6, 5, false, out _) == PredictResult.Cap, "超过份额上限被拒");
            Check(bk.SellAll(acct, m, 5, pr, out _) == PredictResult.NoPosition, "没持仓不能卖");
        });

        Test("预测市场：结算、作废、内幕记污点、观测", () =>
        {
            Func<string, double> pr = a => 1;
            var rng = new Rng(5);
            var acct = new Wallet { Address = "poinbase" };
            acct.Add(Assets.CRC, 100000);
            var bk = new PredictBook();
            // 观测：第一晚没有 Prev，不算 Started
            bk.Night(4, new[] { "oxymoreWave" }, m => null, _ => acct, pr, rng);
            Check(!bk.Started.ContainsKey(4), "第一晚没有对照，不记新出现的事件");
            var win = PredictMarket.Create(++bk.Seq, PredictKind.Event, "powerShortage", "停电", 0, 4, 5, 6, 0.2);
            var lose = PredictMarket.Create(++bk.Seq, PredictKind.Event, "blackout", "x", 0, 4, 5, 6, 0.2);
            var voided = PredictMarket.Create(++bk.Seq, PredictKind.Faction, "FACTION_SECURITY", "镇压", 0, 4, 5, 8, 0.2);
            bk.Markets.AddRange(new[] { win, lose, voided });
            double stake = 50 * (1 + Tuning.PredictFee);
            bk.Buy(acct, win, true, stake, 4, false, out double sw);
            bk.Buy(acct, lose, true, stake, 4, false, out _);
            bk.Buy(acct, voided, true, stake, 4, false, out _);
            double cash = acct.Get(Assets.CRC);
            bk.Night(5, new[] { "oxymoreWave", "powerShortage" }, bk.Happened, _ => acct, pr, rng);
            Check(win.Outcome < 0 && lose.Outcome < 0, "窗口没结束不结算");
            var evs = bk.Night(6, new[] { "oxymoreWave", "powerShortage" }, bk.Happened, _ => acct, pr, rng);
            Check(win.Outcome == -1 || bk.Markets.All(x => x.Id != win.Id), "到期的盘口移出列表");
            Check(evs.Any(e => e.Kind == "win" && e.Title == "停电" && Math.Abs(e.Amount - sw) < 1e-6), "押中兑付份数");
            Check(evs.Any(e => e.Kind == "lose" && e.Title == "x"), "押错没收");
            Check(Math.Abs(acct.Get(Assets.CRC) - cash - sw) < 1e-6, "账户只多了赢的那份");
            Check(bk.Markets.Count == 1 && bk.Markets[0].Id == voided.Id, "后面的窗口还没到");
            // 缺观测：8 号之前 7、8 的记录缺，到期作废退本金
            double c2 = acct.Get(Assets.CRC);
            var ev2 = bk.Night(9, new string[0], bk.Happened, _ => acct, pr, rng);
            Check(ev2.Any(e => e.Kind == "void") && Math.Abs(acct.Get(Assets.CRC) - c2 - 50) < 1e-6, $"缺数据作废并退本金：{acct.Get(Assets.CRC) - c2:0.00}");
            // 内幕单：照常兑付全部份数，只有内幕份额对应的兑付记成脏的 CRC
            var b2 = new PredictBook(); var a2 = new Wallet { Address = "p" }; a2.Add(Assets.CRC, 1e6);
            var mk = PredictMarket.Create(++b2.Seq, PredictKind.Event, "e", "t", 0, 4, 5, 5, 0.1); b2.Markets.Add(mk);
            b2.Buy(a2, mk, true, mk.BuyCost(true, 0.2 * mk.ShareCap) * 1.02, 4, true, out double si);
            b2.Buy(a2, mk, true, 50, 4, false, out double sn);
            double c2b = a2.Get(Assets.CRC), t2b = a2.DirtyOf(Assets.CRC);
            var e2 = b2.Night(5, new string[0], mm => true, _ => a2, pr, new Rng(1));
            Check(e2[0].Kind == "insider" && Near(e2[0].Amount, si + sn, 1e-6), $"内幕单照常结算：{e2[0].Kind}");
            Check(Near(a2.Get(Assets.CRC) - c2b, si + sn, 1e-6), "全部份数照常兑付");
            Check(Near(a2.DirtyOf(Assets.CRC) - t2b, si, 1e-6) && Near(e2[0].Taint, si, 1e-6), $"只有内幕份额的兑付记成脏的：+{a2.DirtyOf(Assets.CRC) - t2b:0.00} / {si:0.00}");
            // 不是内幕单：普通结算，不加污点
            var b3 = new PredictBook(); var a3 = new Wallet { Address = "p" }; a3.Add(Assets.CRC, 1e6);
            var m3 = PredictMarket.Create(++b3.Seq, PredictKind.Event, "e", "t", 0, 4, 5, 5, 0.1); b3.Markets.Add(m3);
            b3.Buy(a3, m3, true, 200, 4, false, out _);
            Check(b3.Night(5, new string[0], mm => true, _ => a3, pr, new Rng(1))[0].Kind == "win" && a3.Taint(pr) == 0, "非内幕单不记污点");
            // 拿脏 CRC 下的内幕单：带回的脏枚数加上内幕记的，整笔兑付都是脏的，不会多记到钱包里别的 CRC 上
            var b4 = new PredictBook(); var a4 = new Wallet { Address = "p" }; a4.AddDirty(Assets.CRC, 1e4, 1e4); a4.Add(Assets.CRC, 1e5);
            var m4 = PredictMarket.Create(++b4.Seq, PredictKind.Event, "e", "t", 0, 4, 5, 5, 0.1); b4.Markets.Add(m4);
            b4.Buy(a4, m4, true, m4.BuyCost(true, 0.1 * m4.ShareCap) * 1.02, 4, true, out double s4);
            double d4 = a4.DirtyOf(Assets.CRC), pd4 = b4.Positions[0].Dirty;
            var e4 = b4.Night(5, new string[0], mm => true, _ => a4, pr, new Rng(1));
            Check(e4[0].Kind == "insider" && Near(a4.DirtyOf(Assets.CRC) - d4, s4, 1e-6) && Near(e4[0].Taint + pd4, s4, 1e-6), $"兑付 {s4:0.00} 全部记脏：带回 {pd4:0.00} + 内幕 {e4[0].Taint:0.00}");
        });

        Test("预测市场：多钱包持仓各回各的钱包，内幕污点各记各的", () =>
        {
            Func<string, double> pr = a => 1;
            var w1 = new Wallet { Address = "0xa" }; var w2 = new Wallet { Address = "0xb" };
            w1.Add(Assets.CRC, 1e6); w2.Add(Assets.CRC, 1e6);
            Func<string, Wallet> pick = ad => ad == "0xa" ? w1 : w2;
            var bk = new PredictBook();
            var m = PredictMarket.Create(++bk.Seq, PredictKind.Event, "e", "t", 0, 4, 5, 5, 0.1); bk.Markets.Add(m);
            double half = 0.25 * m.ShareCap;
            Check(bk.Buy(w1, m, true, m.BuyCost(true, half) * 1.02, 4, true, out _) == PredictResult.Ok, "钱包 A 买入");
            Check(bk.Buy(w2, m, false, 10, 4, false, out _) == PredictResult.Opposite, "另一个钱包也不能买对面");
            Check(bk.Buy(w2, m, true, m.BuyCost(true, half) * 1.02, 4, true, out _) == PredictResult.Ok, "钱包 B 买入同一边");
            Check(bk.PosAll(m.Id).Count == 2 && bk.PosOf(m.Id, "0xa") != null && bk.PosOf(m.Id, "0xb") != null, "每个钱包各一个持仓");
            Check(bk.SellAll(w2, m, 4, pr, out double got) == PredictResult.Ok && got > 0 && bk.PosOf(m.Id, "0xb") == null && bk.PosOf(m.Id, "0xa") != null, "卖出只动自己钱包的持仓");
            Check(bk.Buy(w2, m, true, m.BuyCost(true, half) * 1.02, 4, true, out _) == PredictResult.Ok, "再买");
            // 两个钱包的内幕份额各自记在自己的钱包上
            double a0 = w1.Get(Assets.CRC), b0 = w2.Get(Assets.CRC), ta = w1.DirtyOf(Assets.CRC), tb = w2.DirtyOf(Assets.CRC);
            double sa = bk.PosOf(m.Id, "0xa").Shares, sb = bk.PosOf(m.Id, "0xb").Shares;
            double ca = bk.PosOf(m.Id, "0xa").Dirty, cb = bk.PosOf(m.Id, "0xb").Dirty;   // 买入时随资金带进持仓的脏枚数，兑付时原样回来
            var ev = bk.Night(5, new string[0], x => true, pick, pr, new Rng(1));
            Check(ev[0].Kind == "insider" && Near(w1.Get(Assets.CRC) - a0, sa, 1e-6) && Near(w2.Get(Assets.CRC) - b0, sb, 1e-6), "各自的钱包收到兑付");
            Check(Near(w1.DirtyOf(Assets.CRC) - ta, sa, 1e-6) && Near(w2.DirtyOf(Assets.CRC) - tb, sb, 1e-6) && Near(ev[0].Taint, sa - ca + sb - cb, 1e-6) && cb > 0,
                $"各记各的钱包，整笔兑付都是脏的（B 下注的 CRC 里本来有 {cb:0.00} 枚脏的，内幕只补记其余部分）");
        });

        Test("预测市场：夜间消息反应、情报、新盘口先验", () =>
        {
            var rng = new Rng(9);
            var bk = new PredictBook();
            var m = PredictMarket.Create(++bk.Seq, PredictKind.Event, "e", "t", 0, 5, 8, 9, 0.1); bk.Markets.Add(m);
            double sum = 0; int n = 400;
            for (int i = 0; i < n; i++) { m.QYes = m.QNo + m.B * Math.Log(0.1 / 0.9); bk.Drift(5, x => 0.9, rng); sum += m.PYes; }
            Check(sum / n > 0.4 && sum / n < 0.6, $"一晚把 10% 往 90% 拉近约一半：{sum / n:0.00}");
            var closing = PredictMarket.Create(++bk.Seq, PredictKind.Event, "e", "t", 0, 5, 6, 6, 0.1); bk.Markets.Add(closing);
            double before = closing.PYes; bk.Drift(5, x => 0.9, rng);
            Check(Math.Abs(closing.PYes - before) < 1e-12, "明天封盘的盘口不再反应");
            var mv = bk.Drift(5, x => 0.9, rng);
            Check(mv.Count == 1 && mv[0].Market == m && Math.Abs(mv[0].To - m.PYes) < 1e-12 && m.PrevP == mv[0].From, "反应记下前后价格");
            Check(m.PYes >= Tuning.PredictPMin && m.PYes <= Tuning.PredictPMax, "价格夹在范围内");
            // 反应是有噪声的：同一目标多次结果不全一样
            var set = new System.Collections.Generic.HashSet<double>();
            for (int i = 0; i < 20; i++) { m.QYes = m.QNo + m.B * Math.Log(0.1 / 0.9); bk.Drift(5, x => 0.5, rng); set.Add(Math.Round(m.PYes, 4)); }
            Check(set.Count > 10, "噪声让每晚的价格不同");
            // 情报：三档准确率和假情报
            foreach (int tier in new[] { 0, 1, 2 })
            {
                int right = 0, trials = 4000;
                for (int i = 0; i < trials; i++) if (PredictBook.MakeTip(m, true, tier, rng).Claim) right++;
                double expect = (1 - Tuning.IntelFake) * Tuning.IntelTipAcc[tier] + Tuning.IntelFake * 0;
                Check(Math.Abs(right / (double)trials - expect) < 0.04, $"第 {tier + 1} 档说对的比例约 {expect:0.00}：{right / (double)trials:0.00}");
            }
            Check(Math.Abs(PredictBook.MakeTip(m, true, 2, rng).Paid - Tuning.IntelTipPrice[2] * m.Loss) < 1e-9, "价格按单盘额度的比例");
            // 到期后消息一起清掉
            bk.Tips.Add(new PredictTip { Market = closing.Id });
            var w = new Wallet { Address = "x" };
            bk.Night(6, new string[0], mm => true, _ => w, a => 1, rng);
            Check(bk.Tips.Count == 0, "盘口结算后情报作废");
            Check(bk.Stats.TryGetValue("Event", out var stE) && stE.Count >= 1 && stE.Hits >= 1, "结算计入按类型的历史");
            // gas / CRA / 时代 先验
            double flat = PredictMath.GasFlat(10, 1.2);
            double phase0 = PredictMath.GasAtLeast(10, 0, 1.2), sum2 = 0;
            for (int ph = 0; ph < Tuning.TxGasPeriod; ph++) sum2 += PredictMath.GasAtLeast(10, ph, 1.2);
            Check(Math.Abs(flat - sum2 / Tuning.TxGasPeriod) < 1e-12 && phase0 >= 0 && phase0 <= 1, "gas 先验是各相位的平均");
            Check(PredictMath.GasAtLeast(10, 0, 0.1) == 1 && PredictMath.GasAtLeast(10, 0, 5) == 0, "gas 阈值越界取 0 和 1");
            int hit = 0, runs = 20000; var r2 = new Rng(4);
            for (int i = 0; i < runs; i++) if (TxRules.Gas(10, 3, r2) >= 1.3) hit++;
            Check(Math.Abs(hit / (double)runs - PredictMath.GasAtLeast(10, 3, 1.3)) < 0.02, "gas 概率和真实抽样对得上");
            double h = 1 - Math.Pow(1 - Tuning.CraSpiralWeekly, 1.0 / 7);
            Check(Math.Abs(PredictMath.CraWarnProb(1e9, 100, 10, 12, 14, h) - (1 - Math.Pow(1 - h, 3)) * Math.Pow(1 - h, 2)) < 1e-9, "补贴池充足时按日概率，窗口前发出的不算");
            Check(Math.Abs(PredictMath.CraWarnProb(150, 100, 10, 11, 12, h) - (1 - h)) < 1e-9, "垫子撑不过窗口前一晚：窗口第一晚必发，只要之前没按日概率发出");
            Check(PredictMath.CraWarnProb(50, 100, 10, 10, 10, h) == 1, "归零那晚必发");
            Check(PredictMath.EraProb(60, 61, 63, -1) > 0 && PredictMath.EraProb(60, 61, 63, -1) < 0.2, "没预警时在 50–90 里均匀，3 天窗口很小");
            Check(Math.Abs(PredictMath.EraProb(60, 62, 63, 58) - 0.5) < 1e-9 || PredictMath.EraProb(60, 62, 63, 58) > 0, "预警后落在 4–7 天内");
            Check(PredictMath.EraProb(60, 100, 105, -1) == 0, "窗口在时代区间之外为 0");
            Check(PredictMath.EraProb(60, 62, 65, 58) > 0.5 && PredictMath.EraProb(60, 70, 71, 58) == 0, "预警后窗口覆盖区间就是高概率");
        });

        Test("操纵检出：被查概率随推价幅度升高，推价每晚衰减，行情盘押中按方向被判", () =>
        {
            Check(Market.ManipChance(Tuning.ManipFree / 2) == 0 && Market.ManipChance(-Tuning.ManipFree / 2) == 0, "幅度低于免查线不查");
            Check(Market.ManipChance(Tuning.ManipFree + 0.05) > Market.ManipChance(Tuning.ManipFree + 0.03) && Market.ManipChance(-(Tuning.ManipFree + 0.05)) == Market.ManipChance(Tuning.ManipFree + 0.05), "幅度越大越容易被查，方向无关");
            Check(Market.ManipChance(5) == Tuning.ManipMax, "封顶");
            var mk = Market.CreateDefault(5); double p0 = mk.Price;
            mk.PlayerPush += Math.Log(1.1);
            Check(Math.Abs(mk.NeutralPrice - p0 / 1.1 * 1.0) < 1e-6 || mk.NeutralPrice > 0, "没有玩家时的价格");
            double before = mk.PlayerPush; mk.Night(new List<MarketEvent>(), new Rng(1), 5, a => 1);
            Check(Math.Abs(mk.PlayerPush - before * (1 - Tuning.Revert)) < 1e-9, "推价随回归衰减");
            // 行情盘：玩家推高后押「是」赢了，按概率被判操纵；押「否」不算；没推价不查
            int manip = 0, win = 0, noPush = 0;
            for (ulong sd = 1; sd <= 200; sd++)
            {
                foreach (var (yes, push, outcome) in new[] { (true, Tuning.ManipFree + 0.06, true), (false, Tuning.ManipFree + 0.06, false), (true, 0.0, true) })
                {
                    var bk = new PredictBook(); var w = new Wallet { Address = "w" }; w.Add(Assets.CRC, 1e5);
                    var m = PredictMarket.Create(++bk.Seq, PredictKind.Price, "PSC", "PSC", 100, 4, 5, 5, 0.3); bk.Markets.Add(m);
                    bk.Buy(w, m, yes, 510, 4, false, out _);
                    var ev = bk.Night(5, new string[0], x => outcome, _ => w, a => 1, new Rng(sd), push);
                    if (ev[0].Kind == "manip") { if (yes && push > 0) manip++; else Check(false, "不该被判操纵"); Check(Near(ev[0].Taint, ev[0].Amount, 1e-6) && Near(w.DirtyOf(Assets.CRC), ev[0].Amount, 1e-6), "照常兑付，整笔兑付记成脏的"); Check(w.Get(Assets.CRC) > 1e5 - 510, "本金不没收"); }
                    else if (yes && push > 0) win++; else if (push == 0) noPush++;
                }
            }
            double expect = Market.ManipChance(Tuning.ManipFree + 0.06) * 200;
            Check(Math.Abs(manip - expect) < 40 && manip + win == 200 && noPush == 200, $"推价超线 6% 约 {expect:0} 次被判，被判 {manip}，没被判 {win}");
        });

        Test("高息理财：开张条件、每晚存入与利息、崩盘、被举报、卷走", () =>
        {
            var acct = new Wallet { Address = "pb" }; acct.Add(Assets.CRC, 5000);
            var p = new Ponzi();
            Check(!p.Start(acct, 0.04, Tuning.PonziMinDay - 1), "没到开放日不能开");
            Check(p.Start(acct, 0.04, Tuning.PonziMinDay) && Math.Abs(acct.Get(Assets.CRC) - (5000 - Tuning.PonziAdCost)) < 1e-9, "开张扣宣传费");
            Check(!p.CanOpen(Tuning.PonziMinDay + 1), "已经开着不能再开");
            // 池子增长不超过上限；利息先用新存入的付，不够才动账户
            var rng = new Rng(3);
            double maxPool = 0; bool ended = false;
            for (int d = Tuning.PonziMinDay; d < Tuning.PonziMinDay + 40 && p.Open; d++)
            {
                var ev = p.Night(d, 50, acct, rng);
                maxPool = Math.Max(maxPool, p.Pool);
                Check(p.Pool <= Ponzi.Cap(d) + 1e-6, "池子不超过上限");
                if (ev != null) { ended = true; Check(ev.Amount >= 0 && p.Done && !p.Open && p.Pool == 0, "崩盘后池子清零、产品关闭"); }
            }
            Check(maxPool > 0 && ended, "池子涨起来、最后会崩盘（40 晚内）");
            Check(!p.CanOpen(100) && p.Rug(acct) == 0, "崩盘后不能重开也不能卷走");
            // 付不出利息就挤兑
            var poor = new Wallet { Address = "x" }; poor.Add(Assets.CRC, Tuning.PonziAdCost);
            var q = new Ponzi(); q.Start(poor, 0.10, 20);
            q.Pool = 100000; var ev2 = q.Night(20, 50, poor, new Rng(1));
            Check(ev2 != null && ev2.Kind == "collapse", "账户里没钱补利息：崩盘");
            // 卷走：池子进钱包、整笔脏
            var w = new Wallet { Address = "y" }; w.Add(Assets.CRC, 2000);
            var r = new Ponzi(); r.Start(w, 0.04, 20); r.Pool = 5000;
            double cash = w.Get(Assets.CRC);
            Check(Math.Abs(r.Rug(w) - 5000) < 1e-9 && Math.Abs(w.Get(Assets.CRC) - cash - 5000) < 1e-9 && Math.Abs(w.DirtyOf(Assets.CRC) - 5000) < 1e-9 && r.Done, "卷走：池子进钱包，这 5000 CRC 整笔脏");
            // 被举报概率随日息和池子升高
            Check(Ponzi.ReportChance(0.10, 5000, 20) > Ponzi.ReportChance(0.02, 5000, 20) && Ponzi.ReportChance(0.04, 9000, 20) > Ponzi.ReportChance(0.04, 100, 20), "日息越高、池子越大越容易被举报");
            Check(Ponzi.InflowMean(20, 0.08, 50) > Ponzi.InflowMean(20, 0.04, 50) * 2, "日息高于 4% 时存入按指数放大");
        });

        Test("NPC 骗局：插针、空投与授权、第三方收益池", () =>
        {
            // 插针：出现频率、幅度、标记价只吃一半、当晚回去
            int wicks = 0, n = 4000;
            for (int i = 0; i < n; i++)
            {
                var mk = Market.CreateDefault(20); var rng = new Rng((ulong)(i + 1));
                double p0 = mk.Price;
                mk.Open(new List<MarketEvent>(), rng, 20);
                double open = mk.OpenPrice;
                double w = mk.TryWick(rng, 20);
                if (w == 0) continue;
                wicks++;
                Check(Math.Abs(Math.Abs(Math.Log(mk.Price / open)) - Math.Abs(w)) < 1e-9 && Math.Abs(w) >= Tuning.WickMin && Math.Abs(w) <= Tuning.WickMax, "插针幅度");
                Check(Math.Abs(mk.Mark - (open + mk.Price) / 2) < 1e-9 && mk.OpenPrice == open, "标记价用开门前的价格和插针价平均");
                double back = mk.WickBack, before = mk.Price;
                var none = new List<MarketEvent>();
                // 夜里：先回去 WickBack，再走正常回归
                var copy = Market.CreateDefault(20); copy.Main.SetPrice(before * Math.Exp(-w * back), Tuning.PoolDepth(20));
                mk.Night(none, new Rng(9), 20, a => 1); copy.Night(none, new Rng(9), 20, a => 1);
                Check(Math.Abs(mk.Price - copy.Price) / copy.Price < 0.05 && mk.Wick == 0, "当晚按比例回去");
            }
            Check(Math.Abs(wicks / (double)n - Tuning.WickChance) < 0.015, $"插针频率约 {Tuning.WickChance}：{wicks / (double)n:0.000}");
            // 空投
            var sc = new Scams(); var r2 = new Rng(5);
            var assets = new[] { Assets.CRT, Assets.PSC, Assets.CRC };
            int scam = 0, typo = 0, unlimited = 0;
            for (int i = 0; i < 2000; i++)
            {
                var a = sc.NewAirdrop(10, r2, assets);
                if (a.Scam) { scam++; if (double.IsPositiveInfinity(a.Approve) || a.Approve >= 20 * a.Reward - 10) unlimited++; if (!new[] { "Poinbase", "PSC Foundation", "RatDigg", "Revolution DAO", "Gutter Labs", "Station Rewards" }.Contains(a.Name)) typo++; }
                else Check(!double.IsPositiveInfinity(a.Approve) && a.Approve <= 60, "正规空投不要大额授权");
            }
            Check(Math.Abs(scam / 2000.0 - Tuning.AirdropScam) < 0.04 && unlimited == scam && typo > scam * 0.4, $"钓鱼占比 {scam / 2000.0:0.00}，全部要无上限或远超奖励的授权，过半名字是仿冒 {typo}");
            var wl = new Wallet { Address = "0xa" }; wl.Add(Assets.CRT, 1000);
            var fake = new Airdrop { Id = 1, Name = "P0inbase", Asset = Assets.CRT, Approve = double.PositiveInfinity, Reward = 200, Scam = true, Expire = 12 };
            var wlOther = new Wallet { Address = "0xc" };
            Check(!Scams.Authorized(fake, wl) && Scams.Authorize(fake, wl, 10) && Scams.Authorized(fake, wl) && !Scams.Authorized(fake, wlOther) && !Scams.Authorize(fake, wl, 99), "空投逐个授权：授权的钱包才算已授权，换钱包要重新授权，过期不能授权");
            var ap = sc.Claim(fake, wl, 10, r2, out double got);
            Check(ap != null && ap.Scam && got < 20 && sc.Claim(fake, wl, 10, r2, out _) == null, "钓鱼空投只给诱饵，不能重复领");
            sc.Airdrops.Clear(); sc.Pools.Clear();
            var ev = sc.Night(ap.DrainDay, r2, x => x == "0xa" ? wl : null, assets);
            Check(ev.Any(e => e.Kind == "drain" && Math.Abs(e.Amount - 1000) < 1e-9) && wl.Get(Assets.CRT) < 1e-9, "到期那晚转走这个币");
            var wl2 = new Wallet { Address = "0xb" }; wl2.Add(Assets.CRT, 1000);
            var ap2 = sc.Claim(new Airdrop { Id = 2, Name = "x", Asset = Assets.CRT, Approve = double.PositiveInfinity, Scam = true, Expire = 12 }, wl2, 10, r2, out _);
            Check(sc.Revoke(ap2.Id), "撤销授权");
            sc.Night(ap2.DrainDay, r2, x => wl2, assets);
            Check(Math.Abs(wl2.Get(Assets.CRT) - 1000) < 1e-9, "撤销后不会被转走");
            var good = sc.Claim(new Airdrop { Id = 3, Name = "Poinbase", Asset = Assets.CRT, Approve = 0, Reward = 50, Expire = 12 }, wl2, 10, r2, out double g2);
            Check(good == null && Math.Abs(g2 - 50) < 1e-9, "正规空投给奖励、不留授权");
            // 收益池：骗局概率随日息、时间锁、审计变化
            Check(Scams.ScamChance(0.03, false, false) > Scams.ScamChance(0.01, false, false) && Scams.ScamChance(0.02, true, true) < Scams.ScamChance(0.02, false, false), "日息越高越像骗局，时间锁和审计降低概率");
            // 正规池：计息、储备耗尽退本金；时间锁取款延迟到账
            var pool = new YieldPool { Id = 1, Name = "ok", Rate = 0.02, Reserve = 100, Cap = 1e9, Timelock = true, OpenDay = 1 };
            var sc2 = new Scams(); sc2.Pools.Add(pool);
            var w3 = new Wallet { Address = "0xc" }; w3.Add(Assets.CRC, 3000);
            Check(sc2.Deposit(pool, w3, 1000) && Math.Abs(w3.Get(Assets.CRC) - 2000) < 1e-9, "存入");
            sc2.Night(2, new Rng(1), x => w3, assets);
            Check(Math.Abs(pool.Of("0xc") - 1020) < 1e-9 && Math.Abs(pool.Reserve - 80) < 1e-9, "计息从储备出");
            Check(sc2.Withdraw(pool, w3, 3) == 0 && pool.Pending.Count == 1, "时间锁：进取款队列");
            sc2.Night(3 + Tuning.YieldLockDays, new Rng(1), x => w3, assets);
            Check(Math.Abs(w3.Get(Assets.CRC) - 3020) < 1e-9, "到期到账");
            var pool2 = new YieldPool { Id = 2, Name = "dry", Rate = 0.03, Reserve = 10, Cap = 1e9, OpenDay = 1 };
            sc2.Pools.Add(pool2); sc2.Deposit(pool2, w3, 1000);
            var evd = sc2.Night(5, new Rng(1), x => w3, assets);
            Check(evd.Any(e => e.Kind == "dry") && pool2.Closed && Math.Abs(w3.Get(Assets.CRC) - 3020) < 1e-9, "储备不够付利息：退本金关闭");
            // 骗局池：跑路
            int rugs = 0;
            for (ulong sd = 1; sd <= 400; sd++)
            {
                var p = new YieldPool { Id = 9, Name = "scam", Rate = 0.03, Reserve = 1e9, Cap = 1e9, Scam = true, OpenDay = 1 };
                var s3 = new Scams(); s3.Pools.Add(p);
                var w4 = new Wallet { Address = "0xd" }; w4.Add(Assets.CRC, 100);
                s3.Deposit(p, w4, 100);
                var e4 = s3.Night(2, new Rng(sd), x => w4, assets);
                if (e4.Any(e => e.Kind == "rug")) { rugs++; Check(p.Closed && p.Total == 0 && w4.Get(Assets.CRC) < 1e-9, "跑路后存款没了"); }
            }
            Check(Math.Abs(rugs / 400.0 - Tuning.YieldRugNightly) < 0.05, $"骗局池每晚跑路概率：{rugs / 400.0:0.00}");
        });

        Test("预测市场：内幕先买后卖，卖出所得的内幕部分记成脏的", () =>
        {
            var bk = new PredictBook(); var w = new Wallet { Address = "w" }; w.Add(Assets.CRC, 1e6);
            var m = PredictMarket.Create(++bk.Seq, PredictKind.Event, "e", "t", 0, 4, 7, 7, 0.05); bk.Markets.Add(m);
            bk.Buy(w, m, true, m.BuyCost(true, 0.3 * m.ShareCap) * (1 + Tuning.PredictFee), 4, true, out double si);
            bk.Buy(w, m, true, 100, 4, false, out double sn);
            double cost = bk.PosOf(m.Id, "w").Cost;
            double p1 = m.PYes, p2 = p1 + (1 - p1) / 2;
            m.QYes = m.QNo + m.B * Math.Log(p2 / (1 - p2));   // 夜里价格又涨了一截
            double t0 = w.DirtyOf(Assets.CRC);
            Check(bk.SellAll(w, m, 5, a => 1, out double got, out double taint) == PredictResult.Ok && got > cost, "照常卖出，赚到价差");
            Check(Near(taint, got * si / (si + sn), 1e-6) && Near(w.DirtyOf(Assets.CRC) - t0, taint, 1e-9), $"内幕份额对应的卖出所得记成脏的：{taint:0.00} / {got:0.00}");
            // 非内幕卖出不加污点
            var bk2 = new PredictBook(); var w2 = new Wallet { Address = "w" }; w2.Add(Assets.CRC, 1e6);
            var m2 = PredictMarket.Create(++bk2.Seq, PredictKind.Event, "e", "t", 0, 4, 7, 7, 0.05); bk2.Markets.Add(m2);
            bk2.Buy(w2, m2, true, 200, 4, false, out _);
            m2.QYes = m2.QNo + m2.B * Math.Log(0.6 / 0.4);
            bk2.SellAll(w2, m2, 5, a => 1, out _, out double t2);
            Check(t2 == 0 && w2.Dirty.Count == 0, "非内幕卖出不记污点");
        });

        Test("罪证折算：按额 × 比例，单次封顶", () =>
        {
            Check(Near(Wallet.FraudFor(100), 100 * Tuning.PredictFraudShare, 1e-9) && Wallet.FraudFor(1e9) == Tuning.PredictFraudCap && Wallet.FraudFor(0) == 0, "按比例折罪证，单次封顶");
        });

        Test("日 K：开盘、日内高低、成交量、收盘、均线、保留天数", () =>
        {
            var cb = new CandleBook();
            cb.Open(Assets.PSC, 5, 100);
            cb.Tick(Assets.PSC, 5, 120, 3000);
            cb.Tick(Assets.PSC, 5, 90, 500, true);
            cb.Close(Assets.PSC, 5, 105);
            var k = cb.Of(Assets.PSC)[0];
            Check(k.O == 100 && k.H == 120 && k.L == 90 && k.C == 105 && k.Closed, "开高低收");
            Check(k.VolNpc == 3000 && k.VolPlayer == 500, "成交量分大户和玩家");
            cb.Tick(Assets.PSC, 5, 200);
            Check(k.H == 120 && k.C == 105, "收盘后不再变");
            cb.Tick(Assets.PSC, 6, 110);
            Check(cb.Of(Assets.PSC).Count == 2 && cb.Of(Assets.PSC)[1].O == 110, "读档时没有当天的蜡烛就按现价开一根");
            var cb2 = new CandleBook();
            cb2.Open(Assets.PSC, 5, 100); cb2.Close(Assets.PSC, 5, 105);
            cb2.Open(Assets.PSC, 6, 98);
            var k6 = cb2.Of(Assets.PSC)[1];
            Check(k6.O == 105 && k6.H == 105 && k6.L == 98 && k6.C == 98, "开盘接昨天收盘，夜间变动算进今天这根");
            for (int d = 7; d < 7 + CandleBook.Keep + 10; d++) { cb.Open(Assets.PSC, d, d); cb.Close(Assets.PSC, d, d); }
            Check(cb.Of(Assets.PSC).Count == CandleBook.Keep, "只保留最近若干天");
            var ma = CandleBook.Ma(new List<Candle> { new Candle { C = 1 }, new Candle { C = 3 }, new Candle { C = 5 } }, 2);
            Check(ma[0] == 1 && ma[1] == 2 && ma[2] == 4, "均线");
            Check(cb.Of("SPARK").Count == 0, "没有的币种是空的");
        });

        Test("剧情联动：各选择的倍率与期限、只生效一次、SPARK 推涨", () =>
        {
            var saved = StoryMods.Current;
            try
            {
                var sm = new StoryMods(); StoryMods.Current = sm;
                Check(sm.Apply("serpico_sec", 50) && !sm.Apply("serpico_sec", 51), "只生效一次");
                Check(sm.MixClean(60) == Tuning.StorySecMixClean && sm.MixClean(50 + Tuning.StorySecDays) == 1, "治安部整顿期内混币器洗得更差，到期恢复");
                Check(Mixer.RoundRate(1000, 60) < Mixer.RoundRate(1000) * 0.75, "整顿期内每轮清洗率 ×0.7");
                Check(sm.Sanction(60) == Tuning.StorySecSanctionMult && sm.Enforce(60) == Tuning.StorySecEnforceMult, "制裁与查处倍率");
                sm.Apply("lun_arrested", 55);
                Check(Math.Abs(sm.Enforce(60) - Tuning.StorySecEnforceMult * Tuning.StoryLunMult) < 1e-12 && sm.Enforce(55 + Tuning.StoryLunDays) == Tuning.StorySecEnforceMult, "伦警官立威期叠加，先到期");
                Check(sm.OtcSting(60) == Tuning.StoryLunMult && sm.OtcSting(55 + Tuning.StoryLunDays) == 1, "伦警官立威期内换币客户便衣更多");
                int st0 = 0, st1 = 0;
                for (ulong sd = 1; sd <= 3000; sd++)
                {
                    if (Otc.MakeSell(new List<MarketEvent>(), false, a => 1, 200, new Rng(sd)).Sting) st0++;
                    if (Otc.MakeSell(new List<MarketEvent>(), false, a => 1, 200, new Rng(sd), Tuning.StoryLunMult).Sting) st1++;
                }
                Check(st1 > st0, $"便衣 {st0} → {st1}（3000 位卖币客户）");
                // CRT 脱锚减半：镇压开始时脱锚的频率
                int dep0 = 0, dep1 = 0;
                for (ulong sd = 1; sd <= 2000; sd++)
                {
                    StoryMods.Current = new StoryMods(); var s0 = new Stablecoins(); s0.OnEventStart(MarketEvent.Crackdown, 10, new Rng(sd)); if (s0.Crt < 1) dep0++;
                    var bm = new StoryMods(); bm.Apply("serpico_bm", 1); StoryMods.Current = bm;
                    var s1 = new Stablecoins(); s1.OnEventStart(MarketEvent.Crackdown, 10, new Rng(sd)); if (s1.Crt < 1) dep1++;
                }
                Check(dep1 < dep0 * 0.65 && dep1 > dep0 * 0.35, $"CRT 脱锚约减半：{dep0} → {dep1}");
                // SPARK 推涨：几晚内一共涨到设定倍数
                var rv = new StoryMods(); StoryMods.Current = rv; rv.Apply("serpico_rev", 10);
                Check(rv.CraSpiral(20) == Tuning.StoryCraSpiralMult, "螺旋概率减半");
                double prod = 1; for (int i = 0; i < 10; i++) prod *= rv.TakeSparkPush();
                Check(Math.Abs(prod - Tuning.StorySerpicoSpark) < 1e-9 && rv.SparkPushNights == 0, $"SPARK 推涨合计 {prod:0.000}");
                var lk = new StoryMods(); lk.Apply("serpico_rev", 1); lk.TakeSparkPush(); lk.Apply("eleanor_leak", 2);
                double p2 = 1; for (int i = 0; i < 10; i++) p2 *= lk.TakeSparkPush();
                Check(Math.Abs(p2 - Math.Pow(Tuning.StorySerpicoSpark, 1 - 1.0 / Tuning.StorySparkNights) * Tuning.StoryLeakSpark) < 1e-9, "两次推涨叠加");
                Check(lk.CrcHaircut == Tuning.StoryLeakHaircutMult, "泄露数据盘：CRC 减记概率翻倍");
            }
            finally { StoryMods.Current = saved; }
        });

        Test("Tumbler 剧情线：开始条件、上门日程、混币器倍率", () =>
        {
            var saved = StoryMods.Current;
            try
            {
                var t = new Tumbler();
                Check(!t.TryStart(Tuning.TumblerMinDay - 1, true) && !t.TryStart(40, false), "没到日子或没解锁混币器不开始");
                Check(t.TryStart(40, true) && t.StartDay == 41 && !t.TryStart(42, true), "开始一次，明天第一次上门");
                Check(t.Due(40) == 0 && t.Due(41) == 1, "第 1 次在开始日");
                t.Visited(41);
                Check(t.Due(41) == 0 && t.Due(43) == 0 && t.Due(44) == 2, "同一天不重复，第 2 次在 +3");
                t.Visited(44); Check(t.Due(46) == 3, "第 3 次在 +5"); t.Choice = Tumbler.Hide; t.Visited(46);
                Check(t.Due(49) == 0 && t.Due(50) == 4, "第 4 次在 +9"); t.Visited(50);
                Check(t.Done && t.Due(60) == 0, "结束");
                var t2 = new Tumbler(); t2.TryStart(40, true); t2.Visited(45);
                Check(t2.Due(46) == 2, "错过的上门往后补");
                // 混币器倍率
                var sm = new StoryMods(); StoryMods.Current = sm;
                double fee0 = Mixer.Fee, rate0 = Mixer.RoundRate(1000);
                sm.Apply("tumbler_kept", 50);
                Check(Math.Abs(Mixer.Fee - fee0 * Tuning.TumblerKeptFee) < 1e-12 && Mixer.RoundRate(1000) > rate0, "藏好密钥盘：手续费降、匿名集变大");
                var sm2 = new StoryMods(); StoryMods.Current = sm2; sm2.Apply("tumbler_sold", 50);
                Check(Mixer.Fee > fee0 && sm2.MixerRug == Tuning.TumblerSoldRug, "卖给黑市：手续费涨、跑路概率上升");
                var sm3 = new StoryMods(); sm3.Apply("tumbler_reported", 50);
                Check(sm3.MixerClosed, "举报：混币器关闭");
            }
            finally { StoryMods.Current = saved; }
        });

        Test("Tumbler 之后：探员与搜查卡住上门日程、泄露还原出款污点、审判、黑市跑路、换皮重开", () =>
        {
            var saved = StoryMods.Current;
            try
            {
                StoryMods.Current = new StoryMods();
                // 探员：第 2 次之后的第二天起来，来过的第二天起才排第 3 次
                var t = new Tumbler(); var a = new TumblerAfter();
                t.TryStart(40, true); t.Visited(41); t.Visited(44);
                Check(!a.AgentDue(t, 44) && a.AgentDue(t, 45), "探员在第 2 次上门的第二天起来");
                Check(t.Due(46, a) == 0 && t.Due(46) == 3, "探员没来过，第 3 次不排（不传进度时照旧）");
                a.Agent = TumblerAfter.Deny; a.AgentDay = 46;
                Check(t.Due(46, a) == 0 && t.Due(47, a) == 3 && !a.AgentDue(t, 47), "探员来过的第二天起排第 3 次，只来一次");
                t.Choice = Tumbler.Hide; t.Visited(47);
                Check(!a.NeedSearch(t) && t.Due(51, a) == 4, "否认过：不搜查，第 4 次照常");
                // 配合过又藏了：第 4 次前一天搜查
                var t2 = new Tumbler(); var a2 = new TumblerAfter { Agent = TumblerAfter.Cooperate, AgentDay = 45 };
                t2.TryStart(40, true); t2.Visited(41); t2.Visited(44); t2.Choice = Tumbler.Hide; t2.Visited(46);
                Check(!a2.SearchDue(t2, 48) && a2.SearchDue(t2, 49) && t2.Due(50, a2) == 0, "配合 + 藏：第 4 次前一天搜查，搜查前不排第 4 次");
                a2.Searched = true; a2.SearchDay = 50;
                Check(t2.Due(50, a2) == 0 && t2.Due(51, a2) == 4 && !a2.SearchDue(t2, 51), "搜查的第二天起排第 4 次");
                var t3 = new Tumbler { Choice = Tumbler.Report }; t3.TryStart(40, true); t3.Visited(41); t3.Visited(44); t3.Visited(46);
                Check(!a2.NeedSearch(t3), "举报的不搜查");

                // 出款记录与泄露
                Func<string, double> px = x => 1;
                var w = new Wallet { Address = "w" }; w.AddDirty(Assets.CRC, 5000, 4000);
                var m = new Mixer();
                m.Deposit(w, Assets.CRC, 1000, 3, 1, px);
                m.Night(w, 4, px, new Rng(1));
                var r0 = m.Returns.Single();
                Check(r0.Day == 4 && r0.Wallet == "w" && r0.Cleaned > 100 && Near(r0.Cleaned, 800 - (w.DirtyOf(Assets.CRC) - 3200), 1e-6), $"出款记下洗掉的污点：{r0.Cleaned:0}");
                double before = w.DirtyOf(Assets.CRC);
                var lk = m.Leak(4 + Tuning.TumblerLeakDays);
                Check(lk.Count == 1 && Near(lk.Cleaned, r0.Cleaned, 1e-6) && Near(lk.Fraud, Wallet.FraudFor(r0.Cleaned), 1e-9) && w.DirtyOf(Assets.CRC) == before && m.Returns.Count == 0, "泄露：按洗掉的污点记罪证，不动钱包，记录清空");
                var m2 = new Mixer();
                m2.Returns.Add(new MixReturn { Day = 1, Wallet = "v", Amount = 10, Cleaned = 500 });
                m2.Returns.Add(new MixReturn { Day = 40, Wallet = "v", Amount = 10, Cleaned = 300 });
                m2.Returns.Add(new MixReturn { Day = 40, Wallet = "gone", Amount = 10, Cleaned = 300 });
                var lk2 = m2.Leak(45);
                Check(lk2.Count == 2 && Near(lk2.Cleaned, 600, 1e-9) && Near(lk2.Fraud, Wallet.FraudFor(600), 1e-9), "只算 30 天内的，罪证 = 洗掉的污点 × 50%");
                a.LeakTonight(50); a.LeakTonight(60);
                Check(a.LeakDay == 50, "泄露只安排一次");

                // 审判
                int conv = 0;
                for (int i = 0; i < 2000; i++) { var x = new TumblerAfter(); x.StartTrial(10, new Rng((ulong)i + 1)); if (x.Verdict == 1) conv++; }
                Check(Near(conv / 2000.0, Tuning.TumblerTrialConvict, 0.03), $"定罪约 {Tuning.TumblerTrialConvict:P0}：{conv / 2000.0:P1}");
                var tr = new TumblerAfter(); tr.StartTrial(10, new Rng(7));
                Check(tr.TrialOpenDay == 10 + Tuning.TumblerTrialOpen && tr.TrialDay == 10 + Tuning.TumblerTrialDays && tr.ClosedDay == 10, "开盘、宣判、关停的日子");
                var rng = new Rng(9);
                Check(tr.Hint(tr.TrialOpenDay - 2, rng) == null && tr.Hint(tr.TrialOpenDay - 1, rng) != null && tr.Hint(tr.TrialOpenDay - 1, rng) == null, "开盘前一晚起每晚一条");
                for (int dd = tr.TrialOpenDay; dd < tr.TrialDay + 3; dd++) tr.Hint(dd, rng);
                Check(tr.Hints.Count == tr.TrialDay - tr.TrialOpenDay + 1, $"宣判前一晚为止：{tr.Hints.Count} 条");
                Check(Near(new TumblerAfter().TrialPublic(), Tuning.TumblerTrialConvict, 1e-9), "没有新闻时公开概率 = 先验");
                double sumC = 0, sumA = 0; int nC = 0, nA = 0;
                for (int i = 0; i < 1000; i++)
                {
                    var x = new TumblerAfter(); var rr = new Rng((ulong)i + 11); x.StartTrial(0, rr);
                    for (int dd = 0; dd < 10; dd++) x.Hint(dd, rr);
                    if (x.Verdict == 1) { sumC += x.TrialPublic(); nC++; } else { sumA += x.TrialPublic(); nA++; }
                }
                Check(sumC / nC > 0.85 && sumA / nA < 0.5, $"新闻把公开概率推向真实判决：定罪 {sumC / nC:0.00}，无罪 {sumA / nA:0.00}");

                // 黑市跑路、换皮重开
                var sold = new TumblerAfter(); sold.StartSold(20, new Rng(3));
                Check(sold.BmRugDay >= 20 + Tuning.TumblerBmRugMin && sold.BmRugDay <= 20 + Tuning.TumblerBmRugMax, $"黑市跑路在 {sold.BmRugDay}");
                var mr = new Mixer(); var w3 = new Wallet { Address = "z" }; w3.Add(Assets.CRC, 1000);
                mr.Deposit(w3, Assets.CRC, 400, 2, 1, px); mr.Deposit(w3, Assets.CRC, 300, 3, 1, px);
                var rug = mr.RugAll();
                Check(rug.Count == 2 && rug.All(x => x.Rugged) && mr.Pending.Count == 0, "黑市跑路：在途的全部丢失");
                var rb = new TumblerAfter { ClosedDay = 30 };
                Check(!rb.RebrandDue(30 + Tuning.TumblerRebrandDays - 1) && rb.RebrandDue(30 + Tuning.TumblerRebrandDays) && !new TumblerAfter().RebrandDue(100), "关停满 15 天换皮，没关停不换");
                var sm = new StoryMods(); StoryMods.Current = sm;
                sm.Apply("tumbler_sold", 1); sm.Apply("tumbler_bm_rug", 2);
                Check(sm.MixerClosed, "黑市跑路后混币器关停");
                sm.Rebrand(false);
                Check(!sm.MixerClosed && sm.MixerRebranded && Near(Mixer.Fee, Tuning.MixerFee, 1e-12) && sm.MixerRug == 1, "换皮重开：重新开放，费率和跑路倍率回到 1");
                var w4 = new Wallet { Address = "h" }; w4.Add(Assets.CRC, 500);
                var mh = new Mixer(); mh.Deposit(w4, Assets.CRC, 500, 1, 1, px); mh.Night(w4, 2, px, new Rng(5));
                Check(w4.Dirty.Count == 0, "正常的新混币器：干净的币出来还是干净的");
                sm.Rebrand(true);
                Check(Near(Mixer.Fee, Tuning.MixerFee * Tuning.TumblerHoneypotFee, 1e-12), "钓鱼混币器费率打折");
                var w5 = new Wallet { Address = "k" }; w5.Add(Assets.CRC, 500);
                var mk = new Mixer(); mk.Deposit(w5, Assets.CRC, 500, 1, 1, px); mk.Night(w5, 2, px, new Rng(5));
                Check(Near(w5.DirtyOf(Assets.CRC), w5.Get(Assets.CRC), 1e-6), "从钓鱼混币器提回的币整笔都是脏的");
            }
            finally { StoryMods.Current = saved; }
        });

        Test("黑市商家收币：每类商家都收 CRT 或隐私币，平台收 PSC、CRT、CRA", () =>
        {
            foreach (MerchantType ty in Enum.GetValues(typeof(MerchantType)))
            {
                var acc = Merchants.Accepts(ty);
                Check(acc.Length >= 2 && acc.Distinct().Count() == acc.Length && acc.All(a => a == Assets.PSC || a == Assets.CRT || a == Assets.CRA || a == Assets.Ponero), $"{ty} 收 {string.Join("/", acc)}");
            }
            Check(Merchants.Accepts(MerchantType.Arms).Contains(Assets.Ponero) && !Merchants.Accepts(MerchantType.Rigs).Contains(Assets.Ponero), "违禁类收隐私币，硬件类不收");
            Check(Merchants.Table[MerchantType.Rigs].Any(e => e.Id == "rare_electronic" && e.Kind == GoodKind.Goods), "矿架与能源商家卖稀有电子元件（升级矿架槽要用），按普通货计价");
            Check(Merchants.PlatformAccepts.Contains(Assets.CRT) && Tuning.BmAutoSwapSlip > Tuning.DexFee * 10, "自动换币的滑点远高于 DEX 手续费");
        });

        Test("手册数值占位符：中英文都能从 Tuning 取到值，渲染后不留花括号", () =>
        {
            var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../src/Decentralized/ModFiles/Localization"));
            foreach (var lang in new[] { "zh", "en", "de", "fr", "es", "pl", "ru", "ja" })
            {
                foreach (var gk in new[] { "guide.web3.text" })
                {
                var line = System.IO.File.ReadAllLines(System.IO.Path.Combine(dir, lang + ".csv")).First(l => l.StartsWith(gk + ","));
                var text = line.Substring(gk.Length + 1).Trim('"').Replace("\"\"", "\"");
                Check(text.Contains("{"), lang + " " + gk + " 用了占位符");
                var bad = TuningText.Unresolved(text);
                Check(bad.Count == 0, lang + " 手册有取不到值的占位符：" + string.Join(" ", bad));
                Check(!TuningText.Render(text).Contains("{"), lang + " 渲染后没有残留花括号");
                }
            }
            foreach (var lang in new[] { "zh", "en", "de", "fr", "es", "pl", "ru", "ja" })
                foreach (var line in System.IO.File.ReadAllLines(System.IO.Path.Combine(dir, lang + ".csv")).Where(l => l.StartsWith("gg.") && !l.StartsWith("gg.ex.")))
                {
                    var gv = line.Substring(line.IndexOf(',') + 1).Trim('"');
                    Check(TuningText.Unresolved(gv).Count == 0 && !TuningText.Render(gv).Contains("{"), lang + " 鉴定指南占位符取不到值：" + line.Substring(0, line.IndexOf(',')));
                }
            Check(TuningText.Render("{BmListFee:%}") == "5%" && TuningText.Render("{PonziRates[4]:%}") == "10%" && TuningText.Render("{WalletMax}") == "5", "格式：百分比、数组下标、整数");
        });

        Test("多语言表：8 种语言键集合与英文一致，占位符和标记一致", () =>
        {
            var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../src/Decentralized/ModFiles/Localization"));
            Func<string, Dictionary<string, string>> load = lang =>
            {
                var d = new Dictionary<string, string>();
                foreach (var line in System.IO.File.ReadAllLines(System.IO.Path.Combine(dir, lang + ".csv")))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    int c = line.IndexOf(',');
                    Check(c > 0, lang + " 行没有逗号：" + line);
                    var v = line.Substring(c + 1).Trim();
                    if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') v = v.Substring(1, v.Length - 2).Replace("\"\"", "\"");
                    d[line.Substring(0, c).Trim()] = v;
                }
                return d;
            };
            var en = load("en");
            var ph = new System.Text.RegularExpressions.Regex(@"\{[^{}]+\}");
            foreach (var lang in new[] { "zh", "de", "fr", "es", "pl", "ru", "ja" })
            {
                var d = load(lang);
                Check(d.Keys.OrderBy(x => x).SequenceEqual(en.Keys.OrderBy(x => x)), lang + " 键集合和英文不同");
                foreach (var kv in en)
                {
                    var v = d[kv.Key];
                    Check(ph.Matches(kv.Value).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).OrderBy(x => x)
                        .SequenceEqual(ph.Matches(v).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).OrderBy(x => x)), lang + " 占位符不一致：" + kv.Key);
                    foreach (var mark in new[] { "§", "¶", "\\n" })
                        Check(kv.Value.Split(new[] { mark }, StringSplitOptions.None).Length == v.Split(new[] { mark }, StringSplitOptions.None).Length, lang + " 标记 " + mark + " 数量不一致：" + kv.Key);
                    if (lang != "zh" && lang != "ja") Check(kv.Value.StartsWith(" ") == v.StartsWith(" ") && kv.Value.EndsWith(" ") == v.EndsWith(" "), lang + " 首尾空格和英文不同（拼接用）：" + kv.Key);
                    if (lang != "zh" && lang != "ja") Check(!System.Text.RegularExpressions.Regex.IsMatch(v, "[\u3040-\u30ff\u4e00-\u9fff]"), lang + " 有汉字或假名：" + kv.Key);
                }
            }
        });

        Test("mod 设置倍率：解析、封顶、挖矿产出按倍率", () =>
        {
            Check(Scales.Parse("×1.5") == 1.5 && Scales.Parse("×0.5") == 0.5 && Scales.Parse("x") == 1 && Scales.Parse(null) == 1, "解析档位文字");
            foreach (var c in Scales.Choices) Check(Scales.Parse(c) > 0, "档位都能解析：" + c);
            try
            {
                Scales.Risk = 2; Check(Math.Abs(Scales.R(0.3) - 0.6) < 1e-9 && Scales.R(0.8) == 1, "风险倍率乘概率并封顶 1");
                Scales.Risk = 1;
                var n = new Network();
                Scales.Mining = 1; double a = n.Mine(100);
                Scales.Mining = 2; double b2 = n.Mine(100);
                Check(a > 0 && Math.Abs(b2 - 2 * a) < 1e-9, "挖矿产出按倍率");
            }
            finally { Scales.Risk = 1; Scales.Mining = 1; }
        });

        Test("预测市场：先验概率", () =>
        {
            Check(Math.Abs(PredictMath.PurityAtLeast(8500, 8000, 8999) - 0.5) < 1e-9, "纯度 8500 以上一半");
            Check(PredictMath.PurityAtLeast(7000, 8000, 8999) == 1 && PredictMath.PurityAtLeast(9500, 8000, 8999) == 0, "越界取 0 和 1");
            double p3 = PredictMath.EventProb(0.4, 15, 150, 3);
            Check(Math.Abs(p3 - (1 - Math.Pow(1 - 0.04, 3))) < 1e-12, "事件概率：每天的概率连乘");
            Check(PredictMath.FactionProb(1) > 0 && PredictMath.FactionProb(3) > PredictMath.FactionProb(1), "天数越长概率越大");
            Check(Math.Abs(PredictMath.Phi(0) - 0.5) < 1e-6 && PredictMath.Phi(1.96) > 0.974 && PredictMath.Phi(1.96) < 0.976, "正态分布函数");
            // 行情先验和 Market 的真实过程对得上
            foreach (var (mult, nights) in new[] { (1.0, 1), (1.1, 2), (0.9, 3), (1.2, 4) })
            {
                int hit = 0, n = 3000;
                for (int i = 0; i < n; i++)
                {
                    var mk = Market.CreateDefault(20);
                    var rng = new Rng((ulong)(i * 7 + 3));
                    double start = mk.Price, anchor = mk.Anchor(new List<MarketEvent>());
                    double thr = start * mult;
                    for (int k = 0; k < nights; k++) { mk.Night(new List<MarketEvent>(), rng, 20 + k, a => 1); mk.Open(new List<MarketEvent>(), rng, 21 + k); }
                    if (mk.Price >= thr) hit++;
                }
                double sim = hit / (double)n, calc = PredictMath.PriceAtLeast(Market.CreateDefault(20).Price, Market.CreateDefault(20).Anchor(new List<MarketEvent>()), Tuning.PscVol, Tuning.Revert, nights, Market.CreateDefault(20).Price * mult);
                Check(Math.Abs(sim - calc) < 0.04, $"行情先验 ×{mult} {nights} 晚：模拟 {sim:0.000} vs 公式 {calc:0.000}");
            }
        });

        Console.WriteLine($"\n{_passed} 通过，{_failed} 失败");
        return _failed == 0 ? 0 : 1;
    }
}

internal sealed class SaveBundle
{
    public Market Market;
    public Stablecoins Stable;
    public Network Network;
    public Wallet Hot;
    public Exchange Exchange;
    public Rng Rng;
}
