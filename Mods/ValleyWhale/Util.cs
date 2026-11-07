using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace BlindWhaleFarmer
{
    internal static class Util
    {
        public static string Season => SeasonName(Game1.currentSeason);

        public static string SeasonName(string season) => season?.ToLowerInvariant() switch
        {
            "spring" => "春",
            "summer" => "夏",
            "fall" => "秋",
            "winter" => "冬",
            _ => season
        };

        public static string TimeString(int time)
        {
            int h = time / 100 % 24, m = time % 100;
            return $"{h:00}:{m:00}" + (time >= 2400 ? "(次日)" : "");
        }

        public static string Stamp() => $"第{Game1.year}年{Season}{Game1.dayOfMonth}日 {TimeString(Game1.timeOfDay)}";

        public static string DirName(int dir) => dir switch
        {
            0 => "上",
            1 => "右",
            2 => "下",
            3 => "左",
            _ => "?"
        };

        /// <summary>解析方向：up/right/down/left、上/右/下/左、北/东/南/西 或 0-3，无法识别返回 -1。</summary>
        public static int ParseDir(string s) => (s ?? "").Trim().ToLowerInvariant() switch
        {
            "0" or "up" or "north" or "上" or "北" => 0,
            "1" or "right" or "east" or "右" or "东" => 1,
            "2" or "down" or "south" or "下" or "南" => 2,
            "3" or "left" or "west" or "左" or "西" => 3,
            _ => -1
        };

        public static string ItemLabel(Item item)
        {
            if (item == null)
                return "空";
            string s = item.DisplayName;
            if (item is WateringCan can)
                s += $"（水 {can.WaterLeft}/{can.waterCanMax}）";
            else if (item is Tool tool && tool.UpgradeLevel > 0 && item is not MeleeWeapon)
                s += $"（等级{tool.UpgradeLevel}）";
            if (item.Stack > 1)
                s += $" x{item.Stack}";
            if (item.Quality > 0)
                s += item.Quality switch { 1 => " 银星", 2 => " 金星", 4 => " 铱星", _ => "" };
            if (item is SObject o && !(item is Tool))
            {
                if (o.Edibility > 0 && o.Edibility != -300)
                    s += $" [可食用 +{o.staminaRecoveredOnConsumption()}体力]";
                if (o.Category == SObject.SeedsCategory)
                    s += " [种子]";
            }
            return s;
        }

        /// <summary>去掉状态里显示的品质后缀（“土豆 银星”→“土豆”），模型常把整段名字原样抄回来。</summary>
        public static string StripQuality(string name) =>
            System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"\s*(银星|金星|铱星)\+?$", "");

        /// <summary>按序号或名称在背包里找物品，返回背包下标，找不到返回 -1。同名不同品质时优先品质和名字都对上的。</summary>
        public static int FindItemIndex(string spec, Func<Item, bool> filter = null)
        {
            if (string.IsNullOrWhiteSpace(spec))
                return -1;
            spec = spec.Trim();
            var items = Game1.player.Items;
            int quality = spec.EndsWith("铱星") ? 4 : spec.EndsWith("金星") || spec.EndsWith("金星+") ? 2 : spec.EndsWith("银星") || spec.EndsWith("银星+") ? 1 : -1;
            string bare = StripQuality(spec);
            if (quality >= 0 && bare != spec)
            {
                for (int i = 0; i < items.Count; i++)
                    if (items[i] is { } q && q.Quality == quality && (filter == null || filter(q))
                        && (q.DisplayName.Equals(bare, StringComparison.OrdinalIgnoreCase) || q.Name.Equals(bare, StringComparison.OrdinalIgnoreCase)))
                        return i;
                spec = bare;
            }
            if (int.TryParse(spec, out int idx))
            {
                if (idx >= 0 && idx < items.Count && items[idx] != null && (filter == null || filter(items[idx])))
                    return idx;
                return -1;
            }

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    if (it == null || (filter != null && !filter(it)))
                        continue;
                    bool match = pass == 0
                        ? it.DisplayName.Equals(spec, StringComparison.OrdinalIgnoreCase) || it.Name.Equals(spec, StringComparison.OrdinalIgnoreCase)
                        : it.DisplayName.Contains(spec, StringComparison.OrdinalIgnoreCase) || it.Name.Contains(spec, StringComparison.OrdinalIgnoreCase);
                    if (match)
                        return i;
                }
            }
            return -1;
        }

        private static readonly Dictionary<string, Func<Item, bool>> ToolAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["axe"] = i => i is Axe,
            ["斧头"] = i => i is Axe,
            ["斧"] = i => i is Axe,
            ["hoe"] = i => i is Hoe,
            ["锄头"] = i => i is Hoe,
            ["锄"] = i => i is Hoe,
            ["pickaxe"] = i => i is Pickaxe,
            ["镐子"] = i => i is Pickaxe,
            ["镐"] = i => i is Pickaxe,
            ["wateringcan"] = i => i is WateringCan,
            ["watering_can"] = i => i is WateringCan,
            ["watering can"] = i => i is WateringCan,
            ["水壶"] = i => i is WateringCan,
            ["洒水壶"] = i => i is WateringCan,
            ["scythe"] = i => i is MeleeWeapon w && w.isScythe(),
            ["镰刀"] = i => i is MeleeWeapon w && w.isScythe(),
            ["weapon"] = i => i is MeleeWeapon w && !w.isScythe(),
            ["sword"] = i => i is MeleeWeapon w && !w.isScythe(),
            ["武器"] = i => i is MeleeWeapon w && !w.isScythe(),
            ["剑"] = i => i is MeleeWeapon w && !w.isScythe(),
            ["fishingrod"] = i => i is FishingRod,
            ["rod"] = i => i is FishingRod,
            ["鱼竿"] = i => i is FishingRod,
        };

        public static int FindToolIndex(string spec)
        {
            if (spec != null && ToolAliases.TryGetValue(spec.Trim().Replace("-", "_"), out var pred))
            {
                var items = Game1.player.Items;
                for (int i = 0; i < items.Count; i++)
                    if (items[i] != null && pred(items[i]))
                        return i;
                return -1;
            }
            return FindItemIndex(spec, i => i is Tool);
        }

        public static int FindTool<T>(Func<T, bool> pred = null) where T : Tool
        {
            var items = Game1.player.Items;
            for (int i = 0; i < items.Count; i++)
                if (items[i] is T t && (pred == null || pred(t)))
                    return i;
            return -1;
        }

        public static string ObjectName(string qualifiedOrUnqualifiedId)
        {
            if (string.IsNullOrEmpty(qualifiedOrUnqualifiedId))
                return "?";
            string id = qualifiedOrUnqualifiedId.StartsWith("(") ? qualifiedOrUnqualifiedId : "(O)" + qualifiedOrUnqualifiedId;
            return ItemRegistry.GetDataOrErrorItem(id).DisplayName;
        }

        /// <summary>把坐标列表压缩成短文字。</summary>
        public static string Coords(IEnumerable<Point> points, int max = 40)
        {
            var list = points.ToList();
            string s = string.Join(" ", list.Take(max).Select(p => $"({p.X},{p.Y})"));
            if (list.Count > max)
                s += $" …等共{list.Count}处";
            return s;
        }

        public static int Manhattan(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        /// <summary>按“最近邻”顺序排列要访问的格子，减少来回跑。</summary>
        public static List<Point> NearestOrder(Point start, IEnumerable<Point> points)
        {
            var remaining = points.ToList();
            var result = new List<Point>();
            var cur = start;
            while (remaining.Count > 0)
            {
                int bi = 0, bd = int.MaxValue;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int d = Manhattan(cur, remaining[i]);
                    if (d < bd)
                    {
                        bd = d;
                        bi = i;
                    }
                }
                cur = remaining[bi];
                result.Add(cur);
                remaining.RemoveAt(bi);
            }
            return result;
        }
    }
}
