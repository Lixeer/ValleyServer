using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Menus;
using xLocation = xTile.Dimensions.Location;

namespace BlindWhaleFarmer
{
    internal sealed partial class GameActions
    {
        // ───────────────────────── 参加节日 ─────────────────────────

        /// <summary>
        /// 节日里可以自由走动的时候（Event.playerControlSequence），允许 festival 工具走路、找人说话。
        /// 只有 festival 工具运行期间打开，其他工具在节日里仍然被 Blocked 拦下。
        /// </summary>
        private static bool festivalWalk;

        /// <summary>走 travel_to 路线时允许进入节日地图（只在 festival enter 时打开）。</summary>
        private static bool travelIntoFestival;

        /// <summary>今天已经在节日上打过招呼的人（换一天清空）。</summary>
        private static readonly HashSet<string> greetedToday = new();
        private static int greetedDay = -1;

        private static bool FestivalFreeRoam() =>
            Game1.CurrentEvent is { isFestival: true } ev && ev.playerControlSequence
            && Game1.activeClickableMenu == null && !Game1.isWarping && !Game1.fadeToBlack;

        /// <summary>把对话框一页页点完；遇到选择题用 pick 选（返回 -1 表示不选、停下）。最多 maxTicks 帧。</summary>
        private static IEnumerable<object> ClickThroughDialogue(Func<DialogueBox, int> pick, int maxTicks = 900)
        {
            for (int t = 0; t < maxTicks && Game1.activeClickableMenu is DialogueBox db; t++)
            {
                if (!db.transitioning && db.safetyTimer <= 0)
                {
                    if (db.isQuestion)
                    {
                        int choice = pick(db);
                        if (choice < 0)
                            yield break;
                        db.selectedResponse = choice;
                    }
                    db.receiveLeftClick(0, 0);
                }
                yield return null;
            }
        }

        /// <summary>选择题里“是/好”的那个选项；找不到就选第一个。</summary>
        private static int YesChoice(DialogueBox db)
        {
            int yes = Array.FindIndex(db.responses, r => r.responseKey is "Yes" or "yes" || r.responseText.Contains("是") || r.responseText.Contains("好"));
            return yes >= 0 ? yes : 0;
        }

        /// <summary>选择题里“不/否”的那个选项（比如花舞节邀请跳舞）；找不到就选最后一个。</summary>
        private static int NoChoice(DialogueBox db)
        {
            int no = Array.FindIndex(db.responses, r => r.responseKey is "No" or "no" || r.responseText.Contains("不"));
            return no >= 0 ? no : db.responses.Length - 1;
        }

        public IEnumerable<object> Festival(string action)
        {
            action = (action ?? "").Trim().ToLowerInvariant();
            festivalWalk = true;
            try
            {
                switch (action)
                {
                    case "enter":
                        foreach (var s in EnterFestival())
                            yield return s;
                        break;
                    case "greet":
                        foreach (var s in GreetAtFestival())
                            yield return s;
                        break;
                    case "start":
                        foreach (var s in StartFestivalEvent())
                            yield return s;
                        break;
                    case "leave":
                        foreach (var s in LeaveFestival())
                            yield return s;
                        break;
                    case "grange":
                        if (Game1.CurrentEvent is { isFestival: true } fair && fair.isSpecificFestival("fall16"))
                        {
                            var placed = new List<string>();
                            foreach (var s in GrangeDisplay(fair, placed))
                                yield return s;
                            yield return ActionResult.Success(string.Join("；", placed));
                        }
                        else
                            yield return ActionResult.Fail("只有星露谷展览会才有展示柜");
                        break;
                    default:
                        yield return ActionResult.Fail("action 只能是 enter（进入）、greet（和大家打招呼）、start（找刘易斯开始主活动）、leave（离开）");
                        break;
                }
            }
            finally
            {
                festivalWalk = false;
                travelIntoFestival = false;
            }
        }

        private IEnumerable<object> EnterFestival()
        {
            if (Game1.CurrentEvent is { isFestival: true })
            {
                yield return ActionResult.Success("已经在节日里了。" + FestivalPeople());
                yield break;
            }
            var fest = Knowledge.TodaysFestival();
            if (fest == null)
            {
                yield return ActionResult.Fail("今天没有需要入场的节日（鳟鱼大赛、鱿鱼节、夜市、沙漠节不用入场，直接去那里玩）");
                yield break;
            }
            if (Game1.timeOfDay < fest.Start)
            {
                yield return ActionResult.Fail($"{fest.Name} {Util.TimeString(fest.Start)} 才开始，先去做别的，到点再来");
                yield break;
            }
            if (Game1.timeOfDay > fest.End)
            {
                yield return ActionResult.Fail($"{fest.Name} 已经过了入场时间（{Util.TimeString(fest.End)}）");
                yield break;
            }
            var o = new Outcome();
            travelIntoFestival = true;
            foreach (var s in TravelInner(fest.Location, o))
                yield return s;
            travelIntoFestival = false;
            // 走进节日地图后游戏会淡出、加载节日，等它就绪
            for (int t = 0; t < 900 && !(Game1.CurrentEvent is { isFestival: true } ev && ev.playerControlSequence); t++)
            {
                if (Game1.activeClickableMenu is DialogueBox)
                    foreach (var s in ClickThroughDialogue(YesChoice, 120))
                        yield return s;
                yield return null;
            }
            if (Game1.CurrentEvent is not { isFestival: true })
            {
                yield return ActionResult.Fail($"没能进入{fest.Name}：{(o.Ok ? "节日没有开始" : o.Message)}");
                yield break;
            }
            yield return ActionResult.Success($"进入了{fest.Name}。节日里时间不走。{FestivalPeople()}"
                + "接下来：festival greet 和大家打招呼（加好感），再 festival start 找刘易斯开始主活动；不想参加主活动就 festival leave");
        }

        /// <summary>节日上有哪些村民（Event.actors）。</summary>
        private static string FestivalPeople()
        {
            var ev = Game1.CurrentEvent;
            if (ev == null)
                return "";
            var names = ev.actors.Where(a => a is not Child && a.IsVillager).Select(a => a.displayName).Distinct().ToList();
            return names.Count > 0 ? $"在场的人：{string.Join("、", names)}。" : "";
        }

        private IEnumerable<object> GreetAtFestival()
        {
            var ev = Game1.CurrentEvent;
            if (ev is not { isFestival: true })
            {
                yield return ActionResult.Fail("现在不在节日里，先 festival enter");
                yield break;
            }
            if (greetedDay != Game1.Date.TotalDays)
            {
                greetedToday.Clear();
                greetedDay = Game1.Date.TotalDays;
            }
            var greeted = new List<string>();
            var skipped = new List<string>();
            var o = new Outcome();
            for (int round = 0; round < 40; round++)
            {
                if (!FestivalFreeRoam())
                {
                    if (Game1.activeClickableMenu is DialogueBox)
                    {
                        foreach (var s in ClickThroughDialogue(NoChoice))
                            yield return s;
                        continue;
                    }
                    break;
                }
                var me = Player.TilePoint;
                var next = ev.actors
                    .Where(a => a is not Child && a.IsVillager && a.Name != "Lewis" && !greetedToday.Contains(a.Name))
                    .OrderBy(a => Util.Manhattan(a.TilePoint, me))
                    .FirstOrDefault();
                if (next == null)
                    break;
                greetedToday.Add(next.Name);
                var tile = next.TilePoint;
                foreach (var s in GoAdjacent(tile, o))
                    yield return s;
                if (!o.Ok)
                {
                    skipped.Add(next.displayName);
                    o.Succeed();
                    continue;
                }
                Face(tile);
                Loc.checkAction(new xLocation(tile.X, tile.Y), Game1.viewport, Player);
                // 普通的节日对话；花舞节别人邀请跳舞之类的选择题先婉拒（要跳舞让 AI 自己用 interact + dialogue 决定）
                foreach (var s in ClickThroughDialogue(NoChoice))
                    yield return s;
                greeted.Add(next.displayName);
            }
            string msg = greeted.Count > 0 ? $"和 {greeted.Count} 个人打了招呼：{string.Join("、", greeted)}" : "没有新的人可以打招呼";
            if (skipped.Count > 0)
                msg += $"；走不到旁边的：{string.Join("、", skipped)}";
            yield return ActionResult.Success(msg + "。接下来 festival start 找刘易斯开始主活动，或 festival leave 离开");
        }

        /// <summary>节日主活动已经开始了（可能是别人开始的）。展览会没有这个阶段。</summary>
        internal static bool FestivalMainRunning(Event ev) =>
            ev is { isFestival: true } && (ev.eventSwitched || ev.playerControlSequenceID is "eggHunt" or "iceFishing");

        private static string festivalReadySaid;

        private IEnumerable<object> StartFestivalEvent()
        {
            var ev = Game1.CurrentEvent;
            if (ev is not { isFestival: true })
            {
                yield return ActionResult.Fail("现在不在节日里，先 festival enter");
                yield break;
            }
            var notes = new List<string>();
            bool farmhand = !Game1.IsMasterGame;
            bool mainRunning = FestivalMainRunning(ev) || ev.doingSecretSanta;

            // 展览会的展示柜大家共用，评分也只有主机能开始
            if (farmhand && ev.isSpecificFestival("fall16"))
            {
                if (ev.grangeJudged)
                {
                    var host = ev.getActorByName("Lewis");
                    var walk = new Outcome();
                    if (host != null)
                    {
                        foreach (var s in GoAdjacent(host.TilePoint, walk))
                            yield return s;
                        if (walk.Ok)
                        {
                            Face(host.TilePoint);
                            Loc.checkAction(new xLocation(host.TilePoint.X, host.TilePoint.Y), Game1.viewport, Player);
                            foreach (var s in ClickThroughDialogue(YesChoice))
                                yield return s;
                        }
                    }
                    yield return walk.Ok && host != null
                        ? ActionResult.Success($"找刘易斯问过评分了，现在有 {Player.festivalScore} 个星星币。逛完 festival leave")
                        : ActionResult.Fail("走不到刘易斯旁边：" + walk.Message);
                    yield break;
                }
                yield return ActionResult.Success(Game1.player.team.grangeDisplay.Any(i => i != null)
                    ? "展示柜里已经摆了东西。评分只有主机能开始（主机找刘易斯），评完再 festival start 找刘易斯领奖"
                    : "展示柜是大家共用的，先用 say 问主机要不要你来摆，同意了再 festival grange。评分只有主机能开始，评完再 festival start 找刘易斯领奖");
                yield break;
            }

            // 没有要刘易斯宣布的主活动的节日：万灵节去迷宫拿宝箱；展览会的展示柜和小游戏没做，打完招呼就走
            if (ev.isSpecificFestival("fall27"))
            {
                foreach (var s in OpenFestivalTreasure(notes))
                    yield return s;
                yield return ActionResult.Success(string.Join("；", notes) + "。万灵节没有别的主活动，festival leave 回家");
                yield break;
            }
            if (ev.isSpecificFestival("fall16"))
            {
                foreach (var s in GrangeDisplay(ev, notes))
                    yield return s;
                yield return ActionResult.Success(string.Join("；", notes)
                    + "。星星币可以在巴士站方向入口的星星币商店换奖品（星星之果实 2000 币）；展览会的其他小游戏没做，逛完就 festival leave 回家");
                yield break;
            }
            // 主活动开始前要做的：夏威夷宴会往汤里放东西、花舞节找舞伴、冬日星盛宴送神秘礼物
            if (!mainRunning && ev.isSpecificFestival("summer11"))
                foreach (var s in AddLuauSoup(ev, notes))
                    yield return s;
            if (!mainRunning && ev.isSpecificFestival("spring24"))
                foreach (var s in InviteDancePartner(ev, notes))
                    yield return s;
            if (!mainRunning && ev.isSpecificFestival("winter25"))
                foreach (var s in GiveSecretSantaGift(ev, notes))
                    yield return s;

            // 联机时只有主机问得到“准备好了吗”，主机答应后所有人立刻开始，不会等农场工人。
            // 所以做完准备就说一声，等主活动开始再接着往下走
            if (farmhand && !FestivalMainRunning(ev) && !ev.isSpecificFestival("winter25"))
            {
                string readyKey = ev.id + Game1.Date.TotalDays;
                if (festivalReadySaid != readyKey)
                {
                    festivalReadySaid = readyKey;
                    Game1.chatBox?.textBoxEnter("我准备好了，可以找刘易斯开始主活动了");
                }
                string prepared = notes.Count > 0 ? string.Join("；", notes) + "。" : "";
                for (int t = 0; t < 300 * 60 && !FestivalMainRunning(ev) && Game1.CurrentEvent == ev; t++)
                {
                    if (incomingChat.Count > 0)
                    {
                        yield return ActionResult.Success(prepared + "有人说话了，主活动还没开始。回完话再 festival start 接着等");
                        yield break;
                    }
                    if (Game1.activeClickableMenu != null)
                    {
                        yield return ActionResult.Success(prepared + "出现了界面，先处理（处理完再 festival start 接着等）：\n" + observer.DescribeMenu());
                        yield break;
                    }
                    yield return null;
                }
                if (!FestivalMainRunning(ev) && Game1.CurrentEvent == ev)
                {
                    yield return ActionResult.Fail(prepared + "等了 5 分钟，主机还没有开始主活动。用 say 问一声，想接着等就再 festival start，不等了就 festival leave");
                    yield break;
                }
            }

            if (!FestivalMainRunning(ev) && !farmhand && !ev.isSpecificFestival("winter25"))
            {
                var lewis = ev.getActorByName("Lewis");
                if (lewis == null)
                {
                    yield return ActionResult.Fail("节日上找不到刘易斯，这个节日可能没有要他宣布开始的主活动（展览会、万灵节逛完直接 festival leave）");
                    yield break;
                }
                var o = new Outcome();
                var tile = lewis.TilePoint;
                foreach (var s in GoAdjacent(tile, o))
                    yield return s;
                if (!o.Ok)
                {
                    yield return ActionResult.Fail("走不到刘易斯旁边：" + o.Message);
                    yield break;
                }
                Face(tile);
                Loc.checkAction(new xLocation(tile.X, tile.Y), Game1.viewport, Player);
                // “大家准备好了吗？”选“是”，主活动开始
                bool asked = false;
                for (int t = 0; t < 900 && Game1.activeClickableMenu is DialogueBox; t++)
                {
                    if (Game1.activeClickableMenu is DialogueBox { isQuestion: true })
                        asked = true;
                    foreach (var s in ClickThroughDialogue(YesChoice, 60))
                        yield return s;
                    yield return null;
                }
                if (!asked && ev.playerControlSequence && Game1.CurrentEvent == ev)
                {
                    yield return ActionResult.Fail("刘易斯没有问要不要开始（可能还没到时间，或者这个节日没有主活动）。"
                        + "展览会、万灵节没有要他宣布的主活动，逛完 festival leave；其他节日可以过一会儿再 festival start");
                    yield break;
                }

                // 联机时刘易斯那边答应后要等所有人都准备好
                if (Game1.activeClickableMenu is ReadyCheckDialog ready)
                {
                    foreach (var s in WaitForPlayers(300))
                        yield return s;
                    if (Game1.activeClickableMenu == ready)
                    {
                        yield return ActionResult.Fail("等了 5 分钟，其他玩家还没准备好开始主活动。" + ReadyCheckHint(ready));
                        yield break;
                    }
                }
            }

            // 冬日星盛宴没有要主机开始的主活动：送了礼物才有后面的交换剧情，没送成就到这里为止
            if (ev.isSpecificFestival("winter25") && !ev.doingSecretSanta)
            {
                for (int t = 0; t < 300 && !ev.doingSecretSanta && Game1.CurrentEvent == ev; t++)
                    yield return null;
                if (!ev.doingSecretSanta && Game1.CurrentEvent == ev)
                {
                    yield return ActionResult.Success((notes.Count > 0 ? string.Join("；", notes) + "。" : "")
                        + "冬日星盛宴没有要主机开始的主活动，送了礼物才有交换剧情。可以 festival greet 或者 festival leave");
                    yield break;
                }
            }

            // 主活动：大多是过场动画，自己点掉对话；找彩蛋时去捡彩蛋；冰钓比赛还不会钓，站着等它结束
            bool eggsDone = false, iceDone = false;
            for (int t = 0; t < 60 * 60 * 4 && Game1.eventUp && Game1.CurrentEvent != null; t++)
            {
                // 冬日星盛宴交换完礼物可以自由走动，等大家一起离开，这里不用再等了
                if (ev.playerControlSequenceID == "christmas2" && ev.playerControlSequence && Game1.activeClickableMenu == null)
                {
                    yield return ActionResult.Success((notes.Count > 0 ? string.Join("；", notes) + "。" : "")
                        + "交换完礼物了，可以和大家聊聊天，想走就 festival leave（联机时要所有玩家都同意）");
                    yield break;
                }
                if (!eggsDone && ev.playerControlSequenceID == "eggHunt" && ev.playerControlSequence && ev.festivalTimer > 0 && Game1.activeClickableMenu == null)
                {
                    eggsDone = true;
                    foreach (var s in EggHunt(ev, notes))
                        yield return s;
                }
                if (!iceDone && ev.playerControlSequenceID == "iceFishing" && ev.playerControlSequence && ev.festivalTimer > 0 && Game1.activeClickableMenu == null)
                {
                    iceDone = true;
                    foreach (var s in IceFishing(ev, notes))
                        yield return s;
                }
                if (Game1.activeClickableMenu is DialogueBox)
                    foreach (var s in ClickThroughDialogue(YesChoice, 120))
                        yield return s;
                else if (Game1.activeClickableMenu is ItemGrabMenu prize && ev.eventCommands[ev.CurrentCommand] == "null")
                {
                    // 发奖时背包满了会弹物品界面，剧情停在 null 命令上，要靠关界面时往下走一步（游戏在 ItemGrabMenu 按键关闭时做）
                    int waitingAt = ev.CurrentCommand;
                    GrabAll(prize);
                    if (ev.CurrentCommand == waitingAt)
                        ev.CurrentCommand++;
                }
                else if (Game1.activeClickableMenu is ItemGrabMenu or ShopMenu or LetterViewerMenu)
                    Game1.exitActiveMenu();
                // 发奖界面被别的办法关掉了也接着走。只认发奖这两条：其他 null 命令（比如宣布赢家）是等对话框关闭，不能抢
                else if (Game1.activeClickableMenu == null && ev.CurrentCommand > 0 && ev.CurrentCommand < ev.eventCommands.Length && ev.eventCommands[ev.CurrentCommand] == "null"
                    && ev.eventCommands[ev.CurrentCommand - 1] is "awardFestivalPrize" or "cutscene addSecretSantaItem")
                    ev.CurrentCommand++;
                yield return null;
            }
            for (int t = 0; t < 600 && (Game1.isWarping || Game1.fadeToBlack || Game1.globalFade); t++)
                yield return null;
            if (Game1.eventUp)
            {
                yield return ActionResult.Fail((notes.Count > 0 ? string.Join("；", notes) + "。" : "") + "主活动还没结束（等太久了）。再 festival start 接着参加，或者用 dialogue 过对白");
                yield break;
            }
            yield return ActionResult.Success((notes.Count > 0 ? string.Join("；", notes) + "。" : "")
                + $"节日主活动结束了，现在在 {Loc.NameOrUniqueName}，{Util.TimeString(Game1.timeOfDay)}。"
                + "节日结束后游戏会把你送回农场，今天剩下的时间不多，可以收拾一下睡觉");
        }
    }
}
