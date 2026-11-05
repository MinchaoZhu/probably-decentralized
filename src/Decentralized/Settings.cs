using Decentralized.Domain;
using UnityEngine;

namespace Decentralized
{
    /// <summary>
    /// mod 设置（游戏自带的模组配置：主菜单“改装件”里选中 mod 后点齿轮）。
    /// 文件在 persistentDataPath/ModConfigs/probably_decentralized.json，全局生效，不随存档。
    /// 分类（Section）：界面、剧情、难度。
    /// </summary>
    public static class Settings
    {
        /// <summary>终端窗口尺寸档位（宽×高，界面单位）。第一项是旧版大小。</summary>
        public static readonly string[] TerminalSizes = { "828×680", "1200×900", "1400×1000" };
        public const string DefaultTerminalSize = "1200×900";

        private static ConfigEntry<string> _size, _risk, _mining;
        private static ConfigEntry<bool> _tumbler;

        /// <summary>Tumbler 上门剧情开关：关掉后不再有新的上门，已有进度保留。</summary>
        public static bool TumblerStory => _tumbler?.Value ?? true;

        public static float TerminalW => Size().w;
        public static float TerminalH => Size().h;

        private static (float w, float h) Size()
        {
            var s = _size?.Value ?? DefaultTerminalSize;
            var p = s.Split('×');
            if (p.Length == 2 && float.TryParse(p[0], out var w) && float.TryParse(p[1], out var h)) return (w, h);
            return (1200f, 900f);
        }

        /// <summary>
        /// 登记设置项。本地化表要等主菜单起来才读得到，而分类名是字符串（不是回调），所以等表可用了再登记。
        /// </summary>
        public static void DefineWhenReady(ModConfig config)
        {
            var go = new GameObject("pd_settings_defer");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Waiter>().Config = config;
        }

        private sealed class Waiter : MonoBehaviour
        {
            public ModConfig Config;
            private int _frames;
            private void Update()
            {
                _frames++;
                if (_frames < 300 || (L.T("settings.section.ui") == "settings.section.ui" && _frames < 1800)) return;
                Log.Guard("登记 mod 设置", () => Define(Config));
                Destroy(gameObject);
            }
        }

        public static void Define(ModConfig config)
        {
            if (config == null) { Log.Warn("[Settings] 拿不到模组配置，用默认值"); return; }
            string ui = L.T("settings.section.ui");
            _size = config.Define("terminal_size", DefaultTerminalSize, ui, () => L.T("settings.terminal_size"), TerminalSizes,
                () => L.T("settings.terminal_size_desc"));
            _size.OnChanged += _ => { if (CustomUIManager.Instance != null && CustomUIManager.Instance.IsOpen(TerminalWindow.Id)) TerminalWindow.Open(); };

            _tumbler = config.Define("tumbler_story", true, L.T("settings.section.story"), () => L.T("settings.tumbler_story"), () => L.T("settings.tumbler_story_desc"));

            string hard = L.T("settings.section.difficulty");
            _risk = config.Define("risk_scale", Scales.Default, hard, () => L.T("settings.risk_scale"), Scales.Choices, () => L.T("settings.risk_scale_desc"));
            _mining = config.Define("mining_scale", Scales.Default, hard, () => L.T("settings.mining_scale"), Scales.Choices, () => L.T("settings.mining_scale_desc"));
            _risk.OnChanged += _ => Apply();
            _mining.OnChanged += _ => Apply();
            Apply();
        }

        private static void Apply()
        {
            Scales.Risk = Scales.Parse(_risk?.Value);
            Scales.Mining = Scales.Parse(_mining?.Value);
            Log.Info($"[Settings] 风险 ×{Scales.Risk}，挖矿 ×{Scales.Mining}");        }
    }
}
