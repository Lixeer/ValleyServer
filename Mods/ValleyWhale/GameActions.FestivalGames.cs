using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.Tools;
using SObject = StardewValley.Object;
using xLocation = xTile.Dimensions.Location;

namespace BlindWhaleFarmer
{
    internal sealed partial class GameActions
    {
        // ───────────────────────── 节日小游戏 ─────────────────────────
        // 都按游戏源码（Event.cs）里的规则来：能调用游戏自己的方法就调用，不另造规则。

        /// <summary>
        /// 夏威夷宴会的汤：照抄 Event.governorTaste 的档次。州长按汤里**最差**的一样打分：
        /// 4 档（金星且售价≥160，或银星、售价≥300、能吃）全镇好感 +120；3 档（能量≥20 或售价≥100 或银星以上售价≥70）+60；
        /// 2 档不加不减；1 档 -50；0 档（有毒的）-100。不放东西没有影响。
        /// </summary>
        private static int SoupTier(SObject o)
        {
            if (o.Quality >= 2 && o.Price >= 160 || o.Quality == 1 && o.Price >= 300 && o.Edibility > 10)
                return 4;
            if (o.Edibility >= 20 || o.Price >= 100 || o.Price >= 70 && o.Quality >= 1)
                return 3;
            if (o.Price > 20 && o.Edibility >= 10 || o.Price >= 40 && o.Edibility >= 5)
                return 2;
            return o.Edibility >= 0 ? 1 : 0;
        }

        /// <summary>往汤里放一样东西：只放 3 档以上（加好感）的，同档里挑最便宜的；没有就不放（不放不扣好感）。</summary>
        private IEnumerable<object> AddLuauSoup(Event ev, List<string> notes)
        {
            var best = Player.Items
                .OfType<SObject>()
                .Where(o => Utility.highlightLuauSoupItems(o) && o.Edibility != -300)
                .Select(o => (o, tier: SoupTier(o)))
                .Where(x => x.tier >= 3)
                .OrderByDescending(x => x.tier).ThenBy(x => x.o.Price)
                .FirstOrDefault();
            if (best.o == null)
            {
                notes.Add("背包里没有能让州长满意的汤料（要金星品质、售价 160 以上的作物/鱼最好），没有放，不放不会扣好感");
                yield break;
            }
            // 和在汤锅界面里点背包物品一样：先从背包拿走，再交给游戏的 addItemToLuauSoup（它会把多的放回背包）
            var item = best.o;
            Player.removeItemFromInventory(item);
            ev.addItemToLuauSoup(item, Player);
            foreach (var s in ClickThroughDialogue(YesChoice, 300))
                yield return s;
            notes.Add($"往汤里放了 {item.DisplayName}（{(best.tier == 4 ? "最好的一档，全镇好感 +120" : "好的一档，全镇好感 +60")}）");
        }

        /// <summary>花舞节：邀请好感最高（≥4 心）的单身村民跳舞。和村民说话、回答“是”，最多试两次。</summary>
        private IEnumerable<object> InviteDancePartner(Event ev, List<string> notes)
        {
            if (Player.dancePartner.Value != null)
                yield break;
            var candidates = ev.actors
                .Where(a => a.datable.Value && a.IsVillager && Player.getFriendshipHeartLevelForNPC(a.Name) >= 4)
                .OrderByDescending(a => Player.getFriendshipHeartLevelForNPC(a.Name))
                .ToList();
            if (candidates.Count == 0)
            {
                notes.Add("没有好感 4 心以上的单身村民，这次没有舞伴（看大家跳就好）");
                yield break;
            }
            var o = new Outcome();
            foreach (var npc in candidates.Take(3))
            {
                for (int attempt = 0; attempt < 2 && Player.dancePartner.Value == null; attempt++)
                {
                    foreach (var s in GoAdjacent(npc.TilePoint, o))
                        yield return s;
                    if (!o.Ok)
                    {
                        o.Succeed();
                        break;
                    }
                    Face(npc.TilePoint);
                    Loc.checkAction(new xLocation(npc.TilePoint.X, npc.TilePoint.Y), Game1.viewport, Player);
                    foreach (var s in ClickThroughDialogue(YesChoice))
                        yield return s;
                }
                if (Player.dancePartner.Value != null)
                {
                    notes.Add($"邀请 {npc.displayName} 跳舞成功（跳舞后好感 +250）");
                    yield break;
                }
            }
            notes.Add($"邀请跳舞没成功（试了 {string.Join("、", candidates.Take(3).Select(c => c.displayName))}）");
        }

        /// <summary>
        /// 冬日星盛宴：和秘密礼物对象（Event.secretSantaRecipient）说话、回答“是”，在弹出的选礼物界面里挑他最喜欢的东西
        /// （按 NPC.getGiftTasteForThisItem：最爱 > 喜欢 > 一般，不送讨厌的），交给界面自己的回调（chooseSecretSantaGift）。
        /// </summary>
        private IEnumerable<object> GiveSecretSantaGift(Event ev, List<string> notes)
        {
            var who = ev.secretSantaRecipient;
            if (who == null)
                yield break;
            var target = ev.actors.FirstOrDefault(a => a.Name == who.Name) ?? who;
            var o = new Outcome();
            foreach (var s in GoAdjacent(target.TilePoint, o))
                yield return s;
            if (!o.Ok)
            {
                notes.Add($"走不到 {who.displayName} 旁边，没送成礼物");
                yield break;
            }
            Face(target.TilePoint);
            Loc.checkAction(new xLocation(target.TilePoint.X, target.TilePoint.Y), Game1.viewport, Player);
            foreach (var s in ClickThroughDialogue(YesChoice))
                yield return s;
            for (int t = 0; t < 120 && Game1.activeClickableMenu is not ItemGrabMenu; t++)
                yield return null;
            if (Game1.activeClickableMenu is not ItemGrabMenu menu)
            {
                notes.Add($"和 {who.displayName} 说话后没有弹出送礼界面（可能已经送过了）");
                yield break;
            }
            int Rank(int taste) => taste switch { NPC.gift_taste_love => 0, NPC.gift_taste_like => 1, NPC.gift_taste_neutral => 2, _ => 9 };
            var gift = Player.Items
                .Where(i => i != null && Utility.highlightSantaObjects(i))
                .Select(i => (i, rank: Rank(who.getGiftTasteForThisItem(i))))
                .Where(x => x.rank < 9)
                .OrderBy(x => x.rank).ThenBy(x => x.i.salePrice())
                .FirstOrDefault();
            if (gift.i == null)
            {
                notes.Add($"背包里没有 {who.displayName} 不讨厌的礼物，没送（送礼界面关不掉的话用 menu_click 选一样）");
                yield break;
            }
            Player.removeItemFromInventory(gift.i);
            menu.behaviorFunction(gift.i, Player);
            foreach (var s in ClickThroughDialogue(YesChoice))
                yield return s;
            notes.Add($"送给 {who.displayName} 的神秘礼物：{gift.i.DisplayName}（{new[] { "最爱", "喜欢", "一般" }[gift.rank]}）");
        }

        /// <summary>
        /// 复活节找彩蛋：彩蛋是节日地图上的道具（Event.festivalProps），对着彩蛋那一格交互就捡到（festivalScore +1）。
        /// 限时 52 秒，捡到 9 个以上才赢。从最近的捡起。
        /// </summary>
        private IEnumerable<object> EggHunt(Event ev, List<string> notes)
        {
            var o = new Outcome();
            int start = Player.festivalScore;
            for (int guard = 0; guard < 60 && ev.festivalTimer > 0 && ev.playerControlSequence; guard++)
            {
                var me = Player.TilePoint;
                var egg = ev.festivalProps
                    .Select(p => reflection.GetField<Rectangle>(p, "boundingRect").GetValue())
                    .Select(r => new Point(r.X / 64, r.Y / 64))
                    .OrderBy(p => Util.Manhattan(p, me))
                    .Cast<Point?>()
                    .FirstOrDefault();
                if (egg is not Point tile)
                    break;
                foreach (var s in GoAdjacent(tile, o))
                    yield return s;
                if (!o.Ok)
                {
                    o.Succeed();
                    // 走不过去的彩蛋先从列表里拿掉，免得卡在它上面
                    ev.festivalProps.RemoveAll(p => p.isColliding(new Rectangle(tile.X * 64, tile.Y * 64, 64, 64)));
                    continue;
                }
                Face(tile);
                Loc.checkAction(new xLocation(tile.X, tile.Y), Game1.viewport, Player);
                yield return null;
            }
            int got = Player.festivalScore - start;
            notes.Add($"找彩蛋捡到 {got} 个{(got >= 9 ? "，应该能赢" : "（要 9 个才赢）")}");
        }

        /// <summary>
        /// 冰雪节冰钓比赛：活动发一根带精装旋式鱼饵的竹竿（Farmer.TemporaryItem），限时 120 秒，钓到 5 条以上获胜。
        /// 钓上来的鱼由 Event.caughtFish 计分，所以直接用平时的抛竿 + 自动玩小游戏（FishOnce + FishingAssist）就行。
        /// 冰洞在面前：先看面朝的方向，再看其他方向，找最近的能钓鱼的格子。
        /// </summary>
        private IEnumerable<object> IceFishing(Event ev, List<string> notes)
        {
            if (Player.CurrentTool is not FishingRod)
            {
                notes.Add("冰钓比赛开始了，但手上没有活动发的鱼竿，没法钓");
                yield break;
            }
            FishingSpot spot = null;
            var me = Player.TilePoint;
            foreach (int dir in new[] { Player.FacingDirection, 0, 1, 2, 3 }.Distinct())
            {
                var d = DirVec[dir];
                for (int k = 1; k <= 4 && spot == null; k++)
                    if (Fishable(Loc, me.X + d.X * k, me.Y + d.Y * k))
                        spot = new FishingSpot(me, dir, k);
                if (spot != null)
                    break;
            }
            if (spot == null)
            {
                notes.Add("冰钓比赛：身边没找到能抛竿的冰洞");
                yield break;
            }
            int start = Player.festivalScore, misses = 0;
            FishingAssist.Active = true;
            try
            {
                for (int cast = 0; cast < 40 && ev.festivalTimer > 4000 && ev.playerControlSequenceID == "iceFishing"; cast++)
                {
                    var o = new Outcome();
                    foreach (var s in FishOnce(-1, spot, o))
                        yield return s;
                    if (!o.Ok)
                    {
                        misses++;
                        if (o.Message.StartsWith("!") && misses > 3)
                            break;
                    }
                    // 钓上来的展示动画结束后再抛下一竿
                    for (int t = 0; t < 120 && (Player.UsingTool || Game1.activeClickableMenu != null); t++)
                    {
                        if (Game1.activeClickableMenu is DialogueBox)
                            foreach (var s in ClickThroughDialogue(YesChoice, 60))
                                yield return s;
                        yield return null;
                    }
                }
            }
            finally
            {
                FishingAssist.Active = false;
            }
            int got = Player.festivalScore - start;
            notes.Add($"冰钓比赛钓到 {got} 条{(got >= 5 ? "，应该能赢" : "（5 条以上才赢）")}");
        }

        /// <summary>展示柜计分时的种类（照抄 Event.judgeGrange 的分组），不计分的返回 null。</summary>
        private static int? GrangeGroup(SObject o) => o.Category switch
        {
            -75 => -75,                       // 蔬菜
            -79 => -79,                       // 水果
            -18 or -14 or -6 or -5 => -5,     // 动物产品
            -12 or -2 => -12,                 // 矿物、宝石
            -4 => -4,                         // 鱼
            -81 or -80 or -27 => -81,         // 采集品、花、树液类
            -7 => -7,                         // 料理
            -26 => -26,                       // 工匠品
            _ => null,
        };

        /// <summary>单样东西在展示柜里的分（Event.judgeGrange：品质+1，售价 ≥20/≥90/≥200 各 +1，便宜但高价的低品质再 +1）。</summary>
        private static int GrangeItemScore(SObject o)
        {
            int price = o.sellToStorePrice(), s = o.Quality + 1;
            if (price >= 20) s++;
            if (price >= 90) s++;
            if (price >= 200) s++;
            if (price >= 300 && o.Quality < 2) s++;
            if (price >= 400 && o.Quality < 1) s++;
            return s;
        }

        /// <summary>
        /// 星露谷展览会展示柜：从背包挑 9 样摆上去，先凑种类（每个种类 +5，最多 6 类 +30），再挑单样分高的；
        /// 摆好后找刘易斯评比，等他评完再去领奖（≥90 一等奖 1000 星星币、≥75 500、≥60 250），最后把东西拿回背包。
        /// 每样只摆 1 个，多的留在背包；摆东西用游戏自己的 addItemToGrangeDisplay。
        /// </summary>
        private IEnumerable<object> GrangeDisplay(Event ev, List<string> notes)
        {
            if (Game1.player.team.grangeDisplay.Any(i => i != null))
            {
                notes.Add("展示柜里已经摆了东西");
            }
            else
            {
                var pool = Player.Items.OfType<SObject>()
                    .Where(o => !o.bigCraftable.Value && o.canBeGivenAsGift() && GrangeGroup(o) != null)
                    .ToList();
                var chosen = new List<SObject>();
                // 每个种类先挑分最高的一样
                foreach (var g in pool.GroupBy(o => GrangeGroup(o)).Select(g => g.OrderByDescending(GrangeItemScore).First()).OrderByDescending(GrangeItemScore))
                    if (chosen.Count < 9)
                        chosen.Add(g);
                // 剩下的格子用分最高的补满
                foreach (var o in pool.Except(chosen).OrderByDescending(GrangeItemScore))
                    if (chosen.Count < 9)
                        chosen.Add(o);
                if (chosen.Count == 0)
                {
                    notes.Add("背包里没有能摆展示柜的东西（作物、水果、动物产品、宝石、鱼、采集品、料理、工匠品都行）");
                    yield break;
                }
                for (int i = 0; i < chosen.Count; i++)
                {
                    var src = chosen[i];
                    SObject one;
                    if (src.Stack > 1)
                    {
                        one = (SObject)src.getOne();
                        src.Stack--;
                    }
                    else
                    {
                        one = src;
                        Player.removeItemFromInventory(src);
                    }
                    ev.addItemToGrangeDisplay(one, i, false);
                }
                int kinds = chosen.Select(o => GrangeGroup(o)).Distinct().Count();
                notes.Add($"展示柜摆了 {chosen.Count} 样（{kinds} 个种类）：{string.Join("、", chosen.Select(o => o.DisplayName))}");
            }

            // 找刘易斯评比：问“准备好了吗”选是
            var lewis = ev.getActorByName("Lewis");
            if (lewis == null)
                yield break;
            var go = new Outcome();
            foreach (var s in GoAdjacent(lewis.TilePoint, go))
                yield return s;
            if (!go.Ok)
            {
                notes.Add("走不到刘易斯旁边，没开始评比");
                yield break;
            }
            Face(lewis.TilePoint);
            Loc.checkAction(new xLocation(lewis.TilePoint.X, lewis.TilePoint.Y), Game1.viewport, Player);
            foreach (var s in ClickThroughDialogue(YesChoice))
                yield return s;
            // 刘易斯绕展示柜走一圈评分（节日里时间不走，但动画照常播）
            for (int t = 0; t < 60 * 90 && !ev.grangeJudged; t++)
            {
                if (Game1.activeClickableMenu is DialogueBox)
                    foreach (var s in ClickThroughDialogue(YesChoice, 60))
                        yield return s;
                yield return null;
            }
            if (!ev.grangeJudged)
            {
                notes.Add("刘易斯还没评完（可能没开始评比），过一会儿再和他说话领奖");
                yield break;
            }
            // 再和他说话领奖（星星币加在 festivalScore 上）
            int before = Player.festivalScore;
            lewis = ev.getActorByName("Lewis");
            foreach (var s in GoAdjacent(lewis.TilePoint, go))
                yield return s;
            Face(lewis.TilePoint);
            Loc.checkAction(new xLocation(lewis.TilePoint.X, lewis.TilePoint.Y), Game1.viewport, Player);
            foreach (var s in ClickThroughDialogue(YesChoice))
                yield return s;
            int tokens = Player.festivalScore - before;
            notes.Add($"展示柜得分 {ev.grangeScore}，{(tokens > 0 ? $"拿到 {tokens} 星星币" : "没拿到奖")}（≥90 一等奖）");

            // 把展示的东西拿回背包（不拿会被放进镇长家的失物招领箱）
            var back = new List<string>();
            for (int i = 0; i < Game1.player.team.grangeDisplay.Count; i++)
            {
                var item = Game1.player.team.grangeDisplay[i];
                if (item == null)
                    continue;
                if (Player.addItemToInventoryBool(item))
                {
                    Game1.player.team.grangeDisplay[i] = null;
                    back.Add(item.DisplayName);
                }
            }
            if (Game1.player.team.grangeDisplay.Any(i => i != null))
                notes.Add("背包满了，展示柜里还有东西没拿回来（节日后在镇长家的失物招领箱里）");
        }

        /// <summary>
        /// 万灵节迷宫的宝箱（游戏在节日地图里放的：单数年 (33,13) 金南瓜，双数年 (63,16) 奖券）。
        /// 走过去打开：第一下掀盖子，再点一下拿东西，和矿洞宝箱一样。
        /// </summary>
        private IEnumerable<object> OpenFestivalTreasure(List<string> notes)
        {
            var chest = Loc.objects.Pairs
                .Where(p => p.Value is Chest c && !c.playerChest.Value && c.GetItemsForPlayer().Count > 0)
                .Select(p => new Point((int)p.Key.X, (int)p.Key.Y))
                .Cast<Point?>()
                .FirstOrDefault();
            if (chest is not Point tile)
            {
                notes.Add("迷宫里没找到宝箱（可能已经拿过了）");
                yield break;
            }
            var o = new Outcome();
            foreach (var s in GoAdjacent(tile, o))
                yield return s;
            if (!o.Ok)
            {
                notes.Add($"迷宫宝箱在 ({tile.X},{tile.Y})，但找不到路走过去（迷宫里有一段要穿过树篱的暗道）：{o.Message}");
                yield break;
            }
            Face(tile);
            var got = new List<string>();
            for (int click = 0; click < 6; click++)
            {
                if (!(Loc.objects.TryGetValue(new Vector2(tile.X, tile.Y), out var obj) && obj is Chest c) || c.GetItemsForPlayer().Count == 0)
                    break;
                var before = c.GetItemsForPlayer().Where(i => i != null).Select(i => i.DisplayName).ToList();
                Loc.checkAction(new xLocation(tile.X, tile.Y), Game1.viewport, Player);
                foreach (var s in WaitTicks(60))
                    yield return s;
                foreach (var name in c.GetItemsForPlayer().Where(i => i != null).Select(i => i.DisplayName))
                    before.Remove(name);
                got.AddRange(before);
                foreach (var s in ClickThroughDialogue(YesChoice, 120))
                    yield return s;
            }
            notes.Add(got.Count > 0 ? $"迷宫宝箱里拿到：{string.Join("、", got)}" : "打开了迷宫宝箱，但没拿到东西");
        }
    }
}
