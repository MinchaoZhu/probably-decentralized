namespace Decentralized
{
    /// <summary>
    /// mod 入口（游戏自带 mod 系统）。Init / OnEnable 在主菜单阶段调用：这里只订阅钩子、登记物品和人脉升级，
    /// 不碰存档和店里的东西（那些在读档 / 新游戏的钩子里做）。
    /// </summary>
    public class ModEntry : IMod
    {
        public void Init(ModManifest manifest) { }

        public void OnEnable()
        {
            Items.Subscribe();
            Items.OpenTerminal = TerminalWindow.Open;
            Items.OpenGpuHandbook = () => GpuGuide.Open();
            ModHook.OnModAssetsLoaded += GpuGuide.OnAssetsLoaded;
            Items.AddWallet = TerminalWindow.AddWallet;
            Items.CanAddWallet = TerminalWindow.CanAddWallet;
            Settings.DefineWhenReady(ModHelper.GetConfig(ModInfo.Id));
            Unlock.Register();
            Night.Subscribe();
            DealerStock.Subscribe();
            UsedDealerClient.Subscribe();
            CoinCustomers.Subscribe();
            HotCoinSeller.Subscribe();
            BlackDelivery.Subscribe();
            TumblerStory.Subscribe();
            GpuInspectView.Ensure();
            ModHook.OnGameLoadedLate += Session.Load;
            ModHook.OnNewGameStarted += OnNewGame;
            Log.Info($"{ModInfo.Name} {ModInfo.Version} 已启用");
        }

        public void OnDisable()
        {
            Items.Unsubscribe();
            Night.Unsubscribe();
            DealerStock.Unsubscribe();
            UsedDealerClient.Unsubscribe();
            CoinCustomers.Unsubscribe();
            HotCoinSeller.Unsubscribe();
            BlackDelivery.Unsubscribe();
            TumblerStory.Unsubscribe();
            GpuInspectView.Remove();
            WalletDriver.Remove();
            ModHook.OnModAssetsLoaded -= GpuGuide.OnAssetsLoaded;
            ModHook.OnGameLoadedLate -= Session.Load;
            ModHook.OnNewGameStarted -= OnNewGame;
        }

        private static void OnNewGame(string scenarioId) => Session.Load();
    }
}
