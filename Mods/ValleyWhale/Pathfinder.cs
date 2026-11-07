using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;

namespace BlindWhaleFarmer
{
    /// <summary>基于格子的 A* 寻路（四方向）。</summary>
    internal static class Pathfinder
    {
        public static readonly Point[] Dirs = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };

        public static int MapWidth(GameLocation loc) => loc.Map.Layers[0].LayerWidth;
        public static int MapHeight(GameLocation loc) => loc.Map.Layers[0].LayerHeight;

        /// <summary>农夫能否站在这一格。用的是农夫站在这格时的碰撞箱（和 Farmer.GetBoundingBox 一样）。</summary>
        public static bool IsPassable(GameLocation loc, int x, int y)
        {
            if (!loc.isTileOnMap(x, y))
                return false;
            // Farmer.GetBoundingBox = (X+8, Y+精灵高-32, 48, 32)，站在格子上时 Position = 格子左上角，碰撞箱是这格的上半截。
            // 以前写成 y*64+32，下边缘正好压到下一行（Bottom/64 = y+1），等于每格都顺带检查下面一格，
            // 海滩木栈道这种下面一排是栏杆的地方就会被误判成走不通。
            int top = Game1.player?.Sprite != null ? Game1.player.Sprite.getHeight() - 32 : 0;
            var rect = new Rectangle(x * 64 + 8, y * 64 + top, 48, 32);
            // pathfinding + skipCollisionEffects：只做检测，不触发推开 NPC、草晃动等副作用；NPC 不算障碍（会走开）
            return !loc.isCollidingPosition(rect, Game1.viewport, true, 0, false, Game1.player,
                pathfinding: true, projectile: false, ignoreCharacterRequirement: false, skipCollisionEffects: true);
        }

        /// <summary>踩上去就会触发传送的格子（TouchAction Warp）。寻路时绕开，除非它就是目的地。</summary>
        public static bool IsTouchWarp(GameLocation loc, int x, int y)
        {
            string touch = loc.doesTileHaveProperty(x, y, "TouchAction", "Back");
            return touch != null && (touch.StartsWith("Warp") || touch.StartsWith("MagicWarp"));
        }

        /// <summary>
        /// 关着的室内门（Buildings 层 Action "Door [主人…]"）。关门时那格有碰撞，开门后才能走。
        /// 返回 Action 拆开后的参数（第 0 个是 "Door"），不是关着的门返回 null。
        /// </summary>
        public static string[] ClosedDoor(GameLocation loc, Point p)
        {
            if (!loc.interiorDoors.TryGetValue(p, out bool isOpen) || isOpen)
                return null;
            string action = loc.doesTileHaveProperty(p.X, p.Y, "Action", "Buildings");
            if (action == null || !action.StartsWith("Door"))
                return null;
            return ArgUtility.SplitBySpace(action);
        }

        /// <summary>和 GameLocation.performAction 的 "Door" 一样：没写主人随便开；写了主人要有 2 心好感或以前开过。</summary>
        public static bool CanOpenDoor(GameLocation loc, string[] door)
        {
            if (door.Length <= 1)
                return true;
            for (int i = 1; i < door.Length; i++)
            {
                if (Game1.player.getFriendshipHeartLevelForNPC(door[i]) >= 2 || Game1.player.mailReceived.Contains("doorUnlock" + door[i]))
                    return true;
                if (door[i] == "Sebastian" && loc.IsGreenRainingHere() && Game1.year == 1)
                    return true;
            }
            return false;
        }

        /// <summary>寻路时这一格能不能踩：能开的门算能走；传送格只在它就是目的地时才踩。</summary>
        private static bool StepPassable(GameLocation loc, Point p, HashSet<Point> goalSet, bool throughLockedDoors)
        {
            if (p.X < 0 || p.Y < 0 || p.X >= MapWidth(loc) || p.Y >= MapHeight(loc))
                return false;
            var door = ClosedDoor(loc, p);
            if (door != null)
                return throughLockedDoors || CanOpenDoor(loc, door);
            return IsPassable(loc, p.X, p.Y) && (goalSet.Contains(p) || !IsTouchWarp(loc, p.X, p.Y));
        }

        /// <summary>
        /// 找不到路时用：从 start 出发能走到多少格（最多数到 maxNodes），以及其中离 goals 最近的一格。
        /// </summary>
        public static (int Reachable, Point Closest, int Distance) Explore(GameLocation loc, Point start, ICollection<Point> goals, int maxNodes = 20000)
        {
            var goalSet = new HashSet<Point>(goals);
            var seen = new HashSet<Point> { start };
            var queue = new Queue<Point>();
            queue.Enqueue(start);
            Point closest = start;
            int best = int.MaxValue;
            while (queue.Count > 0 && seen.Count < maxNodes)
            {
                var cur = queue.Dequeue();
                foreach (var g in goalSet)
                {
                    int dist = Math.Abs(g.X - cur.X) + Math.Abs(g.Y - cur.Y);
                    if (dist < best)
                    {
                        best = dist;
                        closest = cur;
                    }
                }
                foreach (var d in Dirs)
                {
                    var next = new Point(cur.X + d.X, cur.Y + d.Y);
                    if (!seen.Contains(next) && StepPassable(loc, next, goalSet, false))
                    {
                        seen.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }
            return (seen.Count, closest, best);
        }

        /// <summary>
        /// 寻找从 start 到任一 goal 的路径（含起点），找不到返回 null。能开的室内门算可走；
        /// throughLockedDoors 时锁着的门也算，只用来解释为什么走不到。
        /// clearable 给了的话，它认可的格子（杂草、石头这类能清掉的障碍）也算能走，每格多算 clearCost 步，
        /// 这样能绕就绕，绕不过去才穿过去；走的时候由调用方把路上的障碍清掉。
        /// </summary>
        public static List<Point> FindPath(GameLocation loc, Point start, ICollection<Point> goals, int maxNodes = 60000, bool throughLockedDoors = false,
            Func<Point, bool> clearable = null, int clearCost = 4)
        {
            if (goals.Count == 0)
                return null;
            var goalSet = new HashSet<Point>(goals);
            if (goalSet.Contains(start))
                return new List<Point> { start };

            // 0 = 走不了，1 = 能走，2 = 有障碍但能清掉
            var passCache = new Dictionary<Point, int>();
            int Passable(Point p)
            {
                if (!passCache.TryGetValue(p, out int v))
                    passCache[p] = v = StepPassable(loc, p, goalSet, throughLockedDoors) ? 1
                        : clearable != null && p.X >= 0 && p.Y >= 0 && p.X < MapWidth(loc) && p.Y < MapHeight(loc) && clearable(p) ? 2 : 0;
                return v;
            }

            int Heuristic(Point p)
            {
                int best = int.MaxValue;
                foreach (var g in goalSet)
                    best = Math.Min(best, Math.Abs(g.X - p.X) + Math.Abs(g.Y - p.Y));
                return best;
            }

            var open = new PriorityQueue<Point, int>();
            var cameFrom = new Dictionary<Point, Point>();
            var gScore = new Dictionary<Point, int> { [start] = 0 };
            open.Enqueue(start, Heuristic(start));
            int expanded = 0;

            while (open.Count > 0 && expanded < maxNodes)
            {
                var cur = open.Dequeue();
                expanded++;
                if (goalSet.Contains(cur))
                {
                    var path = new List<Point> { cur };
                    while (cameFrom.TryGetValue(cur, out var prev))
                    {
                        cur = prev;
                        path.Add(cur);
                    }
                    path.Reverse();
                    return path;
                }

                int curG = gScore[cur];
                foreach (var d in Dirs)
                {
                    var next = new Point(cur.X + d.X, cur.Y + d.Y);
                    int pass = Passable(next);
                    if (pass == 0)
                        continue;
                    int ng = curG + (pass == 2 ? 1 + clearCost : 1);
                    if (gScore.TryGetValue(next, out int old) && old <= ng)
                        continue;
                    gScore[next] = ng;
                    cameFrom[next] = cur;
                    open.Enqueue(next, ng + Heuristic(next));
                }
            }
            return null;
        }

        /// <summary>目标格四周可站立的格子（用于对目标使用工具/交互）。</summary>
        public static List<Point> AdjacentStandTiles(GameLocation loc, Point target)
        {
            var list = new List<Point>();
            foreach (var d in Dirs)
            {
                var p = new Point(target.X + d.X, target.Y + d.Y);
                if (IsPassable(loc, p.X, p.Y))
                    list.Add(p);
            }
            return list;
        }

        /// <summary>离 target 最近的若干可站立格（target 本身不可站时用，比如地图边缘的传送点）。</summary>
        public static List<Point> NearestStandTiles(GameLocation loc, Point target, int radius)
        {
            var list = new List<Point>();
            int w = MapWidth(loc), h = MapHeight(loc);
            for (int r = 0; r <= radius && list.Count == 0; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                            continue;
                        int x = target.X + dx, y = target.Y + dy;
                        if (x < 0 || y < 0 || x >= w || y >= h)
                            continue;
                        if (IsPassable(loc, x, y))
                            list.Add(new Point(x, y));
                    }
                }
            }
            return list;
        }

        public static int DirectionTo(Point from, Point to)
        {
            int dx = to.X - from.X, dy = to.Y - from.Y;
            if (Math.Abs(dx) > Math.Abs(dy))
                return dx > 0 ? 1 : 3;
            return dy < 0 ? 0 : 2;
        }
    }
}
