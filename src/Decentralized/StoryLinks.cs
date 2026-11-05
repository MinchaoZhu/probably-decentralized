using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// 剧情联动：每晚读游戏剧情线的选择（塞尔皮科的证据、埃莉诺的数据盘、伦警官被捕），每个选择只生效一次，
    /// 改动本 mod 世界的倍率（见 <see cref="StoryMods"/>），第二天报纸登一条新闻。选择是玩家自己做的，市场要过几天才反应完。
    /// 无尽模式关闭角色剧情线，这些选择不会出现。
    /// </summary>
    public static class StoryLinks
    {
        public static void Night(SaveData d, int day)
        {
            var data = PlayerStore.GetStoreClientData();
            if (data == null) return;
            switch (data.serpicoDocumentChoice)
            {
                case SerpicoStoryline.CHOICE_REVOLUTION: Apply(d, day, "serpico_rev"); break;
                case SerpicoStoryline.CHOICE_BLACK_MARKET: Apply(d, day, "serpico_bm"); break;
                case SerpicoStoryline.CHOICE_SECURITY: Apply(d, day, "serpico_sec"); break;
            }
            if (data.eleanorDriveChoice == 2) Apply(d, day, "eleanor_leak");
            else if (data.eleanorDriveChoice == 1) Apply(d, day, "eleanor_return");
            if (data.officerLunArrested) Apply(d, day, "lun_arrested");
        }

        private static void Apply(SaveData d, int day, string key)
        {
            if (!d.Story.Apply(key, day)) return;
            if (key == "serpico_rev") d.Stable.Reserve += Tuning.StoryRevSubsidy;
            News.Show("pd_news_story_" + key, L.T("news.story." + key + ".title"), L.T("news.story." + key + ".desc"), 4, 9);
            Log.Info($"剧情联动生效：{key}（第 {day} 天）");
        }
    }
}
