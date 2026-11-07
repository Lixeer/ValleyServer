using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace BlindWhaleFarmer
{
    internal sealed partial class GameActions
    {
        // ───────────────────────── 整理背包 ─────────────────────────

        /// <summary>
        /// 整理背包时要留在身上的：每类工具只留最好的一把（鱼竿按 RodScore、武器按伤害、其他工具按升级等级），
        /// 吃的（料理；不够两样时补上回血最多的普通食物，鱼排在后面）、种子、肥料、鱼饵钓具、炸弹、任务物品、
        /// 要摆放的机器/洒水器/箱子，以及玩家点名要留的。其余都存进箱子。
        /// </summary>
        private static HashSet<Item> ItemsToKeep(Func<Item, bool> keepAlso)
        {
            var keep = new HashSet<Item>();
            var items = Player.Items.Where(i => i != null).ToList();
            void BestOf<T>(Func<T, bool> where, Func<T, int> score) where T : Item
            {
                var best = items.OfType<T>().Where(where).OrderByDescending(score).FirstOrDefault();
                if (best != null)
                    keep.Add(best);
            }
            BestOf<FishingRod>(_ => true, RodScore);
            BestOf<MeleeWeapon>(w => !w.isScythe(), w => w.minDamage.Value + w.maxDamage.Value);
            BestOf<MeleeWeapon>(w => w.isScythe(), w => w.UpgradeLevel);
            BestOf<Pickaxe>(_ => true, t => t.UpgradeLevel);
            BestOf<Axe>(_ => true, t => t.UpgradeLevel);
            BestOf<Hoe>(_ => true, t => t.UpgradeLevel);
            BestOf<WateringCan>(_ => true, t => t.UpgradeLevel);
            BestOf<Pan>(_ => true, t => t.UpgradeLevel);
            BestOf<MilkPail>(_ => true, _ => 0);
            BestOf<Shears>(_ => true, _ => 0);
            foreach (var i in items)
            {
                if (i is Tool)
                    continue;
                bool usefulOnHand = i is SObject o && (
                    o.Category is SObject.CookingCategory or SObject.SeedsCategory or SObject.fertilizerCategory
                        or SObject.baitCategory or SObject.tackleCategory
                    || o.bigCraftable.Value                    // 熔炉、洒水器以外的机器、箱子：要摆的
                    || o.IsSprinkler()
                    || o.Name.Contains("Bomb")
                    || o.questItem.Value
                    || o.QualifiedItemId is "(O)458" or "(O)460");   // 花束和美人鱼吊坠：买来就是要送的
                if (usefulOnHand || i is Ring or Boots || keepAlso(i))
                    keep.Add(i);
            }
            // 没带够回血的就下矿，打到一半只能撤退
            bool Heals(Item i) => i is SObject f && f.Edibility > 0 && f.healthRecoveredOnConsumption() > 0;
            var extraFood = items.Where(i => Heals(i) && !keep.Contains(i))
                .OrderBy(i => ((SObject)i).Category == SObject.FishCategory ? 1 : 0)
                .ThenByDescending(i => ((SObject)i).healthRecoveredOnConsumption())
                .Take(Math.Max(0, 2 - keep.Count(Heals)));
            keep.UnionWith(extraFood.ToList());
            return keep;
        }

        /// <summary>
        /// 整理背包：把用不上的东西存进农场（或当前地点）的箱子，一个箱子满了换下一个。
        /// 低级的鱼竿、多余的武器等重复工具也存起来，每类只留最好的一把。
        /// </summary>
        public IEnumerable<object> TidyBackpack(string keep)
        {
            if (Blocked(out string why))
            {
                yield return ActionResult.Fail(why);
                yield break;
            }
            var keepFilter = string.IsNullOrWhiteSpace(keep) ? (_ => false) : ItemFilter(keep);
            var keepSet = ItemsToKeep(keepFilter);
            var toStore = Player.Items.Where(i => i != null && !keepSet.Contains(i)).ToList();
            if (toStore.Count == 0)
            {
                yield return ActionResult.Success("背包已经很整齐了，没有需要存起来的东西");
                yield break;
            }

            // 这里没有箱子就回农场
            var o = new Outcome();
            if (!PlayerChests(Loc).Any())
            {
                foreach (var s in TravelInner("Farm", o))
                    yield return s;
                if (!o.Ok)
                {
                    yield return ActionResult.Fail("回农场失败：" + o.Message);
                    yield break;
                }
            }
            var chests = PlayerChests(Loc).OrderBy(c => Util.Manhattan(c.tile, Player.TilePoint)).ToList();
            if (chests.Count == 0)
            {
                yield return ActionResult.Fail("农场上没有箱子。先 craft Chest（50 木材），再 place_item 放在农场上");
                yield break;
            }

            var stored = new List<string>();
            foreach (var (tile, chest) in chests)
            {
                var pending = toStore.Where(i => Player.Items.Contains(i)).ToList();
                if (pending.Count == 0)
                    break;
                // 先看这个箱子还装不装得下（有空格，或者有同类可以叠）
                if (chest.GetItemsForPlayer().Count(i => i != null) >= chest.GetActualCapacity()
                    && !pending.Any(p => chest.GetItemsForPlayer().Any(c => c != null && c.canStackWith(p) && c.Stack < c.maximumStackSize())))
                    continue;
                foreach (var s in GoAdjacent(tile, o))
                    yield return s;
                if (!o.Ok)
                {
                    o.Succeed();
                    continue;
                }
                foreach (var item in pending)
                {
                    int before = item.Stack;
                    string name = item.DisplayName;
                    var left = chest.addItem(item);
                    if (left == null)
                    {
                        Player.removeItemFromInventory(item);
                        stored.Add(before > 1 ? $"{name}x{before}" : name);
                    }
                    else if (left.Stack < before)
                        stored.Add($"{name}x{before - left.Stack}");
                }
                chest.clearNulls();
            }

            var leftover = toStore.Where(i => Player.Items.Contains(i)).Select(i => i.DisplayName).ToList();
            var kept = Player.Items.Where(i => i != null).Select(i => i.DisplayName).ToList();
            string msg = stored.Count > 0 ? $"存起来了：{string.Join("、", stored)}" : "一样也没存进去";
            msg += $"。身上留着：{string.Join("、", kept)}（空位 {Player.freeSpotsInInventory()} 个）";
            if (leftover.Count > 0)
                msg += $"。箱子都满了，还剩 {string.Join("、", leftover)} 没地方放：craft Chest（50 木材）再放一个箱子";
            yield return stored.Count > 0 ? ActionResult.Success(msg) : ActionResult.Fail(msg);
        }

        /// <summary>某地点里玩家自己放的箱子（不含开局礼物箱）。</summary>
        private static IEnumerable<(Point tile, Chest chest)> PlayerChests(GameLocation loc) =>
            loc.objects.Pairs
                .Where(p => p.Value is Chest c && c.playerChest.Value && !c.giftbox.Value)
                .Select(p => (new Point((int)p.Key.X, (int)p.Key.Y), (Chest)p.Value));
    }
}
