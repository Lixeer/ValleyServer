using System;
using System.Collections.Generic;
using System.Linq;

namespace BlindWhaleFarmer
{
    /// <summary>
    /// “这一次玩多久”：按游戏日（SessionDays）和现实分钟（SessionMinutes）限制一次游玩。
    /// 从读档后的第一个动作开始计；到了上限就拒绝游戏动作，让 AI 收尾并向玩家汇报。
    /// 内置 AI（F8）和 MCP 客户端共用同一份计时。
    /// </summary>
    internal sealed class PlaySession
    {
        /// <summary>时间到了以后还允许的工具：处理界面、记笔记；分钟用完时另外允许 sleep 回家存档。</summary>
        private static readonly HashSet<string> AlwaysAllowed = new()
        {
            "dialogue", "close_menu", "menu_click", "skip_event", "choose_profession", "leave_festival", "update_notes"
        };

        private readonly ModConfig config;
        private readonly object sync = new();
        private DateTime? startedUtc;
        private int daysCompleted;
        /// <summary>分钟用完时已经过了几天；之后再睡一觉（天数变多）就彻底结束。-1 表示分钟还没用完。</summary>
        private int minutesUpAtDay = -1;

        public PlaySession(ModConfig config)
        {
            this.config = config;
        }

        private bool HasLimit => config.SessionDays > 0 || config.SessionMinutes > 0;

        private double Minutes => startedUtc.HasValue ? (DateTime.UtcNow - startedUtc.Value).TotalMinutes : 0;

        /// <summary>读档、或者在 DSH 里改了时长时重新开始计。</summary>
        public void Reset()
        {
            lock (sync)
            {
                startedUtc = null;
                daysCompleted = 0;
                minutesUpAtDay = -1;
            }
        }

        /// <summary>每次执行工具时调用：第一次调用开始计时。</summary>
        public void Touch()
        {
            lock (sync)
                startedUtc ??= DateTime.UtcNow;
        }

        /// <summary>新的一天开始（睡醒）时调用。</summary>
        public void OnDayStarted()
        {
            lock (sync)
            {
                if (startedUtc.HasValue)
                    daysCompleted++;
            }
        }

        /// <summary>这次游玩是不是已经彻底结束（不只是“该回家睡觉”）。</summary>
        public bool Ended
        {
            get
            {
                lock (sync)
                {
                    UpdateMinutes();
                    return (config.SessionDays > 0 && daysCompleted >= config.SessionDays)
                        || (minutesUpAtDay >= 0 && daysCompleted > minutesUpAtDay);
                }
            }
        }

        private void UpdateMinutes()
        {
            if (config.SessionMinutes > 0 && minutesUpAtDay < 0 && Minutes >= config.SessionMinutes)
                minutesUpAtDay = daysCompleted;
        }

        /// <summary>执行工具前检查：返回拒绝理由，允许时返回 null。</summary>
        public string Gate(string tool)
        {
            if (!HasLimit || AlwaysAllowed.Contains(tool))
                return null;
            lock (sync)
            {
                UpdateMinutes();
                if (Ended)
                    return $"失败：本次游玩已经结束（{Describe()}）。不要再执行游戏动作，向玩家汇报这次做了什么、下次打算做什么。"
                        + "玩家可以在 DSH「插件」页 dsh-stardew-blind-whale-farmer 的配置里（或 config.json）调整时长，重新读档就开始新的一次。";
                if (minutesUpAtDay >= 0 && tool != "sleep")
                    return $"失败：本次设定的 {config.SessionMinutes} 分钟游玩时间到了。现在请执行 sleep（会自动回家睡觉、存档），然后向玩家汇报这次做了什么。";
            }
            return null;
        }

        private string Describe()
        {
            var parts = new List<string>();
            if (config.SessionDays > 0)
                parts.Add($"游戏日 {Math.Min(daysCompleted, config.SessionDays)}/{config.SessionDays} 天");
            if (config.SessionMinutes > 0)
                parts.Add($"已玩 {(int)Minutes}/{config.SessionMinutes} 分钟");
            return string.Join("，", parts);
        }

        /// <summary>给 AI 看的进度说明；没设上限时返回 null。</summary>
        public string Status()
        {
            if (!HasLimit)
                return null;
            lock (sync)
            {
                UpdateMinutes();
                if (Ended)
                    return $"本次游玩已经结束（{Describe()}）：不要再执行游戏动作，向玩家汇报。";
                if (minutesUpAtDay >= 0)
                    return $"本次游玩时间到了（{Describe()}）：马上 sleep 回家存档，然后汇报。";
                string day = config.SessionDays > 0 ? $"今天是本次第 {daysCompleted + 1}/{config.SessionDays} 个游戏日" : null;
                string min = config.SessionMinutes > 0 ? $"已玩 {(int)Minutes}/{config.SessionMinutes} 分钟" : null;
                return "本次游玩：" + string.Join("，", new[] { day, min }.Where(s => s != null))
                    + "。快到上限时把今天的活收尾、留时间回家睡觉。";
            }
        }
    }
}
