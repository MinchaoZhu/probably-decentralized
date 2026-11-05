using System;

namespace Decentralized.Domain
{
    /// <summary>
    /// 带种子、状态可存档的随机数（xoshiro128**）。游戏自己的 RNG 不带种子；mod 的事件、行情、暴毙都用这个，
    /// 状态随存档保存，读档后结果不变，测试也能复现。
    /// </summary>
    public sealed class Rng
    {
        public uint S0, S1, S2, S3;

        public Rng() : this(1) { }

        public Rng(ulong seed)
        {
            // SplitMix64 展开种子
            ulong z = seed;
            ulong Next()
            {
                z += 0x9E3779B97F4A7C15UL;
                ulong x = z;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                return x ^ (x >> 31);
            }
            ulong a = Next(), b = Next();
            S0 = (uint)a; S1 = (uint)(a >> 32); S2 = (uint)b; S3 = (uint)(b >> 32);
            if ((S0 | S1 | S2 | S3) == 0) S0 = 1;
        }

        private static uint Rotl(uint x, int k) => (x << k) | (x >> (32 - k));

        public uint NextUInt()
        {
            uint result = Rotl(S1 * 5, 7) * 9;
            uint t = S1 << 9;
            S2 ^= S0; S3 ^= S1; S1 ^= S2; S0 ^= S3;
            S2 ^= t;
            S3 = Rotl(S3, 11);
            return result;
        }

        /// <summary>[0, 1)</summary>
        public double NextDouble() => (NextUInt() >> 8) * (1.0 / (1 << 24));

        public bool Chance(double p) => NextDouble() < p;

        /// <summary>[min, max]，含两端。</summary>
        public int Range(int min, int max) => min + (int)(NextDouble() * (max - min + 1));

        public double Range(double min, double max) => min + NextDouble() * (max - min);

        /// <summary>标准正态（Box–Muller）。</summary>
        public double Gauss()
        {
            double u1 = 1.0 - NextDouble(), u2 = NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
