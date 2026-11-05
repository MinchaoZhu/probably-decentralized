using System;
using UnityEngine;

namespace Decentralized
{
    public static class ModInfo
    {
        public const string Id = "probably_decentralized";
        public const string Name = "Probably Decentralized";
        public const string Version = "1.0.0";
    }

    /// <summary>本地化：游戏的 mod 本地化表（ModFiles/Localization/*.csv），当前语言缺失时回退英文。</summary>
    public static class L
    {
        public static string T(string key, params object[] args) => ModHelper.GetLocalized(ModInfo.Id, key, args);

        /// <summary>当前游戏语言代码（en zh de fr es pl ru ja）。</summary>
        public static string Code => LocHelper.GetCurrentLocaleCode() ?? "en";

        /// <summary>译文普遍比中英文长的语言：表格列宽、操作键区按此放宽。</summary>
        public static bool Wide { get { var c = Code; return c == "de" || c == "fr" || c == "es" || c == "pl" || c == "ru"; } }
    }

    public static class Log
    {
        public static void Info(string m) => Debug.Log($"[{ModInfo.Name}] {m}");
        public static void Warn(string m) => Debug.LogWarning($"[{ModInfo.Name}] {m}");
        public static void Error(string m) => Debug.LogError($"[{ModInfo.Name}] {m}");

        public static void Guard(string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { Error($"{what}: {ex}"); }
        }
    }

    /// <summary>系统剪贴板（游戏里的输入框也能 Ctrl+V）。</summary>
    public static class Clipboard
    {
        public static void Copy(string s) => GUIUtility.systemCopyBuffer = s ?? "";
        public static string Paste() => (GUIUtility.systemCopyBuffer ?? "").Trim();
    }

    public static class Fmt
    {
        public static string Num(double v) => Math.Abs(v) >= 100 ? v.ToString("N0") : Math.Abs(v) >= 1 ? v.ToString("0.##") : v.ToString("0.####");
        public static string Credits(double v) => v.ToString("N0");
    }
}
