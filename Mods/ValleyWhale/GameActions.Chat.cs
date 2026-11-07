using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace BlindWhaleFarmer
{
    internal sealed partial class GameActions
    {
        // ───────────────────────── 联机聊天 ─────────────────────────

        internal const string ModeSolo = "solo", ModeCommand = "command", ModeHybrid = "hybrid";

        /// <summary>联机时 AI 怎么安排自己。由 ModEntry 按配置设置，也可以在聊天里说一句切换。</summary>
        public static string Mode = ModeHybrid;

        /// <summary>聊天里切换了模式：ModEntry 记到 config.json。</summary>
        public static Action<string> ModeChanged;

        internal static string NormalizeMode(string mode) => mode is ModeSolo or ModeCommand ? mode : ModeHybrid;

        /// <summary>聊天里切换模式的话（去掉空格和标点、转小写后整句相等才算；拼音也认）。</summary>
        private static readonly Dictionary<string, string> ModePhrases = new()
        {
            ["你自己玩吧"] = ModeSolo, ["你自己玩"] = ModeSolo, ["自己玩吧"] = ModeSolo, ["自己玩"] = ModeSolo,
            ["nizijiwanba"] = ModeSolo, ["nizijiwan"] = ModeSolo, ["zijiwanba"] = ModeSolo, ["zijiwan"] = ModeSolo,
            ["听我的"] = ModeCommand, ["听我指挥"] = ModeCommand, ["主要听指令"] = ModeCommand, ["zhuyaotingzhiling"] = ModeCommand, ["tingwode"] = ModeCommand, ["tingwozhihui"] = ModeCommand,
            ["指令做完接着自己玩"] = ModeHybrid, ["zhilingzuowanjiezhezijiwan"] = ModeHybrid, ["做完自己玩"] = ModeHybrid, ["做完接着玩"] = ModeHybrid, ["zuowanzijiwan"] = ModeHybrid, ["zuowanjiezhewan"] = ModeHybrid,
        };

        private static string ModeName(string mode) => mode switch
        {
            ModeSolo => "自己玩",
            ModeCommand => "主要听指令",
            _ => "指令做完接着自己玩",
        };

        private static string ModeAdvice() => Mode switch
        {
            ModeSolo => "按你自己的计划玩，不用放下手里的事。别人说话回一句，来不来帮忙看你自己的心情",
            ModeCommand => "主要听指令：有人吩咐就做，做完用 say 汇报，然后用 follow 或 wait_chat 等下一句，不要自己找事做",
            _ => "有人吩咐就先做，做完用 say 汇报，然后接着按自己的计划玩",
        };

        /// <summary>有人说话、而且现在的模式要放下手里的事去听。</summary>
        internal static bool ChatWantsAttention() =>
            incomingChat.Count > 0 && Mode != ModeSolo && Context.IsMultiplayer;

        /// <summary>别人说的、还没告诉 AI 的话。只在主线程读写（游戏收消息也在主线程）。</summary>
        private static readonly List<string> incomingChat = new();

        /// <summary>
        /// 游戏 ChatBox.receiveChatMessage 的后置补丁：记下别的玩家说的话（公开和私聊），
        /// 自己说的、系统提示都不算。AI 可能好几步才看一次，太旧的丢掉。
        /// </summary>
        internal static void OnChatMessage(long sourceFarmer, int chatKind, string message)
        {
            if (sourceFarmer == Game1.player?.UniqueMultiplayerID || chatKind is not (ChatBox.chatMessage or ChatBox.privateMessage))
                return;
            var from = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == sourceFarmer);
            string name = from?.Name ?? "其他玩家";
            string phrase = new string((message ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (ModePhrases.TryGetValue(phrase, out string mode))
            {
                Mode = mode;
                ModeChanged?.Invoke(mode);
                AnnounceMode(name + " ");
            }
            incomingChat.Add($"💬 {name}{(from?.IsMainPlayer == true ? "（主机）" : "")}对你说：{message}");
            if (incomingChat.Count > 20)
                incomingChat.RemoveAt(0);
        }

        /// <summary>模式变了：在聊天里说一声，也告诉 AI。who 是切换的人（比如“某某 ”）。主线程调用。</summary>
        internal static void AnnounceMode(string who)
        {
            incomingChat.Add($"（{who}把联机模式切换成了“{ModeName(Mode)}”）");
            Game1.chatBox?.textBoxEnter($"好的，现在是“{ModeName(Mode)}”模式");
        }

        /// <summary>取出还没告诉 AI 的聊天（没有返回 null）。</summary>
        public static string TakeChat()
        {
            if (incomingChat.Count == 0)
                return null;
            string text = string.Join("\n", incomingChat);
            incomingChat.Clear();
            chatAwaitingReply |= text.Contains("💬");
            return text;
        }

        /// <summary>AI 收到别人的话以后还没用 say 回。AI 写在回复里的字只有 DSH 里看得到，游戏里的人看不到。</summary>
        private static bool chatAwaitingReply;

        /// <summary>收到话之后下一个动作还没 say：提醒一次（没有要提醒的返回 null）。</summary>
        public static string TakeReplyReminder()
        {
            if (!chatAwaitingReply)
                return null;
            chatAwaitingReply = false;
            return "⚠ 刚才别人对你说的话，你还没用 say 回。写在回复里的字对方看不到，只有 say 说的话游戏里才看得到，现在补一句";
        }

        /// <summary>联机时要跟的人：有主机跟主机（AI 自己是主机时跟第一个别的玩家）。</summary>
        private static Farmer OtherPlayer() =>
            Game1.getOnlineFarmers().Where(f => !f.IsLocalPlayer).OrderBy(f => f.IsMainPlayer ? 0 : 1).FirstOrDefault();

        /// <summary>状态里显示的“别的玩家在哪”（单人时返回 null）。</summary>
        public static string OtherPlayersLine()
        {
            if (!Context.IsMultiplayer)
                return null;
            var others = Game1.getOnlineFarmers().Where(f => !f.IsLocalPlayer)
                .Select(f => $"{f.Name}{(f.IsMainPlayer ? "（主机）" : "")}在 {f.currentLocation?.NameOrUniqueName ?? "未知地点"} ({f.TilePoint.X},{f.TilePoint.Y})").ToList();
            bool ccLocked = !Game1.IsMasterGame && Game1.stats.DaysPlayed >= 5 && !Game1.MasterPlayer.mailReceived.Contains("ccDoorUnlock")
                && !Game1.MasterPlayer.mailReceived.Contains("JojaMember");
            return (others.Count > 0 ? "联机中，其他玩家：" + string.Join("、", others) : "联机中，现在只有你在线")
                + $"\n【联机模式：{ModeName(Mode)}】{ModeAdvice()}。别人要换模式就用 set_mode"
                + (ccLocked ? "\n" + Knowledge.FarmhandCommunityCenterNote : "");
        }

        /// <summary>别人用自己的话要换模式时（“换成自己玩”），整句匹配不上，由 AI 听懂了再切。</summary>
        public IEnumerable<object> SetMode(string mode)
        {
            if (!Context.IsMultiplayer)
            {
                yield return ActionResult.Fail("只有联机时才有联机模式");
                yield break;
            }
            string target = (mode ?? "").Trim().ToLowerInvariant();
            target = new[] { ModeSolo, ModeCommand, ModeHybrid }.FirstOrDefault(m => m == target || ModeName(m) == target);
            if (target == null)
            {
                yield return ActionResult.Fail($"没有“{mode}”这个模式，只有 {ModeSolo}（{ModeName(ModeSolo)}）、{ModeCommand}（{ModeName(ModeCommand)}）、{ModeHybrid}（{ModeName(ModeHybrid)}）");
                yield break;
            }
            Mode = target;
            ModeChanged?.Invoke(target);
            Game1.chatBox?.textBoxEnter($"好的，现在是“{ModeName(target)}”模式");
            yield return ActionResult.Success($"已经切换成“{ModeName(target)}”。{ModeAdvice()}");
        }

        public IEnumerable<object> Say(string text)
        {
            if (!Context.IsMultiplayer || Game1.chatBox == null)
            {
                yield return ActionResult.Fail("只有联机时才能说话");
                yield break;
            }
            // 开头的 / 会被聊天框当成游戏命令
            text = (text ?? "").Trim().TrimStart('/').Trim();
            if (text.Length == 0)
            {
                yield return ActionResult.Fail("没有要说的内容");
                yield break;
            }
            if (text.Length > 300)
                text = text.Substring(0, 300);
            Game1.chatBox.textBoxEnter(text);
            chatAwaitingReply = false;
            yield return ActionResult.Success("说了：" + text);
        }

        public IEnumerable<object> WaitChat(int seconds)
        {
            if (!Context.IsMultiplayer)
            {
                yield return ActionResult.Fail("只有联机时才有人和你聊天");
                yield break;
            }
            seconds = Math.Clamp(seconds, 5, 300);
            for (int i = 0; i < seconds * 60 && incomingChat.Count == 0; i++)
            {
                if ((Game1.eventUp && Game1.CurrentEvent != null) || Game1.activeClickableMenu != null)
                {
                    yield return ActionResult.Success("等待被打断：出现了剧情或界面。当前界面：" + observer.DescribeMenu());
                    yield break;
                }
                yield return null;
            }
            // 消息由工具结果统一附在后面
            yield return ActionResult.Success(incomingChat.Count > 0 ? "有人说话了" : $"等了 {seconds} 秒，没有人说话");
        }

        /// <summary>拿结婚戒指向别的玩家求婚。对方自己的窗口里会弹问题，由对方决定，这里等到有结果为止。</summary>
        public IEnumerable<object> Propose()
        {
            if (Blocked(out string why))
            {
                yield return ActionResult.Fail(why);
                yield break;
            }
            var target = Context.IsMultiplayer ? OtherPlayer() : null;
            if (target == null)
            {
                yield return ActionResult.Fail("现在没有别的玩家在线，没有人可以求婚");
                yield break;
            }
            if (Player.isMarriedOrRoommates() || Player.isEngaged())
            {
                yield return ActionResult.Fail("你已经订婚或结婚了");
                yield break;
            }
            if (target.isMarriedOrRoommates() || target.isEngaged())
            {
                yield return ActionResult.Fail($"{target.Name} 已经订婚或结婚了");
                yield break;
            }
            if (Player.Items.FirstOrDefault(i => i?.QualifiedItemId == "(O)801") == null)
            {
                yield return ActionResult.Fail("背包里没有结婚戒指。配方和材料看 get_guide 的“结婚”专题");
                yield break;
            }

            // 对方可能在走动，走到旁边为止
            var o = new Outcome();
            for (int attempt = 0; attempt < 20; attempt++)
            {
                target = OtherPlayer();
                if (target == null)
                {
                    yield return ActionResult.Fail("对方下线了");
                    yield break;
                }
                var where = target.currentLocation;
                if (where?.NameOrUniqueName != Loc.NameOrUniqueName)
                {
                    if (where == null || Game1.isFestival() || where.IsTemporary)
                    {
                        yield return ActionResult.Fail($"{target.Name} 现在在节日或者临时的地方，等对方回来再求婚");
                        yield break;
                    }
                    foreach (var s in TravelInner(where.NameOrUniqueName, o, exactName: true))
                        yield return s;
                    if (!o.Ok)
                    {
                        yield return ActionResult.Fail($"{target.Name} 在 {where.NameOrUniqueName}，走不过去：{o.Message}");
                        yield break;
                    }
                    yield return null;
                    continue;
                }
                if (Util.Manhattan(Player.TilePoint, target.TilePoint) <= 2)
                    break;
                var goals = Pathfinder.AdjacentStandTiles(Loc, target.TilePoint);
                var path = goals.Count > 0 ? Pathfinder.FindPath(Loc, Player.TilePoint, goals, 8000) : null;
                if (path == null || path.Count < 2)
                {
                    foreach (var s in WaitTicks(15))
                        yield return s;
                    continue;
                }
                foreach (var s in WalkPath(path.Take(8).ToList(), o))
                    yield return s;
                o.Succeed();
            }
            if (Blocked(out why))
            {
                yield return ActionResult.Fail(why);
                yield break;
            }
            if (target == null || target.currentLocation?.NameOrUniqueName != Loc.NameOrUniqueName || Util.Manhattan(Player.TilePoint, target.TilePoint) > 2)
            {
                yield return ActionResult.Fail($"没能走到 {target?.Name ?? "对方"} 旁边，请对方站着别动再试");
                yield break;
            }

            // 路上背包可能变了，这里重新找；游戏接受后会从手上扣掉戒指，所以要拿在手里
            int ringIdx = Player.Items.ToList().FindIndex(i => i?.QualifiedItemId == "(O)801");
            if (ringIdx < 0)
            {
                yield return ActionResult.Fail("背包里没有结婚戒指了");
                yield break;
            }
            Player.CurrentToolIndex = ringIdx;
            Player.Halt();
            Player.faceGeneralDirection(target.getStandingPosition(), 0, opposite: false, useTileCalculations: false);
            // 和游戏里按动作键后选“是”一样，只是省掉自己确认的那一问
            Player.team.SendProposal(target, ProposalType.Marriage, Player.CurrentItem.getOne());
            var pending = new PendingProposalDialog();
            Game1.activeClickableMenu = pending;

            bool timedOut = false;
            for (int i = 0; Game1.activeClickableMenu == pending; i++)
            {
                if (i == 120 * 60)
                {
                    timedOut = true;
                    pending.cancelProposal(Player);
                }
                if (i >= 150 * 60)
                {
                    yield return ActionResult.Fail($"{target.Name} 一直没回答，收回求婚也没有结果。界面还开着，等对方点一下才会关");
                    yield break;
                }
                yield return null;
            }
            foreach (var s in WaitTicks(15))
                yield return s;

            string menu = Game1.activeClickableMenu != null ? "\n当前界面：" + observer.DescribeMenu() : "";
            var friendship = Player.team.GetFriendship(Player.UniqueMultiplayerID, target.UniqueMultiplayerID);
            if (friendship?.IsEngaged() == true)
                yield return ActionResult.Success($"{target.Name} 答应了！{friendship.WeddingDate.TotalDays - Game1.Date.TotalDays} 天后早上举行婚礼。" + menu);
            else if (timedOut)
                yield return ActionResult.Fail($"{target.Name} 一直没回答，已经收回求婚，戒指还在你手上。" + menu);
            else
                yield return ActionResult.Fail($"{target.Name} 没有答应（拒绝了，或者正在忙，比如开着界面或看剧情），戒指还在你手上。" + menu);
        }

        public IEnumerable<object> Follow(int seconds)
        {
            if (Blocked(out string why))
            {
                yield return ActionResult.Fail(why);
                yield break;
            }
            if (!Context.IsMultiplayer || OtherPlayer() == null)
            {
                yield return ActionResult.Fail("现在没有别的玩家在线，没有人可以跟");
                yield break;
            }
            seconds = Math.Clamp(seconds, 10, 600);
            var o = new Outcome();
            int start = Game1.ticks;
            while (Game1.ticks - start < seconds * 60)
            {
                if (incomingChat.Count > 0)
                {
                    yield return ActionResult.Success("有人说话了，先停下来");
                    yield break;
                }
                if (Blocked(out why))
                {
                    yield return ActionResult.Success("先不跟了：" + why);
                    yield break;
                }
                var target = OtherPlayer();
                if (target == null)
                {
                    yield return ActionResult.Fail("对方下线了");
                    yield break;
                }
                var where = target.currentLocation;
                string name = where?.NameOrUniqueName;
                if (name != Loc.NameOrUniqueName)
                {
                    if (name == null || Game1.isFestival() || where.IsTemporary)
                    {
                        yield return ActionResult.Fail($"{target.Name} 现在在节日或者临时的地方，跟不过去。要去节日用 festival enter");
                        yield break;
                    }
                    foreach (var s in TravelInner(name, o, exactName: true))
                        yield return s;
                    if (!o.Ok)
                    {
                        yield return ActionResult.Fail($"{target.Name} 在 {name}，走不过去：{o.Message}");
                        yield break;
                    }
                    // 已经在目的地时 TravelInner 不会等一帧，不能直接回到循环开头空转
                    yield return null;
                    continue;
                }

                var me = Player.TilePoint;
                var there = target.TilePoint;
                if (Util.Manhattan(me, there) > 2)
                {
                    var goals = Pathfinder.AdjacentStandTiles(Loc, there);
                    var path = goals.Count > 0 ? Pathfinder.FindPath(Loc, me, goals, 8000) : null;
                    if (path != null && path.Count >= 2)
                    {
                        // 只走几步就重新看，对方也在走
                        foreach (var s in WalkPath(path.Take(5).ToList(), o))
                            yield return s;
                        o.Succeed();
                        continue;
                    }
                }
                foreach (var s in WaitTicks(15))
                    yield return s;
            }
            var last = OtherPlayer();
            yield return ActionResult.Success($"跟了 {seconds} 秒" + (last != null ? $"，{last.Name} 在 {last.currentLocation?.NameOrUniqueName} ({last.TilePoint.X},{last.TilePoint.Y})" : ""));
        }
    }
}
