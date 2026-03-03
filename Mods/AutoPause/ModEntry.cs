using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace AutoPause
{
    public class ModEntry : Mod
    {
        private bool _isPausedByMod = false;

        public override void Entry(IModHelper helper)
        {
            // 监听菜单状态变化
            helper.Events.Display.MenuChanged += OnMenuChanged;
        }

        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            // 只有在联机模式且自己是客机时才生效
            if (!Context.IsMultiplayer || Context.IsMainPlayer)
                return;

            bool hasNewMenu = e.NewMenu != null;

            if (hasNewMenu && !_isPausedByMod)
            {
                SendPauseCommand("打开界面");
                _isPausedByMod = true;
            }
            else if (!hasNewMenu && _isPausedByMod)
            {
                SendPauseCommand("关闭界面");
                _isPausedByMod = false;
            }
        }

        private void SendPauseCommand(string reason)
        {
            try
            {
                if (Game1.chatBox != null)
                {
                    // 使用 SMAPI 的反射功能强制调用私有方法/设置私有属性
                    // 设置聊天文本
                    this.Helper.Reflection.GetMethod(Game1.chatBox, "setText").Invoke("!cmd>alos.pause");
                    
                    // 模拟按下回车提交聊天
                    // 在 1.6 版本中，textBoxEnter 接受一个 TextBox 参数
                    var textBox = this.Helper.Reflection.GetField<TextBox>(Game1.chatBox, "chatBox").GetValue();
                    if (textBox != null)
                    {
                        this.Helper.Reflection.GetMethod(Game1.chatBox, "textBoxEnter").Invoke(textBox);
                        this.Monitor.Log($"[AutoPause] {reason}: 已通过反射发送 !cmd>alos.pause", LogLevel.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[AutoPause] 发送指令失败: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
