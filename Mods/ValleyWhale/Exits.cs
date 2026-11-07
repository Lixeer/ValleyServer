using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;

namespace BlindWhaleFarmer
{
    internal enum ExitKind
    {
        /// <summary>地图边缘/地面传送点，走过去即可切换地点。</summary>
        Warp,
        /// <summary>需要对门交互（可能有营业时间）。</summary>
        Door,
        /// <summary>农场上的建筑门。</summary>
        BuildingDoor
    }

    internal sealed class Exit
    {
        public ExitKind Kind;
        public Point Tile;
        public string Target;
        public Point TargetTile;
        public Building Building;
        /// <summary>踩上去由游戏自动触发的传送。</summary>
        public bool IsTouch;
        public string Note = "";
        /// <summary>LockedDoorWarp 要求的好感：比如玛鲁房间的侧门要和玛鲁 500 点（2 心）。没有要求时为 null。</summary>
        public string FriendNpc;
        public int FriendPoints;

        /// <summary>和 GameLocation.lockedDoorWarp 一样判断好感门槛（冬天这类门不限好感）。</summary>
        public bool FriendshipOk(GameLocation from) =>
            FriendNpc == null || from.IsWinterHere()
            || (Game1.player.friendshipData.TryGetValue(FriendNpc, out var f) && f.Points >= FriendPoints);

        public string Describe()
        {
            string kind = Kind switch
            {
                ExitKind.Warp => "出口",
                ExitKind.Door => "门",
                _ => "建筑门"
            };
            return $"{kind} ({Tile.X},{Tile.Y}) → {Target}{Note}";
        }
    }

    /// <summary>计算每个地点的出口，并在地点之间做广度优先搜索规划路线。</summary>
    internal static class Exits
    {
        private static readonly Dictionary<string, List<Exit>> TileExitCache = new();

        public static void ClearCache() => TileExitCache.Clear();

        public static List<Exit> Get(GameLocation loc)
        {
            var list = new List<Exit>();
            if (loc?.Map == null)
                return list;

            foreach (var w in loc.warps)
            {
                if (w.npcOnly.Value || Game1.getLocationFromName(w.TargetName) == null)
                    continue;
                list.Add(new Exit { Kind = ExitKind.Warp, Tile = new Point(w.X, w.Y), Target = w.TargetName, TargetTile = new Point(w.TargetX, w.TargetY) });
            }

            list.AddRange(GetTileExits(loc));

            foreach (var b in loc.buildings)
            {
                string indoors = null;
                try
                {
                    indoors = b.GetIndoorsName();
                }
                catch
                {
                    // 某些建筑没有室内
                }
                if (string.IsNullOrEmpty(indoors) || b.isUnderConstruction())
                    continue;
                var door = b.humanDoor.Value;
                if (door.X < 0 || door.Y < 0)
                    continue;
                list.Add(new Exit
                {
                    Kind = ExitKind.BuildingDoor,
                    Tile = new Point(b.tileX.Value + door.X, b.tileY.Value + door.Y),
                    Target = indoors,
                    Building = b,
                    Note = $"（{b.buildingType.Value}）"
                });
            }

            // 合并同一目标、相邻的传送格，只保留一个代表，避免列表太长
            var merged = new List<Exit>();
            foreach (var e in list)
            {
                bool dup = merged.Any(m => m.Kind == e.Kind && m.Target == e.Target
                    && Math.Abs(m.Tile.X - e.Tile.X) + Math.Abs(m.Tile.Y - e.Tile.Y) <= 3);
                if (!dup)
                    merged.Add(e);
            }
            return merged;
        }

        private static List<Exit> GetTileExits(GameLocation loc)
        {
            string key = loc.NameOrUniqueName;
            if (TileExitCache.TryGetValue(key, out var cached))
                return cached;

            var list = new List<Exit>();
            int w = Pathfinder.MapWidth(loc), h = Pathfinder.MapHeight(loc);
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    string action = loc.doesTileHaveProperty(x, y, "Action", "Buildings");
                    if (!string.IsNullOrEmpty(action))
                    {
                        var e = ParseDoorAction(action, x, y);
                        if (e != null)
                            list.Add(e);
                    }

                    string touch = loc.doesTileHaveProperty(x, y, "TouchAction", "Back");
                    if (!string.IsNullOrEmpty(touch))
                    {
                        var parts = touch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4 && (parts[0] == "Warp" || parts[0] == "MagicWarp")
                            && Game1.getLocationFromName(parts[1]) != null
                            && int.TryParse(parts[2], out int tx) && int.TryParse(parts[3], out int ty))
                        {
                            list.Add(new Exit { Kind = ExitKind.Warp, Tile = new Point(x, y), Target = parts[1], TargetTile = new Point(tx, ty), IsTouch = true });
                        }
                    }
                }
            }
            TileExitCache[key] = list;
            return list;
        }

        private static Exit ParseDoorAction(string action, int x, int y)
        {
            var p = action.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0)
                return null;
            switch (p[0])
            {
                case "Warp":
                case "LockedDoorWarp":
                case "WarpMensLocker":
                case "WarpWomensLocker":
                    if (p.Length >= 4 && Game1.getLocationFromName(p[3]) != null
                        && int.TryParse(p[1], out int tx) && int.TryParse(p[2], out int ty))
                    {
                        string note = "";
                        if (p[0] == "LockedDoorWarp" && p.Length >= 6)
                            note = $"（营业 {p[4]}-{p[5]}）";
                        var exit = new Exit { Kind = ExitKind.Door, Tile = new Point(x, y), Target = p[3], TargetTile = new Point(tx, ty), Note = note };
                        // LockedDoorWarp x y 地点 开门 关门 [NPC 好感点数]
                        if (p[0] == "LockedDoorWarp" && p.Length >= 8 && int.TryParse(p[7], out int points) && points > 0)
                        {
                            exit.FriendNpc = p[6];
                            exit.FriendPoints = points;
                            exit.Note += $"（要和 {p[6]} 有 {points / 250} 心好感）";
                        }
                        return exit;
                    }
                    break;
                case "WarpCommunityCenter":
                    return new Exit { Kind = ExitKind.Door, Tile = new Point(x, y), Target = "CommunityCenter" };
            }
            return null;
        }

        /// <summary>从当前地点到目标地点的一串出口（BFS，最多 12 跳）。</summary>
        /// <summary>一个出口的标识：Get 每次可能给新对象，不能直接比较对象。</summary>
        public static string ExitKey(string fromName, Exit e) => $"{fromName}>{e.Target}@{e.Tile.X},{e.Tile.Y}";

        /// <param name="avoid">这次走不通的出口（ExitKey），规划时绕开。</param>
        public static List<Exit> PlanRoute(GameLocation from, string targetName, ISet<string> avoid = null)
        {
            if (from.NameOrUniqueName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                return new List<Exit>();

            var prev = new Dictionary<string, (string, Exit)>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<(string name, int depth)>();
            queue.Enqueue((from.NameOrUniqueName, 0));
            prev[from.NameOrUniqueName] = (null, null);

            while (queue.Count > 0)
            {
                var (name, depth) = queue.Dequeue();
                if (depth >= 12)
                    continue;
                var loc = Game1.getLocationFromName(name);
                if (loc == null)
                    continue;
                // 好感不够进不去的门（比如玛鲁房间的侧门）不走，同一个目标通常还有正门
                foreach (var e in Get(loc).Where(e => e.FriendshipOk(loc)))
                {
                    if (avoid != null && avoid.Contains(ExitKey(name, e)))
                        continue;
                    if (prev.ContainsKey(e.Target))
                        continue;
                    prev[e.Target] = (name, e);
                    if (e.Target.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        var route = new List<Exit>();
                        string cur = e.Target;
                        while (prev[cur].Item2 != null)
                        {
                            route.Add(prev[cur].Item2);
                            cur = prev[cur].Item1;
                        }
                        route.Reverse();
                        return route;
                    }
                    queue.Enqueue((e.Target, depth + 1));
                }
            }
            return null;
        }
    }
}
