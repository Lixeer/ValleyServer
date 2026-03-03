using System;
using System.Net.Http;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AutoPause
{
    public class ModEntry : Mod
    {
        private ModConfig Config;
        private static readonly HttpClient httpClient = new HttpClient();
        private bool _isPausedByMod = false;

        public override void Entry(IModHelper helper)
        {
            // 加载配置，如果不存在则按默认创建
            this.Config = helper.ReadConfig<ModConfig>();
            helper.WriteConfig(this.Config);

            helper.Events.Display.MenuChanged += OnMenuChanged;
            this.Monitor.Log("AutoPause WebUI版已就绪。如果还报Ambiguous match，请检查是否删除了旧版DLL！", LogLevel.Info);
        }

        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            if (!Context.IsMultiplayer || Context.IsMainPlayer) return;

            if (e.NewMenu != null && !_isPausedByMod)
            {
                _ = TriggerWebCommand("暂停");
                _isPausedByMod = true;
            }
            else if (e.NewMenu == null && _isPausedByMod)
            {
                _ = TriggerWebCommand("恢复");
                _isPausedByMod = false;
            }
        }

        private async System.Threading.Tasks.Task TriggerWebCommand(string action)
        {
            try
            {
                // 注意：这里完全不使用反射，只发网络请求
                string url = $"http://{Config.ServerIP}:{Config.ServerPort}/api/execute?token={Config.AccessToken}&cmd={Config.Command}";
                await httpClient.GetAsync(url);
                this.Monitor.Log($"[AutoPause] 触发{action}成功", LogLevel.Info);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[AutoPause] 网络请求失败: {ex.Message}", LogLevel.Error);
            }
        }
    }

    public class ModConfig
    {
        public string ServerIP { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 38080;
        public string AccessToken { get; set; } = "YourTokenHere";
        public string Command { get; set; } = "alos.pause";
    }
}
