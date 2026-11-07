using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace BlindWhaleFarmer
{
    /// <summary>AI 主循环：观察 → 调用 DeepSeek → 执行工具 → 记录 → 循环。运行在后台线程。</summary>
    internal sealed class Agent
    {
        internal const string Rules = @"你是一个正在玩《星露谷物语》(Stardew Valley) 的 AI 玩家。你看不到画面，游戏状态会以文字形式给你：基本状态、当前界面、背包、出口、人物、作物、附近物体列表和一张以你为中心的字符地图。你通过调用工具来行动。

## 坐标
- 每个地点是一张格子地图，x 向右增大，y 向下增大。所有工具里的 x,y 都是这个地点的格子坐标。
- 优先使用“附近物体”和“作物”列表里给出的精确坐标；字符地图用来理解大致布局（哪里是空地、水、建筑）。

## 行动原则
1. 每一步先用一两句中文说明你的判断和打算（简短），然后调用一个或几个工具。不要只说话不调用工具。
2. 有界面打开（对话框、信件、商店、升级）时，先处理界面：dialogue / close_menu / buy / choose_profession 等。剧情事件中用 dialogue 推进或 skip_event 跳过；节日用 festival 工具参加（enter → greet → start），不想参加就 festival leave 离开。
3. 优先使用批量工具：water_crops、harvest_crops、till_area、plant_seeds、clear_area，比一格一格操作高效得多。
4. 工具返回“失败”时，读原因并换个做法，不要原样重复同一个失败动作超过两次。
5. 体力低于 20 时不要再干重活：吃东西（eat）或者回家睡觉。晚上 23:00 后要准备 sleep，绝不能拖到凌晨 2 点；状态和动作结果里出现“该回家了/必须马上 sleep”时立刻执行 sleep。
6. 用 update_notes 记录长期目标和关键信息（例如已开垦的农田坐标、商店营业时间、谁喜欢什么礼物）。笔记会一直提供给你。
7. 身边有会打人的怪时（状态或结果里出现“⚠ 附近有 N 只怪”“干活时被攻击了”），先用 fight 清怪再继续干活；生命低于 30% 又没有回血食物就先撤退离开这张地图。
8. 背包满了或出门前：用 tidy_backpack 一键整理（低级鱼竿、多余武器、矿石鱼等存进农场箱子），或 store_items 存指定的东西；再把今天要换钱的作物 ship_items 放进出货箱；不要为了腾地方第一时间 sell 掉，收集包要的材料尤其不能卖。
9. 出现剧情或对话框时，用 dialogue 推进或 skip_event 跳过；不要用 wait 等剧情结束（剧情里时间是停的）。
10. 玩家可能给这次游玩设了时长（状态最前面的【本次游玩…】）：快到上限时把手上的活收尾、留出回家睡觉的时间；提示时间到了就 sleep，然后向玩家汇报这次做了什么、下次打算做什么。
11. 钱要花出去才会变多：状态里出现【花钱建议】时，按攻略“花钱规划”的顺序挑最能提高每天收入的买（背包、工具、鸡舍、当季种子……），只留 1000g 左右备用；把买了什么、为什么记进 update_notes。
12. 联机时结果里出现“💬 某某对你说：”是其他玩家在游戏里和你说话（可能是没有声调的拼音，比如 qu haitan diaoyu；意思拿不准就用 say 问一句，不要乱猜）：优先回应，用 say 回一句（你写在回复里的字对方看不到，只有 say 说的话游戏里才看得到），能做的就照做（【联机模式】是“自己玩”时，帮不帮你自己决定）。之后怎么安排看状态里的【联机模式】，别人要换模式就用 set_mode（你自己就能换，不用去设置里）。上床后要所有玩家都上床才会到第二天。钱大家共用，花 1000g 以上先用 say 问一声。";

        /// <summary>完整系统提示：操作规则（跟着工具代码走）+ 攻略文件（guide/*.md，玩家可以自己改）。</summary>
        internal static string SystemPrompt => string.IsNullOrWhiteSpace(Guide.Text) ? Rules : Rules + "\n\n" + Guide.Text;

        private readonly ModEntry mod;
        private readonly object logLock = new();
        private readonly List<string> log = new();
        private CancellationTokenSource cts;
        private Task loop;
        private DeepSeekClient client;
        private int consecutiveErrors;

        public volatile bool IsThinking;
        public string Notes = "";
        public long TotalPromptTokens;
        public long TotalCompletionTokens;

        public bool Running => loop != null && !loop.IsCompleted;

        public Agent(ModEntry mod)
        {
            this.mod = mod;
        }

        public void Start()
        {
            if (Running)
                return;
            if (string.IsNullOrWhiteSpace(DeepSeekClient.ResolveApiKey(mod.Config)))
            {
                mod.Hud.SetStatus("没有配置 API Key：请在 Mods/BlindWhaleFarmer/config.json 里填写 ApiKey");
                mod.Monitor.Log("没有配置 DeepSeek API Key（config.json 的 ApiKey，或环境变量 DEEPSEEK_API_KEY）。", LogLevel.Error);
                return;
            }
            client?.Dispose();
            client = new DeepSeekClient(mod.Config);
            cts = new CancellationTokenSource();
            consecutiveErrors = 0;
            mod.Hud.SetStatus("AI 已启动");
            mod.Monitor.Log("AI 已启动。", LogLevel.Info);
            loop = Task.Run(() => RunLoop(cts.Token));
        }

        public void Stop(string reason)
        {
            if (!Running)
                return;
            cts?.Cancel();
            mod.MainThread.Cancel("AI 已停止");
            IsThinking = false;
            mod.Hud.SetStatus("AI 已停止：" + reason);
            mod.Monitor.Log("AI 已停止：" + reason, LogLevel.Info);
        }

        public void AddLog(string line)
        {
            lock (logLock)
            {
                log.Add(line);
                if (log.Count > 200)
                    log.RemoveRange(0, log.Count - 200);
            }
            mod.WriteTranscript(line);
        }

        public void ClearLog()
        {
            lock (logLock)
                log.Clear();
        }

        private string RecentLog()
        {
            lock (logLock)
            {
                var lines = log.Skip(Math.Max(0, log.Count - mod.Config.HistoryLines)).ToList();
                return lines.Count == 0 ? "（还没有行动记录）" : string.Join("\n", lines);
            }
        }

        public string UpdateNotes(string notes)
        {
            Notes = notes.Length > 1200 ? notes.Substring(0, 1200) : notes;
            mod.SaveNotes();
            return "成功：笔记已更新";
        }

        private JsonArray BuildMessages(string observation)
        {
            string system = SystemPrompt;
            if (!string.IsNullOrWhiteSpace(mod.Config.ExtraInstructions))
                system += "\n\n## 玩家给你的额外要求\n" + mod.Config.ExtraInstructions.Trim();

            var user = new StringBuilder();
            user.AppendLine("# 你的长期笔记");
            user.AppendLine(string.IsNullOrWhiteSpace(Notes) ? "（空。建议先用 update_notes 写下目标和计划）" : Notes);
            user.AppendLine();
            user.AppendLine("# 最近的行动记录（旧 → 新）");
            user.AppendLine(RecentLog());
            user.AppendLine();
            user.AppendLine("# 当前游戏状态");
            user.AppendLine(observation);
            user.AppendLine();
            user.AppendLine("请简短说明判断和计划，然后调用工具行动。");

            return new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user.ToString() }
            };
        }

        /// <summary>当前是否适合让 AI 思考（世界就绪，不在睡觉过场里）。</summary>
        private static bool CanAct()
        {
            if (!Context.IsWorldReady || Game1.newDay || Game1.fadeToBlack && Game1.activeClickableMenu == null)
                return false;
            // 结算/普通升级界面由模组自动确认，不浪费请求
            if (Game1.activeClickableMenu is ShippingMenu || Game1.activeClickableMenu is SaveGameMenu)
                return false;
            if (Game1.activeClickableMenu is LevelUpMenu lvl && !lvl.isProfessionChooser)
                return false;
            return true;
        }

        private async Task RunLoop(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    bool ready = await mod.MainThread.Invoke(CanAct).ConfigureAwait(false);
                    if (!ready)
                    {
                        await Task.Delay(1000, ct).ConfigureAwait(false);
                        continue;
                    }

                    string observation;
                    try
                    {
                        observation = await mod.MainThread.Invoke(() => mod.Observer.Build()).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        mod.Monitor.Log("读取游戏状态失败：" + ex, LogLevel.Warn);
                        await Task.Delay(2000, ct).ConfigureAwait(false);
                        continue;
                    }
                    var messages = BuildMessages(observation);
                    if (mod.Config.LogPrompts)
                        mod.WriteTranscript("=== PROMPT ===\n" + messages[1]!["content"]!.GetValue<string>());

                    ChatReply reply;
                    IsThinking = true;
                    mod.Hud.SetStatus("思考中…");
                    try
                    {
                        reply = await client.Chat(messages, mod.Tools.Schema, ct).ConfigureAwait(false);
                        consecutiveErrors = 0;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        consecutiveErrors++;
                        mod.Monitor.Log("调用 DeepSeek 失败：" + ex.Message, LogLevel.Warn);
                        mod.Hud.SetStatus("调用 DeepSeek 失败：" + ex.Message);
                        if (consecutiveErrors >= 5)
                        {
                            Stop("连续 5 次请求失败，请检查网络和 API Key");
                            return;
                        }
                        await Task.Delay(5000 * consecutiveErrors, ct).ConfigureAwait(false);
                        continue;
                    }
                    finally
                    {
                        IsThinking = false;
                    }

                    TotalPromptTokens += reply.PromptTokens;
                    TotalCompletionTokens += reply.CompletionTokens;

                    string stamp = mod.CurrentStamp;
                    string thought = reply.Content?.Trim() ?? "";
                    if (thought.Length > 0)
                    {
                        mod.Hud.SetThought(thought);
                        AddLog($"[{stamp}] 想法：{OneLine(thought, 200)}");
                    }

                    if (reply.ToolCalls.Count == 0)
                    {
                        AddLog($"[{stamp}] （没有调用工具。提醒自己：必须调用工具才能行动）");
                        await Task.Delay(1000, ct).ConfigureAwait(false);
                        continue;
                    }

                    foreach (var call in reply.ToolCalls.Take(Math.Max(1, mod.Config.MaxActionsPerStep)))
                    {
                        if (ct.IsCancellationRequested)
                            break;
                        string args = OneLine(call.Arguments ?? "{}", 200);
                        mod.Hud.SetAction($"{call.Name} {args}");
                        string result = await mod.Tools.Execute(call.Name, call.Arguments).ConfigureAwait(false);
                        AddLog($"[{mod.CurrentStamp}] {call.Name}({args}) → {OneLine(result, 400)}");
                        mod.Hud.SetAction($"{call.Name} → {OneLine(result, 80)}");

                        bool failed = !result.StartsWith("成功");
                        bool menuOpen = await mod.MainThread.Invoke(() => Game1.activeClickableMenu != null || Game1.eventUp).ConfigureAwait(false);
                        // 失败或者弹出了界面，剩下的计划可能已经不合适了，重新观察
                        if (failed || (menuOpen && call.Name is not ("dialogue" or "buy" or "sell" or "open_shop" or "menu_click")))
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常停止
            }
            catch (Exception ex)
            {
                mod.Monitor.Log("AI 主循环出错：" + ex, LogLevel.Error);
                mod.Hud.SetStatus("AI 出错停止：" + ex.Message);
            }
            finally
            {
                IsThinking = false;
            }
        }

        private static string OneLine(string s, int max)
        {
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
