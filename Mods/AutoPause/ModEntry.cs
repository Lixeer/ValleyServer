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
            // 加载配置
            this.Config = helper.ReadConfig<ModConfig>();
            
            // 监听菜单变化
            helper.Events.Display.MenuChanged += OnMenuChanged;
            
            this.Monitor.Log($"AutoPause (WebUI版) 已启动。目标服务器: {Config.ServerIP}:{Config.ServerPort}", LogLevel.Info);
        }

        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            // 仅在客机模式生效
            if (!Context.IsMultiplayer || Context.IsMainPlayer)
                return;

            bool hasMenu = e.NewMenu != null;

            // 逻辑：打开菜单时暂停，关闭菜单时恢复（再次发送命令）
            if (hasMenu && !_isPausedByMod)
            {
                TriggerWebCommand("打开菜单，请求暂停");
                _isPausedByMod = true;
            }
            else if (!hasMenu && _isPausedByMod)
            {
                TriggerWebCommand("关闭菜单，请求恢复");
                _isPausedByMod = false;
            }
        }

        private async void TriggerWebCommand(string reason)
        {
            try
            {
                // 构建请求 URL (根据 CommandWebUI 的通用格式)
                // 格式通常为: http://IP:Port/api/execute?token=TOKEN&cmd=COMMAND
                string url = $"http://{Config.ServerIP}:{Config.ServerPort}/api/execute" +
                             $"?token={Config.AccessToken}" +
                             $"&cmd={Uri.EscapeDataString(Config.Command)}";

                this.Monitor.Log($"[AutoPause] {reason}: 正在发送请求...", LogLevel.Debug);

                // 发送异步 GET 请求
                var response = await httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    this.Monitor.Log($"[AutoPause] 指令发送成功: {Config.Command}", LogLevel.Info);
                }
                else
                {
                    this.Monitor.Log($"[AutoPause] 发送失败! HTTP状态码: {response.StatusCode}", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[AutoPause] 网络请求异常: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
