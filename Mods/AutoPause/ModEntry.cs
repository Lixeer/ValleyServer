using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AutoPause
{
    public class ModEntry : Mod
    {
        private ModConfig Config;
        private bool _isPausedByMod = false;

        public override void Entry(IModHelper helper)
        {
            // 加载配置并确保生成文件
            this.Config = helper.ReadConfig<ModConfig>();
            helper.WriteConfig(this.Config);

            // 监听菜单变化
            helper.Events.Display.MenuChanged += this.OnMenuChanged;

            this.Monitor.Log($"AutoPause (WebSocket版) 已启动。目标: ws://{this.Config.ServerIP}:{this.Config.ServerPort}/ws", LogLevel.Info);
        }

        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            if (!Context.IsMultiplayer || Context.IsMainPlayer)
                return;

            if (e.NewMenu != null && !_isPausedByMod)
            {
                _ = this.TriggerWebSocketCommand("暂停");
                _isPausedByMod = true;
            }
            else if (e.NewMenu == null && _isPausedByMod)
            {
                _ = this.TriggerWebSocketCommand("恢复");
                _isPausedByMod = false;
            }
        }

        private async Task TriggerWebSocketCommand(string action)
        {
            // 使用 using 确保 WebSocket 用完后正确释放
            using (var ws = new ClientWebSocket())
            {
                try
                {
                    // CommandWebUI 的 WebSocket 地址固定为 /ws
                    Uri serverUri = new Uri($"ws://{this.Config.ServerIP}:{this.Config.ServerPort}/ws");
                    
                    // 1. 建立连接
                    await ws.ConnectAsync(serverUri, CancellationToken.None);

                    // 2. 将命令转换为字节流
                    // 注意：因为是直接推送到控制台 Reader.PushInput，如果需要前缀请在 config 中配置好，比如 "!cmd>alos.pause" 或 "alos.pause"
                    byte[] commandBytes = Encoding.UTF8.GetBytes(this.Config.Command);
                    ArraySegment<byte> bytesToSend = new ArraySegment<byte>(commandBytes);

                    // 3. 发送命令
                    await ws.SendAsync(bytesToSend, WebSocketMessageType.Text, true, CancellationToken.None);
                    this.Monitor.Log($"[AutoPause] {action} 指令 ({this.Config.Command}) 已通过 WebSocket 发送成功", LogLevel.Info);

                    // 4. 正常关闭连接
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Command Sent", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"[AutoPause] WebSocket 发送失败: {ex.Message}", LogLevel.Error);
                }
            }
        }
    }
}
