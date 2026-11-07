using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

namespace BlindWhaleFarmer
{
    internal sealed partial class GameActions
    {
        // ───────────────────────── 箱子存取 ─────────────────────────

        /// <summary>
        /// 当前地点里玩家放的箱子：给了坐标就用那一格，否则用最近的一个。
        /// 开局礼物箱（giftbox）不算，它打开就消失。
        /// </summary>
        private static Chest FindChest(int? x, int? y, out Point tile, out string error)
        {
            tile = default;
            error = null;
            if (x.HasValue && y.HasValue)
            {
                tile = new Point(x.Value, y.Value);
                if (Loc.objects.TryGetValue(new Vector2(x.Value, y.Value), out var o) && o is Chest c && c.playerChest.Value && !c.giftbox.Value)
                    return c;
                error = $"({x},{y}) 不是箱子";
                return null;
            }
            var me = Player.TilePoint;
            var nearest = Loc.objects.Pairs
                .Where(p => p.Value is Chest c && c.playerChest.Value && !c.giftbox.Value)
                .OrderBy(p => Math.Abs(p.Key.X - me.X) + Math.Abs(p.Key.Y - me.Y))
                .FirstOrDefault();
            if (nearest.Value == null)
            {
                error = "这里没有箱子。先 craft Chest（50 木材），再 place_item 放在农场上（比如出货箱旁边）";
                return null;
            }
            tile = new Point((int)nearest.Key.X, (int)nearest.Key.Y);
            return (Chest)nearest.Value;
        }

        /// <summary>items 为空或 "all" 表示全部；否则用逗号/顿号分隔的名字（包含匹配）。</summary>
        private static Func<Item, bool> ItemFilter(string items)
        {
            if (string.IsNullOrWhiteSpace(items) || items.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) || items.Trim() == "全部")
                return _ => true;
            var names = items.Split(new[] { ',', '，', '、', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Util.StripQuality).ToArray();
            return i => names.Any(n => i.DisplayName.Contains(n, StringComparison.OrdinalIgnoreCase) || i.Name.Contains(n, StringComparison.OrdinalIgnoreCase));
        }

        private static string ChestSummary(Chest chest)
        {
            var items = chest.GetItemsForPlayer().Where(i => i != null).ToList();
            return items.Count == 0
                ? "箱子是空的"
                : $"箱子里（{items.Count}/{chest.GetActualCapacity()} 格）：{string.Join("、", items.Select(i => $"{i.DisplayName}x{i.Stack}"))}";
        }

        /// <summary>把背包里的东西存进箱子（工具和武器只存点了全名的，比如“训练用鱼竿”）。和箱子界面里点背包物品一样：chest.addItem，全放进去才从背包移除。</summary>
        public IEnumerable<object> StoreItems(string items, int? x, int? y)
        {
            var chest = FindChest(x, y, out var tile, out string error);
            if (chest == null)
            {
                yield return ActionResult.Fail(error);
                yield break;
            }
            var o = new Outcome();
            foreach (var s in GoAdjacent(tile, o))
                yield return s;
            if (!o.Ok)
            {
                yield return ActionResult.Fail(o.Message);
                yield break;
            }
            var filter = ItemFilter(items);
            // 工具和武器只存点了全名的（“剑”这种模糊的名字会把手上最好的武器也存掉）
            var toolNames = string.IsNullOrWhiteSpace(items) ? new HashSet<string>()
                : items.Split(new[] { ',', '，', '、', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool Wanted(Item i) => i is Tool ? toolNames.Contains(i.DisplayName) || toolNames.Contains(i.Name) : filter(i);
            var stored = new List<string>();
            bool full = false;
            foreach (var item in Player.Items.Where(i => i != null && Wanted(i)).ToList())
            {
                int before = item.Stack;
                string name = item.DisplayName;
                var left = chest.addItem(item);
                if (left == null)
                {
                    Player.removeItemFromInventory(item);
                    stored.Add($"{name}x{before}");
                }
                else
                {
                    full = true;
                    if (left.Stack < before)
                        stored.Add($"{name}x{before - left.Stack}");
                }
            }
            chest.clearNulls();
            if (stored.Count == 0)
            {
                yield return ActionResult.Fail((full ? "箱子满了，" : "背包里没有符合的东西可存（工具要写全名才存），") + ChestSummary(chest));
                yield break;
            }
            yield return ActionResult.Success($"存进 ({tile.X},{tile.Y}) 的箱子：{string.Join("、", stored)}{(full ? "；箱子满了，剩下的还在背包里" : "")}。{ChestSummary(chest)}");
        }

        /// <summary>从箱子取东西回背包。和箱子界面里点箱子物品一样：先确认背包装得下再从箱子移除。</summary>
        public IEnumerable<object> TakeItems(string items, int? x, int? y)
        {
            var chest = FindChest(x, y, out var tile, out string error);
            if (chest == null)
            {
                yield return ActionResult.Fail(error);
                yield break;
            }
            var o = new Outcome();
            foreach (var s in GoAdjacent(tile, o))
                yield return s;
            if (!o.Ok)
            {
                yield return ActionResult.Fail(o.Message);
                yield break;
            }
            var filter = ItemFilter(items);
            var taken = new List<string>();
            bool bagFull = false;
            foreach (var item in chest.GetItemsForPlayer().Where(i => i != null && filter(i)).ToList())
            {
                if (!Player.couldInventoryAcceptThisItem(item))
                {
                    bagFull = true;
                    continue;
                }
                chest.GetItemsForPlayer().Remove(item);
                taken.Add($"{item.DisplayName}x{item.Stack}");
                var left = Player.addItemToInventory(item);
                if (left != null)
                    chest.addItem(left);
            }
            chest.clearNulls();
            if (taken.Count == 0)
            {
                yield return ActionResult.Fail((bagFull ? "背包满了，" : "箱子里没有符合的东西，") + ChestSummary(chest));
                yield break;
            }
            yield return ActionResult.Success($"从箱子取出：{string.Join("、", taken)}{(bagFull ? "；背包满了，还有的没取" : "")}。{ChestSummary(chest)}");
        }
    }
}
