using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.Tools;
using xLocation = xTile.Dimensions.Location;

namespace BlindWhaleFarmer
{
    /// <summary>
    /// 矿洞里清路：杂草、石头、木桶、地上的采集物都会挡路（Object.isPassable 对它们都是 false），
    /// 普通寻路把它们当墙。这里允许寻路穿过它们，走到跟前再清掉。
    /// </summary>
    internal sealed partial class GameActions
    {
        /// <summary>矿洞、骷髅洞穴、火山地牢：这些地方的杂草和木桶清掉不心疼。</summary>
        private static bool InDungeon => Loc is MineShaft || Loc is VolcanoDungeon;

        /// <summary>
        /// 这格挡路的东西能不能清掉。杂草、木桶（游戏里 isHeavyHitter 的工具都能打：武器、镰刀、镐子、斧头、锄头）、
        /// 能敲的石头（镐子）、地上能捡的采集物（洞穴萝卜、蘑菇、水晶之类，捡起来就不挡了）。
        /// </summary>
        private static bool ClearableObstacle(Point p)
        {
            if (!InDungeon || !Loc.objects.TryGetValue(new Vector2(p.X, p.Y), out var obj) || obj.isPassable())
                return false;
            // 墙上不会有这些东西，保险起见：Buildings 层有图块的格子清了也走不了
            if (Loc.map?.GetLayer("Buildings")?.Tiles[p.X, p.Y] != null)
                return false;
            if (obj.IsBreakableStone())
                return Util.FindTool<Pickaxe>() >= 0;
            return obj.IsWeeds() || obj is BreakableContainer || (obj.IsSpawnedObject && obj.isForage());
        }

        /// <summary>清掉 p 上的障碍（她已经站在旁边）。maxHits 是最多挥几下；打怪时只挥一两下就回去重新判断。</summary>
        private IEnumerable<object> ClearObstacle(Point p, Outcome o, int maxHits = 12, bool stopOnThreat = true)
        {
            var key = new Vector2(p.X, p.Y);
            if (!Loc.objects.TryGetValue(key, out var obj))
            {
                o.Succeed();
                yield break;
            }
            string name = obj.DisplayName;
            if (obj.IsSpawnedObject && obj.isForage())
            {
                Face(p);
                Loc.checkAction(new xLocation(p.X, p.Y), Game1.viewport, Player);
                foreach (var s in WaitTicks(10))
                    yield return s;
                if (Loc.objects.ContainsKey(key))
                    o.Fail($"挡路的{name}捡不起来（背包满了？）");
                else
                    o.Succeed();
                yield break;
            }
            // 石头只能用镐子；杂草和木桶优先用镰刀/武器砍，不费体力
            int tool = -1;
            if (!obj.IsBreakableStone())
            {
                tool = Util.FindTool<MeleeWeapon>(w => w.isScythe());
                if (tool < 0)
                    tool = Util.FindTool<MeleeWeapon>();
            }
            if (tool < 0)
                tool = Util.FindTool<Pickaxe>();
            if (tool < 0)
            {
                o.Fail($"没有能清掉{name}的工具");
                yield break;
            }
            for (int k = 0; k < maxHits && Loc.objects.ContainsKey(key); k++)
            {
                if (stopOnThreat && (NearestThreat(2) != null || Player.health < Player.maxHealth * 0.4f))
                {
                    o.Fail($"清{name}时有怪靠近");
                    yield break;
                }
                foreach (var s in UseToolOnce(tool, p, o))
                    yield return s;
                if (!o.Ok)
                    yield break;
            }
            if (Loc.objects.ContainsKey(key) && maxHits >= 12)
                o.Fail($"({p.X},{p.Y}) 的{name}打了 {maxHits} 下还没清掉");
            else
                o.Succeed();
        }

        /// <summary>路上顺手采到的东西，下一次工具返回时报告给 AI 后清空。</summary>
        private static readonly List<string> pickedOnTheWay = new();

        /// <summary>取出"路上顺手采了什么"的提示（没有返回 null）。</summary>
        public static string TakePickedOnTheWay()
        {
            if (pickedOnTheWay.Count == 0)
                return null;
            string msg = "🌿 路上顺手采了：" + string.Join("、", pickedOnTheWay.GroupBy(n => n).Select(g => g.Count() > 1 ? $"{g.Key}×{g.Count()}" : g.Key));
            pickedOnTheWay.Clear();
            return msg;
        }

        /// <summary>走路时顺手采集的范围（格，含斜角）。0 = 不采；1 = 只采紧挨着的；更大会离开路线几步去采再回来。由 ModEntry 按配置设置。</summary>
        public static int ForageRadius = 1;

        /// <summary>正在绕去采东西：这期间的走路不再嵌套触发顺手采集。</summary>
        private static bool pickingForage;

        /// <summary>这格是不是现在就能顺手采的野生采集物（有村民站着的不算：交互会变成和他说话，手里拿着礼物还会送出去）。</summary>
        private static bool PickableForage(GameLocation loc, Point p)
        {
            if (!loc.objects.TryGetValue(new Vector2(p.X, p.Y), out var obj) || !obj.IsSpawnedObject || !obj.isForage()
                || obj.questItem.Value || !Player.couldInventoryAcceptThisItem(obj))
                return false;
            var rect = new Rectangle(p.X * 64, p.Y * 64, 64, 64);
            return !loc.characters.Any(c => c != null && c.GetBoundingBox().Intersects(rect));
        }

        /// <summary>
        /// 走路时 ForageRadius 格内（含斜角）有能采的野生采集物（季节野菜、贝壳、洞穴萝卜之类）就顺手采了，和手动按交互键一样
        /// （会算采集经验、品质）。不挨着的先走过去（来回最多多走 4×半径 步，绕太远的不去），采完回到原来那格接着走。
        /// 节日/剧情里不采；背包装不下、别人任务专用的不采。
        /// </summary>
        private IEnumerable<object> PickUpForageNearby(GameLocation loc, int walkDir)
        {
            int r = ForageRadius;
            if (r <= 0 || pickingForage || Game1.CurrentEvent != null || Game1.activeClickableMenu != null)
                yield break;
            var home = Player.TilePoint;
            var targets = new List<Point>();
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    if ((dx != 0 || dy != 0) && PickableForage(loc, new Point(home.X + dx, home.Y + dy)))
                        targets.Add(new Point(home.X + dx, home.Y + dy));
            if (targets.Count == 0)
                yield break;

            pickingForage = true;
            try
            {
                var o = new Outcome();
                foreach (var p in targets.OrderBy(t => Util.Manhattan(t, home)))
                {
                    if (!PickableForage(loc, p))
                        continue;
                    var me = Player.TilePoint;
                    if (System.Math.Max(System.Math.Abs(p.X - me.X), System.Math.Abs(p.Y - me.Y)) > 1)
                    {
                        var stands = Pathfinder.AdjacentStandTiles(loc, p);
                        var path = stands.Count > 0 ? Pathfinder.FindPath(loc, me, stands, 3000) : null;
                        if (path == null || path.Count - 1 > 2 * r)
                            continue;
                        foreach (var s in WalkPath(path, o))
                            yield return s;
                        if (!o.Ok)
                            break;
                    }
                    string name = loc.objects[new Vector2(p.X, p.Y)].DisplayName;
                    Player.Halt();
                    Face(p);
                    loc.checkAction(new xLocation(p.X, p.Y), Game1.viewport, Player);
                    foreach (var s in WaitPlayerFree(60))
                        yield return s;
                    if (!loc.objects.ContainsKey(new Vector2(p.X, p.Y)))
                        pickedOnTheWay.Add(name);
                    if (Game1.activeClickableMenu != null || Loc != loc)
                        yield break;
                }
                // 回到原来那格：外面的 WalkPath 从这格直线走向下一格
                if (Player.TilePoint != home && Loc == loc)
                {
                    var back = Pathfinder.FindPath(loc, Player.TilePoint, new[] { home }, 3000);
                    if (back != null)
                        foreach (var s in WalkPath(back, o))
                            yield return s;
                }
            }
            finally
            {
                pickingForage = false;
            }
            Player.faceDirection(walkDir);
        }

        /// <summary>target 四周能站、或者有能清掉的障碍的格子。</summary>
        private static List<Point> StandOrClearTiles(Point target) =>
            Pathfinder.Dirs.Select(d => new Point(target.X + d.X, target.Y + d.Y))
                .Where(p => Pathfinder.IsPassable(Loc, p.X, p.Y) || ClearableObstacle(p))
                .ToList();

        /// <summary>
        /// 普通寻路走不到时用：路线可以穿过杂草、石头、木桶、采集物，边走边清。
        /// 每清掉一个就重新寻路（清掉以后可能有更近的路）。
        /// </summary>
        private IEnumerable<object> GoToAnyClearing(ICollection<Point> goals, Outcome o, string label)
        {
            for (int round = 0; round < 20; round++)
            {
                var me = Player.TilePoint;
                if (goals.Contains(me))
                {
                    o.Succeed();
                    yield break;
                }
                var path = goals.Count > 0 ? Pathfinder.FindPath(Loc, me, goals, 20000, clearable: ClearableObstacle) : null;
                if (path == null)
                {
                    o.Fail($"走不到{label}（路上的杂草石头清掉了也过不去）");
                    yield break;
                }
                int block = path.FindIndex(1, p => ClearableObstacle(p));
                if (block < 0)
                {
                    foreach (var s in WalkPath(path, o))
                        yield return s;
                    yield break;
                }
                if (block > 1)
                {
                    foreach (var s in WalkPath(path.Take(block).ToList(), o))
                        yield return s;
                    if (!o.Ok)
                        yield break;
                }
                foreach (var s in ClearObstacle(path[block], o))
                    yield return s;
                if (!o.Ok)
                    yield break;
            }
            o.Fail($"去{label}的路上障碍太多");
        }

        /// <summary>
        /// 在矿洞里普通寻路到不了这些站位时，改走清路的路线。返回 false 表示不需要（不在矿洞，或者正常能走到）。
        /// </summary>
        private static bool NeedsClearing(Point target, List<Point> stands) =>
            InDungeon && (stands.Count == 0 || Pathfinder.FindPath(Loc, Player.TilePoint, stands, 20000) == null)
            && StandOrClearTiles(target).Count > 0;
    }
}
