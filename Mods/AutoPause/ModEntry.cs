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
            // 直接调用多玩家通信接口发送聊天/指令
            if (Context.IsMultiplayer)
            {
                Game1.multiplayer.sendChatMessage(ModData.ReadWrite.Common, "!cmd>alos.pause");
                this.Monitor.Log($"[AutoPause] {reason}: 通过网络包发送了暂停请求", LogLevel.Info);
            }
        }
    }
}
