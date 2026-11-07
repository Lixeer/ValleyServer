using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace BlindWhaleFarmer
{
    /// <summary>
    /// 钓鱼小游戏的自动操作。BobberBar 每帧读取鼠标/按键决定绿条往上还是往下，
    /// 这里在游戏更新之后（UpdateTicked）把“没按键”的加速度改成“按住键”的加速度，效果等同于按住左键。
    /// </summary>
    internal static class FishingAssist
    {
        /// <summary>只有 fish 动作执行期间才接管小游戏，玩家自己钓鱼时不干预。</summary>
        public static bool Active;

        public static void Tick(bool actionRunning)
        {
            // 动作被取消/超时时协程的 finally 不一定会执行，这里兜底
            if (!actionRunning)
                Active = false;
            if (!Active || Game1.activeClickableMenu is not BobberBar bar || bar.fadeIn || bar.fadeOut)
                return;

            // 绿条覆盖 [barPos-32, barPos-32+h]，鱼占 [pos-16, pos+12]，让两者中心对齐
            float h = bar.bobberBarHeight;
            float target = bar.bobberPosition + 30f - h / 2f;
            // 预测几帧后的位置，提前松键/按键，避免来回过冲
            // 预测帧数用离线复刻的小游戏物理调过：12 帧时普通鱼（难度≤70）几乎全中
            float predicted = bar.bobberBarPos + bar.bobberBarSpeed * 12f;
            bool press = predicted > target; // y 向下增大，按住=往上
            if (!press)
                return;

            float factor = bar.bobberInBar ? (bar.bobbers.Contains("(O)691") ? 0.3f : 0.6f) : 1f;
            float delta = -0.5f * factor; // 按住(-0.25) 与没按(+0.25) 的加速度差
            bar.bobberBarSpeed += delta;
            bar.bobberBarPos += delta;
            float max = 568 - h;
            if (bar.bobberBarPos < 0f)
            {
                bar.bobberBarPos = 0f;
                bar.bobberBarSpeed = 0f;
            }
            else if (bar.bobberBarPos > max)
            {
                bar.bobberBarPos = max;
                bar.bobberBarSpeed = 0f;
            }
            bar.buttonPressed = true;
        }
    }

    internal sealed partial class GameActions
    {
        /// <param name="Distance">从站立格到落点的格数。</param>
        /// <param name="Clear">落点离岸多远（游戏算鱼和品质用的 clearWaterDistance，0~5）。</param>
        private sealed record FishingSpot(Point Stand, int Dir, int Distance, int Clear = 0);

        private static readonly Point[] DirVec = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };

        private static int RodAddedDistance(Farmer who) =>
            who.FishingLevel >= 15 ? 4 : who.FishingLevel >= 8 ? 3 : who.FishingLevel >= 4 ? 2 : who.FishingLevel >= 1 ? 1 : 0;

        private static bool Fishable(GameLocation loc, int x, int y) =>
            loc.isTileOnMap(x, y) && loc.isTileFishable(x, y);

        /// <summary>满力能抛多远：左右比上下多 1 格（FishingRod 抛竿时左右乘 加成+4，上下乘 加成+3）。</summary>
        private static int MaxCastDistance(Farmer who, int dir) =>
            RodAddedDistance(who) + (dir is 1 or 3 ? 4 : 3);

        /// <summary>刚好抛到 distance 格的力度，公式和 FishingRod 里算落点的一样（左右会少飞 8 像素）。</summary>
        private static float CastPower(Farmer who, int dir, int distance) =>
            Math.Clamp((distance * 64f + (dir is 1 or 3 ? 8f : 0f)) / (MaxCastDistance(who, dir) * 64f), 0f, 1f);

        /// <summary>
        /// stand 格朝 dir 方向最好的落点：离岸最远，一样远就抛近一点。抛不到返回 null。
        /// 只看抛多远不行：沿着岸往前抛，或者隔河抛到对岸边上，抛得再远也是贴着岸。
        /// 浮漂是飞过去的，游戏只检查落点（FishingRod 里 isTileFishable(落点)），中间隔着岸边碰撞格、栏杆都没关系。
        /// 注意岸边那圈水面通常叠着 Buildings 碰撞格，isTileFishable 是 false，所以不能要求“面前一格可钓”。
        /// 只要求前两格内有水面，确认真的站在岸边/栈桥边，而不是隔着一片陆地往远处的水里抛。
        /// 最少飞 2 格（力度再小游戏也按 128 像素算），所以从 2 开始。
        /// </summary>
        private static FishingSpot BestCast(GameLocation loc, Point stand, int dir)
        {
            var d = DirVec[dir];
            if (!loc.isWaterTile(stand.X + d.X, stand.Y + d.Y) && !loc.isWaterTile(stand.X + d.X * 2, stand.Y + d.Y * 2))
                return null;
            FishingSpot best = null;
            for (int k = 2; k <= MaxCastDistance(Player, dir); k++)
            {
                int x = stand.X + d.X * k, y = stand.Y + d.Y * k;
                if (!Fishable(loc, x, y))
                    continue;
                int clear = FishingRod.distanceToLand(x, y, loc);
                if (best == null || clear > best.Clear)
                    best = new FishingSpot(stand, dir, k, clear);
            }
            return best;
        }

        /// <summary>面前隔了一格陆地才是水：同样远的落点，优先站到紧贴水边的地方。</summary>
        private static int ShoreGap(GameLocation loc, FishingSpot s) =>
            loc.isWaterTile(s.Stand.X + DirVec[s.Dir].X, s.Stand.Y + DirVec[s.Dir].Y) ? 0 : 1;

        /// <summary>在附近找钓鱼点：能站、面朝水，落点离岸越远越好（离岸越远鱼越好），其次站得贴水边、离得近。</summary>
        private static List<FishingSpot> FindFishingSpots(GameLocation loc, Point from, int radius)
        {
            var spots = new List<FishingSpot>();
            int w = Pathfinder.MapWidth(loc), h = Pathfinder.MapHeight(loc);
            for (int x = Math.Max(0, from.X - radius); x <= Math.Min(w - 1, from.X + radius); x++)
            {
                for (int y = Math.Max(0, from.Y - radius); y <= Math.Min(h - 1, from.Y + radius); y++)
                {
                    if (loc.isWaterTile(x, y) || !Pathfinder.IsPassable(loc, x, y) || Pathfinder.IsTouchWarp(loc, x, y))
                        continue;
                    var stand = new Point(x, y);
                    for (int dir = 0; dir < 4; dir++)
                    {
                        if (BestCast(loc, stand, dir) is { } cast)
                            spots.Add(cast);
                    }
                }
            }
            return spots.OrderByDescending(s => s.Clear).ThenBy(s => ShoreGap(loc, s)).ThenBy(s => Util.Manhattan(s.Stand, from)).ToList();
        }

        private const string BagFullForFishing = "背包满了，再钓拿不下的会掉在地上。先把鱼和用不上的东西放进出货箱（ship_items），或者整理（tidy_backpack），空出位置再钓。体力够时别为了腾位置吃鱼";

        public IEnumerable<object> Fish(int casts, int? x, int? y, string direction)
        {
            if (Blocked(out string why))
            {
                yield return ActionResult.Fail(why);
                yield break;
            }
            // 背包里有几根鱼竿时用最好的那根，能装鱼饵/钓具就顺手装上
            int rodIdx = BestRodIndex();
            if (rodIdx < 0)
            {
                yield return ActionResult.Fail("背包里没有鱼竿（第二天早上去海边找威利 Willy 会送你一根，或者在鱼店 FishShop 买）");
                yield break;
            }
            // 背包满时游戏会弹物品界面，拿不下的只能丢在地上，鱼就白钓了
            if (Player.freeSpotsInInventory() == 0)
            {
                yield return ActionResult.Fail(BagFullForFishing);
                yield break;
            }
            var bestRod = (FishingRod)Player.Items[rodIdx];
            string rodNote = FillRodAttachments(bestRod);
            var loc = Loc;
            if (!loc.canFishHere())
            {
                yield return ActionResult.Fail("这里不能钓鱼");
                yield break;
            }
            casts = Math.Clamp(casts, 1, 20);

            // ── 选钓鱼点 ──
            FishingSpot spot = null;
            if (x.HasValue && y.HasValue)
            {
                var stand = new Point(x.Value, y.Value);
                IEnumerable<int> dirs = string.IsNullOrEmpty(direction) ? Enumerable.Range(0, 4) : new[] { Util.ParseDir(direction) };
                foreach (int dir in dirs.Where(d => d >= 0))
                {
                    if (BestCast(loc, stand, dir) is { } cast && (spot == null || cast.Clear > spot.Clear))
                        spot = cast;
                }
                if (spot == null)
                {
                    yield return ActionResult.Fail($"站在 ({stand.X},{stand.Y}) 面前没有可以钓鱼的水面，不传坐标我会自动找钓鱼点");
                    yield break;
                }
            }

            var o = new Outcome();
            if (spot == null)
            {
                var spots = FindFishingSpots(loc, Player.TilePoint, 30);
                if (spots.Count == 0)
                {
                    yield return ActionResult.Fail("附近 30 格内找不到能钓鱼的岸边，换个有水的地方（Beach、Mountain 的湖、Town/Forest 的河）");
                    yield break;
                }
                // 从落点离岸最远的一档开始，每档做一次多目标寻路，取最近的走得到的点（紧贴水边的先试）
                foreach (var level in spots.GroupBy(s => (s.Clear, Gap: ShoreGap(loc, s))).OrderByDescending(g => g.Key.Clear).ThenBy(g => g.Key.Gap))
                {
                    var byStand = level.GroupBy(s => s.Stand).ToDictionary(g => g.Key, g => g.First());
                    var path = Pathfinder.FindPath(loc, Player.TilePoint, byStand.Keys.ToList());
                    if (path != null)
                    {
                        spot = byStand[path[^1]];
                        break;
                    }
                }
                if (spot == null)
                {
                    yield return ActionResult.Fail("找到了水面但走不过去");
                    yield break;
                }
            }

            foreach (var s in GoToAny(new[] { spot.Stand }, o, "钓鱼点"))
                yield return s;
            if (!o.Ok)
            {
                yield return ActionResult.Fail(o.Message);
                yield break;
            }

            var caught = new List<string>();
            int misses = 0;
            string stopReason = null;
            try
            {
                FishingAssist.Active = true;
                for (int c = 0; c < casts; c++)
                {
                    if (ChatWantsAttention())
                    {
                        stopReason = "有人说话了，先停下来";
                        break;
                    }
                    if (Player.freeSpotsInInventory() == 0)
                    {
                        stopReason = BagFullForFishing;
                        break;
                    }
                    if (Player.Stamina <= 10f)
                    {
                        stopReason = "体力不足，停止钓鱼";
                        break;
                    }
                    if (Game1.timeOfDay >= 2400)
                    {
                        stopReason = "太晚了，该回家睡觉";
                        break;
                    }
                    var one = new Outcome();
                    foreach (var s in FishOnce(rodIdx, spot, one))
                        yield return s;
                    if (one.Ok)
                        caught.Add(one.Message);
                    else if (one.Message.StartsWith("!"))
                    {
                        stopReason = one.Message.Substring(1);
                        break;
                    }
                    else
                        misses++;
                }
            }
            finally
            {
                FishingAssist.Active = false;
            }

            string summary = $"用 {bestRod.DisplayName}" + (rodNote != null ? $"（{rodNote}）" : "")
                             + $"在 ({spot.Stand.X},{spot.Stand.Y}) 朝{Util.DirName(spot.Dir)}抛 {spot.Distance} 格（落点离岸 {spot.Clear} 格，最多算 5）钓鱼，钓到 {caught.Count} 次";
            if (caught.Count > 0)
                summary += "：" + string.Join("、", caught);
            if (misses > 0)
                summary += $"，跑掉/没咬钩 {misses} 次";
            if (stopReason != null)
                summary += $"（{stopReason}）";
            summary += $"，体力 {(int)Player.Stamina}，现在 {Util.TimeString(Game1.timeOfDay)}";
            yield return caught.Count > 0 || misses > 0 ? ActionResult.Success(summary) : ActionResult.Fail(summary);
        }

        /// <summary>
        /// 抛一次竿直到结束。o.Ok=钓到（Message 为物品名）；失败时 Message 以 "!" 开头表示应停止整个钓鱼动作。
        /// </summary>
        private IEnumerable<object> FishOnce(int rodIdx, FishingSpot spot, Outcome o)
        {
            foreach (var _ in WaitPlayerFree())
                yield return null;
            if (Blocked(out string why))
            {
                o.Fail("!" + why);
                yield break;
            }
            // rodIdx = -1：用手上的鱼竿（冰钓比赛发的竹竿是 TemporaryItem，不在背包里）
            var rod = rodIdx < 0 ? Player.CurrentTool as FishingRod
                    : rodIdx < Player.Items.Count ? Player.Items[rodIdx] as FishingRod : null;
            if (rod == null)
            {
                o.Fail("!鱼竿不见了");
                yield break;
            }

            SnapToTile(spot.Stand);
            if (rodIdx >= 0)
                Player.CurrentToolIndex = rodIdx;
            Player.faceDirection(spot.Dir);
            var d = DirVec[spot.Dir];
            var landing = new Point(spot.Stand.X + d.X * spot.Distance, spot.Stand.Y + d.Y * spot.Distance);
            Player.lastClick = new Vector2(landing.X * 64 + 32, landing.Y * 64 + 32);

            // 抛竿：beginUsing 进入蓄力；真实鼠标是松开的，下一帧游戏就会出竿，所以这里直接设定力度
            Player.BeginUsingTool();
            rod.castingPower = CastPower(Player, spot.Dir, spot.Distance);

            // 等浮漂落水
            int t = 0;
            while (!rod.isFishing && t++ < 300)
            {
                if (!Player.UsingTool && t > 20)
                    break;
                yield return null;
            }
            if (!rod.isFishing)
            {
                foreach (var _ in WaitPlayerFree())
                    yield return null;
                o.Fail("!没抛进水里（可能离水太远或者被挡住了）");
                yield break;
            }

            // 等咬钩（最多约 40 秒）
            t = 0;
            while (rod.isFishing && !rod.isNibbling && t++ < 2400)
            {
                if (Game1.activeClickableMenu != null && Game1.activeClickableMenu is not BobberBar)
                    break;
                yield return null;
            }
            if (!rod.isNibbling)
            {
                // 收竿
                if (rod.isFishing && Game1.activeClickableMenu == null)
                    Player.EndUsingTool();
                foreach (var _ in WaitPlayerFree(300))
                    yield return null;
                o.Fail("等了很久没有鱼咬钩");
                yield break;
            }

            // 咬钩了，稍微停顿一下再提竿（和人的反应差不多）
            foreach (var _ in WaitTicks(8))
                yield return null;
            if (!rod.isNibbling)
            {
                o.Fail("鱼跑了");
                yield break;
            }
            Player.EndUsingTool();

            // 等小游戏出现，或者直接钓上来（垃圾、海草之类没有小游戏）
            t = 0;
            while (t++ < 300 && Game1.activeClickableMenu is not BobberBar && !rod.fishCaught && !rod.showingTreasure)
                yield return null;

            bool sawBar = false, sawCaught = rod.fishCaught;
            float lastProgress = 0f;
            bool perfect = false;
            t = 0;
            while (Game1.activeClickableMenu is BobberBar bar && t++ < 60 * 90)
            {
                sawBar = true;
                lastProgress = bar.distanceFromCatching;
                perfect = bar.perfect;
                yield return null;
            }

            // 等钓上来的展示动画 / 宝箱
            t = 0;
            while (t++ < 600 && !rod.fishCaught)
            {
                if (Game1.activeClickableMenu is ItemGrabMenu)
                    break;
                if (!Player.UsingTool && !rod.showingTreasure && !rod.pullingOutOfWater && t > 30)
                    break;
                yield return null;
            }
            sawCaught |= rod.fishCaught || rod.showingTreasure || Game1.activeClickableMenu is ItemGrabMenu;

            string fishName = "某样东西";
            if (rod.whichFish != null)
            {
                try
                {
                    fishName = ItemRegistry.GetDataOrErrorItem(rod.whichFish.QualifiedItemId).DisplayName;
                }
                catch
                {
                    fishName = rod.whichFish.QualifiedItemId;
                }
            }

            if (rod.fishCaught)
            {
                // 相当于手动点一下，把鱼收进背包
                foreach (var _ in WaitTicks(20))
                    yield return null;
                rod.doneHoldingFish(Player);
                foreach (var _ in WaitTicks(10))
                    yield return null;
            }

            // 宝箱或背包满时会弹出物品界面，能拿的都拿走，拿不下的丢在地上
            var dropped = new List<string>();
            for (int i = 0; i < 240; i++)
            {
                if (Game1.activeClickableMenu is ItemGrabMenu grab)
                {
                    dropped = GrabAll(grab);
                    break;
                }
                if (!rod.showingTreasure && !rod.fishCaught && Player.CanMove && !Player.UsingTool)
                    break;
                yield return null;
            }

            foreach (var _ in WaitPlayerFree(300))
                yield return null;

            // 有小游戏时看进度条是否满；没有小游戏（垃圾等）看有没有“举起物品”
            bool success = sawBar ? lastProgress >= 0.9f : sawCaught;
            if (success)
                o.Succeed(fishName + (sawBar && perfect ? "(完美)" : "")
                    + (dropped.Count > 0 ? $"（背包满，{string.Join("、", dropped)} 掉在脚边了，空出位置再走过去能捡回来）" : ""));
            else
                o.Fail(sawBar ? $"{fishName} 跑掉了" : "提竿失败");
        }

        /// <summary>拿走物品界面里的东西，返回拿不下、丢在地上的物品名。</summary>
        private static List<string> GrabAll(ItemGrabMenu grab)
        {
            var dropped = new List<string>();
            var items = grab.ItemsToGrabMenu?.actualInventory;
            if (items != null)
            {
                // 先拍快照：回调（比如自动采集器的）会修改同一个列表
                foreach (var item in items.Where(i => i != null).ToList())
                {
                    items.Remove(item);
                    var left = Player.addItemToInventory(item);
                    if (left != null)
                    {
                        dropped.Add(left.DisplayName);
                        Game1.createItemDebris(left, Player.getStandingPosition(), Player.FacingDirection);
                    }
                    // 和手动拿走一样触发界面的回调（社区中心奖励靠它标记“已领取”）
                    grab.behaviorOnItemGrab?.Invoke(item, Player);
                }
            }
            grab.exitThisMenu(playSound: false);
            // 自动采集器的回调每拿一件都会重新打开一个物品界面，空了就一起关掉
            if (Game1.activeClickableMenu is ItemGrabMenu reopened && reopened != grab
                && !(reopened.ItemsToGrabMenu?.actualInventory.Any(i => i != null) ?? false))
                reopened.exitThisMenu(playSound: false);
            return dropped;
        }
    }
}
