using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace BlindWhaleFarmer
{
    public class ModEntry : Mod
    {
        internal ModConfig Config;
        internal MainThread MainThread;
        internal Observer Observer;
        internal GameActions Actions;
        internal Tools Tools;
        internal PlaySession Session;
        internal Agent Agent;
        internal McpServer Mcp;
        internal readonly Hud Hud = new();

        /// <summary>主线程每帧更新的游戏时间字符串，供后台线程写日志。</summary>
        internal volatile string CurrentStamp = "";

        /// <summary>Game1.isTimePaused 是不是我们打开的（玩家用调试命令自己暂停的不去动）。</summary>
        private bool timeFrozenByUs;
        /// <summary>Game1.paused 是不是我们打开的：MCP 客户端停下后整个游戏暂停，等玩家按键或 AI 回来。</summary>
        private bool pausedByUs;
        private bool mcpWasPlaying;

        /// <summary>玩家按了接管键：AI 的游戏动作一律拒绝，不冻结时间、不自动暂停。再按一次交还。</summary>
        internal volatile bool PlayerInControl;
        /// <summary>AI 在玩时临时关掉的“失去焦点时暂停”原值；null 表示没有改动。</summary>
        private bool? savedPauseWhenOutOfFocus;
        private string transcriptPath;
        private readonly object transcriptLock = new();

        private sealed class SaveData
        {
            public string Notes { get; set; } = "";
        }

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();
            Monitor.Log(Guide.Load(helper.DirectoryPath, Config.GuideFile), LogLevel.Info);
            Guide.SetOverrides(Config.GuideOverrides);
            GameActions.ForageRadius = Math.Clamp(Config.ForageRadius, 0, 10);
            MainThread = new MainThread(Monitor);
            Observer = new Observer(Config, helper.Reflection);
            Actions = new GameActions(helper.Reflection, Observer);
            Agent = new Agent(this);
            Session = new PlaySession(Config);
            Tools = new Tools(MainThread, Actions, notes => Agent.UpdateNotes(notes)) { Session = Session };

            Directory.CreateDirectory(Path.Combine(helper.DirectoryPath, "logs"));
            transcriptPath = Path.Combine(helper.DirectoryPath, "logs", $"transcript-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            PatchChat();
            chatInput = new ChatInput(Monitor);
            GameActions.Mode = GameActions.NormalizeMode(Config.CompanionMode);
            GameActions.ModeChanged = mode =>
            {
                Config.CompanionMode = mode;
                Helper.WriteConfig(Config);
            };

            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += (_, _) =>
            {
                Exits.ClearCache();
                Session.OnDayStarted();
            };
            helper.Events.GameLoop.ReturnedToTitle += (_, _) => Agent.Stop("回到了标题画面");
            helper.Events.GameLoop.GameLaunched += (_, _) => RestorePauseOptionIfNeeded();
            helper.Events.GameLoop.Saved += (_, _) => OnSavedPauseOption();
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Display.RenderedHud += (_, e) => DrawHud(e.SpriteBatch);
            helper.Events.Display.RenderedActiveMenu += (_, e) => DrawHud(e.SpriteBatch);

            helper.ConsoleCommands.Add("dsai_start", "启动 DeepSeek AI。", (_, _) => Agent.Start());
            helper.ConsoleCommands.Add("dsai_stop", "停止 DeepSeek AI。", (_, _) => Agent.Stop("手动停止"));
            helper.ConsoleCommands.Add("dsai_state", "打印当前发送给 AI 的文字状态。", (_, _) =>
            {
                if (!Context.IsWorldReady)
                {
                    Monitor.Log("请先进入存档。", LogLevel.Info);
                    return;
                }
                Monitor.Log("\n" + Observer.Build(), LogLevel.Info);
            });
            helper.ConsoleCommands.Add("dsai_do", "手动执行一个 AI 工具，例如：dsai_do move_to {\"x\":64,\"y\":15}", OnDoCommand);
            helper.ConsoleCommands.Add("dsai_notes", "查看或设置 AI 笔记：dsai_notes [新内容]", (_, args) =>
            {
                if (args.Length > 0)
                    Agent.UpdateNotes(string.Join(" ", args));
                Monitor.Log("AI 笔记：" + Agent.Notes, LogLevel.Info);
            });
            helper.ConsoleCommands.Add("dsai_tokens", "查看本次运行消耗的 token。", (_, _) =>
                Monitor.Log($"输入 {Agent.TotalPromptTokens} tokens，输出 {Agent.TotalCompletionTokens} tokens", LogLevel.Info));

            Monitor.Log($"大肥鱼种白饭 Blind Whale Farmer 已加载。进入存档后按 {Config.ToggleKey} 启动/停止 AI。", LogLevel.Info);

            if (Config.EnableMcp)
            {
                try
                {
                    Mcp = new McpServer(this, Config.McpPort);
                    Mcp.Start();
                    Monitor.Log($"MCP 服务器已启动：http://127.0.0.1:{Config.McpPort}/mcp （WorkBuddy 等 MCP 客户端可以连这个地址来玩）", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    Mcp = null;
                    Monitor.Log($"MCP 服务器启动失败（端口 {Config.McpPort} 可能被占用，可在 config.json 改 McpPort）：{ex.Message}", LogLevel.Warn);
                }
            }
        }

        /// <summary>MCP 客户端最近在调用工具，视为“正在玩”。</summary>
        private bool McpPlaying => Mcp != null && Mcp.IsActive(Config.McpIdleSeconds);

        private void OnDoCommand(string cmd, string[] args)
        {
            if (args.Length == 0)
            {
                Monitor.Log("用法：dsai_do <工具名> [JSON参数]", LogLevel.Info);
                return;
            }
            string name = args[0];
            string json = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "{}";
            Monitor.Log($"执行 {name} {json} …", LogLevel.Info);
            Tools.Execute(name, json).ContinueWith(t =>
                Monitor.Log("结果：" + (t.IsFaulted ? t.Exception?.GetBaseException().Message : t.Result), LogLevel.Info));
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            var data = Helper.Data.ReadJsonFile<SaveData>(NotesFile) ?? new SaveData();
            Agent.Notes = data.Notes ?? "";
            Agent.ClearLog();
            Exits.ClearCache();
            // 每次读档算新的一次游玩，时长重新计
            Session.Reset();
            // 读档会用存档里的选项覆盖 Game1.options
            RestorePauseOptionIfNeeded();
        }

        /// <summary>
        /// 应用从 DSH（dsh-stardew-blind-whale-farmer 插件）推过来的设置，并写回 config.json 让下次启动也生效。
        /// 时长上限变了就重新开始计时（“从现在起玩 60 分钟”）。返回给日志看的说明。
        /// </summary>
        internal string ApplyRemoteSettings(System.Text.Json.Nodes.JsonObject s)
        {
            int oldDays = Config.SessionDays, oldMinutes = Config.SessionMinutes;
            if (s["sessionDays"] is { } d)
                Config.SessionDays = Math.Max(0, d.GetValue<int>());
            if (s["sessionMinutes"] is { } m)
                Config.SessionMinutes = Math.Max(0, m.GetValue<int>());
            if (s["freezeTime"] is { } f)
                Config.McpFreezeTime = f.GetValue<bool>();
            if (s["idleSeconds"] is { } idle)
                Config.McpIdleSeconds = Math.Clamp(idle.GetValue<int>(), 10, 3600);
            if (s["forageRadius"] is { } fr)
                GameActions.ForageRadius = Config.ForageRadius = Math.Clamp(fr.GetValue<int>(), 0, 10);
            if (s["companionMode"] is { } mode)
            {
                string newMode = GameActions.NormalizeMode(mode.GetValue<string>());
                // 这里不在主线程，等下一帧再说（还没进存档就等进了再说）
                modeAnnouncePending |= newMode != GameActions.Mode;
                GameActions.Mode = Config.CompanionMode = newMode;
            }
            if (s["extraInstructions"] is { } extra)
                Config.ExtraInstructions = extra.GetValue<string>() ?? "";
            string guideLog = null;
            if (s["guideFile"] is { } g && (g.GetValue<string>() ?? "") is var guide && guide != Config.GuideFile)
            {
                Config.GuideFile = string.IsNullOrWhiteSpace(guide) ? Guide.DefaultFile : guide;
                guideLog = Guide.Load(Helper?.DirectoryPath ?? "", Config.GuideFile);
            }
            // 玩家在 DSH 设置页改过的攻略（只发改过的篇目；空对象表示全部用文件里的）
            if (s["guides"] is System.Text.Json.Nodes.JsonObject guides)
            {
                Config.GuideOverrides = guides
                    .Where(kv => kv.Value != null && !string.IsNullOrWhiteSpace(kv.Value.GetValue<string>()))
                    .ToDictionary(kv => kv.Key, kv => kv.Value.GetValue<string>());
                Guide.SetOverrides(Config.GuideOverrides);
                if (Config.GuideOverrides.Count > 0)
                    guideLog = (guideLog != null ? guideLog + "；" : "") + $"使用 DSH 里改过的攻略：{string.Join("、", Config.GuideOverrides.Keys)}";
            }
            if (oldDays != Config.SessionDays || oldMinutes != Config.SessionMinutes)
                Session.Reset();
            Helper?.WriteConfig(Config);
            return $"已应用 DSH 设置：每次 {(Config.SessionDays > 0 ? Config.SessionDays + " 个游戏日" : "不限天数")}、"
                + $"{(Config.SessionMinutes > 0 ? Config.SessionMinutes + " 分钟" : "不限时长")}，思考时冻结时间={Config.McpFreezeTime}"
                + (guideLog != null ? "；" + guideLog : "");
        }

        private string NotesFile => Path.Combine("data", Constants.SaveFolderName + ".json");

        internal void SaveNotes()
        {
            string notes = Agent.Notes;
            // 后台线程也可能调用，写文件本身是线程安全的
            try
            {
                Helper.Data.WriteJsonFile(NotesFile, new SaveData { Notes = notes });
            }
            catch (Exception ex)
            {
                Monitor.Log("保存笔记失败：" + ex.Message, LogLevel.Warn);
            }
        }

        internal void WriteTranscript(string line)
        {
            try
            {
                lock (transcriptLock)
                    File.AppendAllText(transcriptPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 日志写不进去不影响游戏
            }
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            // AI 停下后暂停的游戏：玩家按任意键接手
            if (pausedByUs)
                SetIdlePaused(false);
            if (e.Button == Config.ChatInputKey && Context.IsWorldReady && Context.IsMultiplayer)
            {
                chatInput.Open();
                return;
            }
            if (e.Button == Config.TakeOverKey && Context.IsWorldReady)
            {
                SetPlayerInControl(!PlayerInControl);
                return;
            }
            if (e.Button != Config.ToggleKey || !Context.IsWorldReady)
                return;
            if (Agent.Running)
                Agent.Stop("按下了 " + Config.ToggleKey);
            else
                Agent.Start();
        }

        private sealed class PauseOptionData
        {
            public bool RestorePauseWhenOutOfFocus { get; set; }
        }

        private const string PauseOptionKey = "pause-option";

        private void KeepRunningInBackground(bool playing)
        {
            if (Game1.options == null)
                return;
            if (playing && savedPauseWhenOutOfFocus == null && Game1.options.pauseWhenOutOfFocus)
            {
                savedPauseWhenOutOfFocus = true;
                Game1.options.pauseWhenOutOfFocus = false;
                // 选项会跟着存档写盘；先记下来，万一带着 false 存了档或退出，下次启动/读档时改回去
                Helper.Data.WriteGlobalData(PauseOptionKey, new PauseOptionData { RestorePauseWhenOutOfFocus = true });
            }
            else if (!playing && savedPauseWhenOutOfFocus != null)
            {
                Game1.options.pauseWhenOutOfFocus = savedPauseWhenOutOfFocus.Value;
                savedPauseWhenOutOfFocus = null;
            }
        }

        /// <summary>上次改过“失去焦点时暂停”却没能以原值存盘时，把它改回开启。</summary>
        private void RestorePauseOptionIfNeeded()
        {
            if (savedPauseWhenOutOfFocus == null && Game1.options != null
                && Helper.Data.ReadGlobalData<PauseOptionData>(PauseOptionKey)?.RestorePauseWhenOutOfFocus == true)
                Game1.options.pauseWhenOutOfFocus = true;
        }

        private void OnSavedPauseOption()
        {
            // 这次存档写进去的已经是原值，标记可以清掉了
            if (savedPauseWhenOutOfFocus == null)
                Helper.Data.WriteGlobalData<PauseOptionData>(PauseOptionKey, null);
        }

        /// <summary>联机时把别人说的话记下来，告诉 AI。打不上补丁只是听不到聊天，不影响别的。</summary>
        private ChatInput chatInput;
        private volatile bool modeAnnouncePending;

        private void PatchChat()
        {
            try
            {
                new Harmony("Darian.BlindWhaleFarmer").Patch(
                    AccessTools.Method(typeof(ChatBox), nameof(ChatBox.receiveChatMessage)),
                    postfix: new HarmonyMethod(typeof(ModEntry), nameof(AfterChatMessage)));
            }
            catch (Exception ex)
            {
                Monitor.Log("聊天补丁失败，AI 听不到游戏里的聊天：" + ex, LogLevel.Warn);
            }
        }

        private static void AfterChatMessage(long sourceFarmer, int chatKind, string message) =>
            GameActions.OnChatMessage(sourceFarmer, chatKind, message);

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            bool ready = Context.IsWorldReady;
            if (ready)
                CurrentStamp = Util.Stamp();

            // 玩家接管时 AI 可能还在发（会被拒绝的）请求，不算在玩：不冻结时间、不后台运行
            bool mcpPlaying = McpPlaying && !PlayerInControl;
            UpdateIdlePause(ready, mcpPlaying);

            if (chatInput.TakeLine() is string said && ready && Context.IsMultiplayer)
                Game1.chatBox?.textBoxEnter(said);
            if (modeAnnouncePending && ready)
            {
                modeAnnouncePending = false;
                if (Context.IsMultiplayer)
                    GameActions.AnnounceMode("玩家在 DSH 设置里");
            }

            MainThread.Tick(ready);
            if (ready)
                FishingAssist.Tick(MainThread.IsBusy);

            // 内置 AI 玩到本次时长上限就自己停下（MCP 客户端那边靠工具的拒绝信息收尾）
            if (ready && Agent.Running && !MainThread.IsBusy && e.IsMultipleOf(60) && Session.Ended)
                Agent.Stop("本次游玩时间到了");

            // 用 MCP 客户端玩时游戏窗口通常在后台；默认的“失去焦点时暂停”会让 Game1.Update 直接返回，人物走不动
            KeepRunningInBackground(ready && (Agent.Running || mcpPlaying || MainThread.IsBusy));
            // 剧情标记卡住（eventUp 还开着、却没有剧情/界面/切场景）时自己解开；只在 AI 玩的时候管，不碰玩家自己玩
            if (ready && (Agent.Running || mcpPlaying || MainThread.IsBusy))
                Actions.RecoverStuckEvent(Monitor);
            if (!ready || (!Agent.Running && !mcpPlaying))
            {
                SetTimeFrozen(false);
                return;
            }

            if (Config.AutoConfirmNightMenus)
                Actions.AutoConfirmNightMenus();

            // 模型思考时冻结时钟：内置 AI 在等回复，或者 MCP 客户端在两次调用之间思考
            bool thinking = Agent.Running ? Config.FreezeTimeWhileThinking && Agent.IsThinking
                                          : Config.McpFreezeTime && mcpPlaying;
            SetTimeFrozen(thinking && !MainThread.IsBusy);
        }

        /// <summary>
        /// MCP 客户端停下（超过 McpIdleSeconds 没有调用：模型出错结束了回合、断开、或者想太久）时，用 Game1.paused 把整个游戏停住。
        /// 只冻结时钟不够：游戏窗口在前台时怪物照样打人，在矿洞里被打晕会丢东西。AI 再次调用或玩家按任意键时继续。
        /// </summary>
        private void UpdateIdlePause(bool ready, bool mcpPlaying)
        {
            bool stopped = mcpWasPlaying && !mcpPlaying && !Agent.Running;
            mcpWasPlaying = mcpPlaying;
            if (pausedByUs && (!ready || mcpPlaying || Agent.Running))
                SetIdlePaused(false);
            else if (stopped && ready && Config.PauseWhenAiStops && !PlayerInControl && !Context.IsMultiplayer && !Game1.paused && !MainThread.IsBusy)
                SetIdlePaused(true);
        }

        /// <summary>
        /// 接管：中止 AI 正在执行的动作（比如卡住的下矿），停下内置 AI，之后 AI 的游戏动作都会被拒绝，玩家自己操作。
        /// 交还：AI 下次调用就能继续玩。
        /// </summary>
        private void SetPlayerInControl(bool inControl)
        {
            PlayerInControl = inControl;
            mcpWasPlaying = false;
            if (inControl)
            {
                MainThread.CancelCurrent($"玩家按 {Config.TakeOverKey} 接管了游戏，动作已中止");
                if (Agent.Running)
                    Agent.Stop($"玩家按 {Config.TakeOverKey} 接管了游戏");
                SetTimeFrozen(false);
                Game1.player.CanMove = true;
                Hud.SetStatus($"你接管了游戏，AI 的动作会被拒绝。再按 {Config.TakeOverKey} 交还给 AI");
                Monitor.Log($"玩家按 {Config.TakeOverKey} 接管了游戏，已中止 AI 的动作。", LogLevel.Info);
            }
            else
            {
                Hud.SetStatus("");
                Monitor.Log($"玩家按 {Config.TakeOverKey} 把游戏交还给 AI。", LogLevel.Info);
            }
        }

        private void SetIdlePaused(bool paused)
        {
            Game1.paused = paused;
            pausedByUs = paused;
            Hud.SetStatus(paused ? "AI 停下了，游戏已暂停。按任意键接手，或让 AI 继续" : "");
            if (paused)
                Monitor.Log($"MCP 客户端 {Config.McpIdleSeconds} 秒没有动作，已暂停游戏（按任意键继续）。", LogLevel.Info);
        }

        /// <summary>
        /// 用游戏自己的 Game1.isTimePaused 暂停时钟（shouldTimePass 会返回 false，时钟计数不再累加）。
        /// 以前是每帧把 gameTimeInterval 改回冻结时的值，但游戏同一帧里会先加上这帧耗时再判断满不满 7 秒：
        /// 冻结值接近 7 秒、或者某帧卡顿时，会每帧都跳 10 分钟，几十秒就从中午跳到凌晨 2 点。
        /// </summary>
        private void SetTimeFrozen(bool frozen)
        {
            if (frozen == timeFrozenByUs)
                return;
            // 多人模式下时间由 netWorldState 控制，这个开关不起作用，也不去动它
            if (frozen && (Context.IsMultiplayer || Game1.isTimePaused))
                return;
            Game1.isTimePaused = frozen;
            timeFrozenByUs = frozen;
        }

        private void DrawHud(SpriteBatch b)
        {
            if (!Config.ShowHud || !Context.IsWorldReady)
                return;
            try
            {
                bool mcp = !Agent.Running && McpPlaying && !PlayerInControl;
                Hud.Draw(b, Agent.Running || mcp, Agent.IsThinking || (mcp && !MainThread.IsBusy), mcp);
            }
            catch (Exception ex)
            {
                Monitor.LogOnce("HUD 绘制失败：" + ex.Message, LogLevel.Warn);
            }
        }
    }

    /// <summary>屏幕左上角的小面板：显示 AI 状态、想法和当前动作。</summary>
    internal sealed class Hud
    {
        private readonly object sync = new();
        private string status = "";
        private string thought = "";
        private string action = "";
        private HashSet<char> glyphs;

        public void SetStatus(string s)
        {
            lock (sync)
                status = s;
        }

        public void SetThought(string s)
        {
            lock (sync)
                thought = s;
        }

        public void SetAction(string s)
        {
            lock (sync)
                action = s;
        }

        public void Draw(SpriteBatch b, bool running, bool thinking, bool viaMcp = false)
        {
            string s, t, a;
            lock (sync)
            {
                s = status;
                t = thought;
                a = action;
            }
            if (!running && string.IsNullOrEmpty(s))
                return;

            var font = Game1.smallFont;
            glyphs ??= new HashSet<char>(font.Characters);
            const int width = 520;
            var lines = new List<string>();
            if (viaMcp)
                lines.Add(thinking ? "[MCP 客户端思考中，时间已冻结]" : "[MCP 客户端操作中]");
            else
                lines.Add((running ? (thinking ? "[AI 思考中] " : "[AI 运行中] ") : "[AI 已停止] ") + s);
            if (running)
            {
                lines.AddRange(Wrap(font, "想法：" + t, width - 24, 4));
                lines.AddRange(Wrap(font, "动作：" + a, width - 24, 2));
            }

            float lineH = font.LineSpacing;
            int height = (int)(lines.Count * lineH) + 20;
            int x = 8, y = 8;
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x, y, width, height, Color.White * 0.85f, 1f, false);
            float cy = y + 10;
            foreach (var line in lines)
            {
                b.DrawString(font, Sanitize(line), new Vector2(x + 12, cy), Game1.textColor);
                cy += lineH;
            }
        }

        /// <summary>去掉当前字体里没有的字符（英文版游戏字体不含中文，会崩溃）。</summary>
        private string Sanitize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(glyphs.Contains(c) ? c : (c < 128 ? ' ' : '?'));
            return sb.ToString();
        }

        private IEnumerable<string> Wrap(SpriteFont font, string text, int maxWidth, int maxLines)
        {
            text = Sanitize((text ?? "").Replace("\r", "").Replace("\n", " "));
            var result = new List<string>();
            var cur = new StringBuilder();
            foreach (char c in text)
            {
                cur.Append(c);
                if (font.MeasureString(cur).X > maxWidth)
                {
                    cur.Length--;
                    result.Add(cur.ToString());
                    cur.Clear();
                    cur.Append(c);
                    if (result.Count >= maxLines)
                        break;
                }
            }
            if (result.Count < maxLines && cur.Length > 0)
                result.Add(cur.ToString());
            else if (result.Count >= maxLines)
                result[maxLines - 1] = result[maxLines - 1].TrimEnd() + "…";
            return result;
        }
    }
}
