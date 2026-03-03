using System;
using System.Net.Http;
using System.Threading.Tasks;
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
            // 加载配置
            this.Config = helper.ReadConfig<ModConfig>();

            // 确保生成 config.json
            helper.WriteConfig(this.Config);

            // 注册菜单变更事件
            helper.Events.Display.MenuChanged += this.OnMenuChanged;

            this.Monitor.Log("AutoPause (WebUI版) 已启动，正在监听菜单状态。", LogLevel.Info);
        }

        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            // 只有客机且在联机模式下才触发
            if (!Context.IsMultiplayer || Context.IsMainPlayer)
                return;

            // e.NewMenu 不为空表示打开了新界面
            if (e.NewMenu != null && !_isPausedByMod)
            {
                _ = this.TriggerWebCommand("暂停 (Open Menu)");
                _isPausedByMod = true;
            }
            // e.NewMenu 为空表示回到了游戏主画面
            else if (e.NewMenu == null && _isPausedByMod)
            {
                _ = this.TriggerWebCommand("恢复 (Close Menu)");
                _isPausedByMod = false;
            }
        }

        private async Task TriggerWebCommand(string action)
        {
            if (string.IsNullOrEmpty(this.Config.AccessToken) || this.Config.AccessToken.Contains("填入"))
            {
                this.Monitor.Log("未配置 Token，已跳过指令发送。", LogLevel.Warn);
                return;
            }

            try
            {
                // 构建接口 URL
                string url = $"http://{this.Config.ServerIP}:{this.Config.ServerPort}/api/execute" +
                             $"?token={this.Config.AccessToken}" +
                             $"&cmd={Uri.EscapeDataString(this.Config.Command)}";

                var response = await httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    this.Monitor.Log($"[AutoPause] {action} 指令发送成功", LogLevel.Info);
                }
                else
                {
                    this.Monitor.Log($"[AutoPause] 接口返回错误: {response.StatusCode}", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[AutoPause] 网络请求失败: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
