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
            // 只有在联机模式且自己是客机 (Farmhand) 时才生效
            if (!Context.IsMultiplayer || Context.IsMainPlayer)
                return;

            // e.NewMenu 是新打开的菜单，e.OldMenu 是关掉的菜单
            bool hasNewMenu = e.NewMenu != null;

            if (hasNewMenu && !_isPausedByMod)
            {
                // 打开了菜单（如背包、箱子、对话、商店等）
                SendPauseCommand("打开界面，请求暂停");
                _isPausedByMod = true;
            }
            else if (!hasNewMenu && _isPausedByMod)
            {
                // 关闭了所有菜单回到游戏界面
                SendPauseCommand("关闭界面，恢复游戏");
                _isPausedByMod = false;
            }
        }

        private void SendPauseCommand(string reason)
        {
            // 在星露谷中，直接让角色在聊天栏输入指令
            // !cmd>alos.pause 是 ALOS 插件的专用指令
            Game1.chatBox?.activate();
            Game1.chatBox?.setText("!cmd>alos.pause");
            Game1.chatBox?.chatMessage();
            
            this.Monitor.Log($"[AutoPause] {reason}: 已自动发送 !cmd>alos.pause", LogLevel.Info);
        }
    }
}
