using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.FarmAnimals;
using StardewValley.Menus;
using StardewValley.Objects;
using SObject = StardewValley.Object;
using xLocation = xTile.Dimensions.Location;

namespace BlindWhaleFarmer
{
    internal sealed partial class GameActions
    {
        /// <summary>农场上所有住着动物的建筑。</summary>
        public static List<(Building building, AnimalHouse house)> AnimalBuildings()
        {
            var list = new List<(Building, AnimalHouse)>();
            foreach (var b in Game1.getFarm().buildings)
            {
                if (b.isUnderConstruction() || b.GetIndoors() is not AnimalHouse house)
                    continue;
                if (house.animalsThatLiveHere.Count > 0)
                    list.Add((b, house));
            }
            return list;
        }

        /// <summary>让动物原地停一会儿，方便走过去。</summary>
        private static void FreezeAnimals(GameLocation loc)
        {
            foreach (var a in loc.animals.Values)
            {
                a.Halt();
                a.pauseTimer = Math.Max(a.pauseTimer, 4000);
            }
        }

        /// <summary>
        /// 照顾动物：喂干草、捡蛋/羊毛等地上的产物、清空自动采集器、摸摸每只动物、用挤奶桶/剪刀收产物。
        /// 会自动走进每个鸡舍/畜棚，最后回到农场上处理在外面的动物。
        /// </summary>
        public IEnumerable<object> CareAnimals(string door)
        {
            if (Blocked(out string why))
            {
                yield return ActionResult.Fail(why);
                yield break;
            }
            var buildings = AnimalBuildings();
            if (buildings.Count == 0 && Game1.getFarm().animals.Length == 0)
            {
                yield return ActionResult.Fail("你还没有动物（先在 Robin 那里建鸡舍/畜棚，再去 Marnie 的牧场 AnimalShop 买动物）");
                yield break;
            }

            var report = new List<string>();
            var stats = new CareStats();
            var o = new Outcome();

            // 按距离：如果已经在某个动物屋里，先处理它
            var ordered = buildings.OrderBy(b => b.house == Loc ? 0 : 1).ToList();
            foreach (var (building, house) in ordered)
            {
                string name = house.NameOrUniqueName;
                if (Loc != house)
                {
                    foreach (var s in TravelInner(name, o))
                        yield return s;
                    if (!o.Ok)
                    {
                        report.Add($"{building.buildingType.Value}：进不去（{o.Message}）");
                        continue;
                    }
                }
                foreach (var s in CareInLocation(house, stats))
                    yield return s;

                // 喂干草：和手动从饲料槽旁取干草放进食槽效果一样，用的是筒仓里的干草
                int before = CountHay(house);
                house.feedAllAnimals();
                // 筒仓不够（或者没有筒仓、干草是在 Marnie 那里买进背包的）：用背包里的干草补满食槽
                FeedFromBackpack(house);
                int fed = CountHay(house) - before;
                if (fed > 0)
                    stats.Hay += fed;
                if (CountHay(house) < house.animalsThatLiveHere.Count)
                    stats.NoHay = true;
            }

            // 农场外面的动物 + 动物门
            bool outsideAnimals = Game1.getFarm().animals.Length > 0;
            bool doorChange = !string.IsNullOrEmpty(door) && door is "open" or "close" or "打开" or "关闭";
            if (outsideAnimals || doorChange)
            {
                if (Loc != Game1.getFarm())
                {
                    foreach (var s in TravelInner("Farm", o))
                        yield return s;
                }
                if (Loc == Game1.getFarm())
                {
                    if (outsideAnimals)
                    {
                        foreach (var s in CareInLocation(Loc, stats))
                            yield return s;
                    }
                    if (doorChange)
                    {
                        bool open = door is "open" or "打开";
                        foreach (var (building, _) in buildings)
                        {
                            if (building.animalDoorOpen.Value != open && building.animalDoor.Value.X >= 0)
                            {
                                building.ToggleAnimalDoor(Player);
                                stats.Doors++;
                            }
                        }
                    }
                }
            }

            report.Insert(0, stats.Describe(door));
            yield return ActionResult.Success(string.Join("；", report));
        }

        private sealed class CareStats
        {
            public int Petted, Harvested, Picked, Hay, Doors, TooLate;
            public bool NoHay;
            public readonly List<string> Problems = new();

            public string Describe(string door)
            {
                var parts = new List<string> { $"摸了 {Petted} 只", $"挤奶/剪毛 {Harvested} 次", $"捡起产物 {Picked} 个", $"放干草 {Hay} 份" };
                if (Doors > 0)
                    parts.Add($"{(door is "open" or "打开" ? "打开" : "关上")}了 {Doors} 扇动物门");
                if (TooLate > 0)
                    parts.Add($"{TooLate} 只已经睡了（19:00 后摸不了）");
                if (NoHay)
                    parts.Add("筒仓和背包里的干草都不够，食槽没喂满（用镰刀割草可以存进筒仓，或在 Marnie 那里买干草）");
                parts.AddRange(Problems.Distinct().Take(4));
                return string.Join("，", parts);
            }
        }

        private static int CountHay(AnimalHouse house) =>
            house.objects.Values.Count(obj => obj.QualifiedItemId == "(O)178");

        /// <summary>食槽的格子（Back 层有 Trough 属性），从左上往右下。</summary>
        internal static List<Point> TroughTiles(GameLocation house)
        {
            var list = new List<Point>();
            var back = house.map?.GetLayer("Back");
            if (back == null)
                return list;
            for (int y = 0; y < back.LayerHeight; y++)
                for (int x = 0; x < back.LayerWidth; x++)
                    if (house.doesTileHaveProperty(x, y, "Trough", "Back") != null)
                        list.Add(new Point(x, y));
            return list;
        }

        /// <summary>
        /// 把背包里的干草放进空食槽，最多放到每只动物一份（和 AnimalHouse.checkAction 手拿干草点食槽一样：
        /// 食槽格上放一个干草物品）。
        /// </summary>
        private static void FeedFromBackpack(AnimalHouse house)
        {
            int need = house.animalsThatLiveHere.Count - CountHay(house);
            foreach (var tile in TroughTiles(house))
            {
                if (need <= 0)
                    return;
                var key = new Vector2(tile.X, tile.Y);
                if (house.objects.ContainsKey(key))
                    continue;
                int idx = Player.Items.IndexOf(Player.Items.FirstOrDefault(i => i?.QualifiedItemId == "(O)178"));
                if (idx < 0 || Player.Items[idx] is not SObject hay)
                    return;
                house.objects.Add(key, (SObject)hay.getOne());
                Player.Items[idx] = hay.ConsumeStack(1);
                need--;
            }
        }

        private IEnumerable<object> CareInLocation(GameLocation loc, CareStats stats)
        {
            var o = new Outcome();

            // 1) 地上的蛋、羊毛、鸭毛等（会自动生成在地上的物品），以及自动采集器。
            //    在农场上只捡猪拱出来的松露，其他采集品不在这里满地图跑着捡
            bool inHouse = loc is AnimalHouse;
            var pickups = loc.objects.Pairs
                .Where(p => (p.Value.IsSpawnedObject && (inHouse || p.Value.QualifiedItemId == "(O)430"))
                    || (p.Value.QualifiedItemId == "(BC)165" && p.Value.heldObject.Value is Chest c && c.Items.Any(i => i != null)))
                .Select(p => new Point((int)p.Key.X, (int)p.Key.Y))
                .ToList();
            foreach (var tile in Util.NearestOrder(Player.TilePoint, pickups))
            {
                if (!loc.objects.ContainsKey(new Vector2(tile.X, tile.Y)))
                    continue;
                if (Player.freeSpotsInInventory() == 0)
                {
                    stats.Problems.Add("背包满了，有东西没捡");
                    break;
                }
                FreezeAnimals(loc);
                foreach (var s in GoAdjacent(tile, o))
                    yield return s;
                if (!o.Ok)
                    continue;
                bool wasThere = loc.objects.ContainsKey(new Vector2(tile.X, tile.Y));
                loc.checkAction(new xLocation(tile.X, tile.Y), Game1.viewport, Player);
                foreach (var s in WaitTicks(6))
                    yield return s;
                if (Game1.activeClickableMenu is ItemGrabMenu grab)
                {
                    int count = grab.ItemsToGrabMenu?.actualInventory.Count(i => i != null) ?? 0;
                    GrabAll(grab);
                    stats.Picked += count;
                }
                else if (wasThere && !loc.objects.ContainsKey(new Vector2(tile.X, tile.Y)))
                    stats.Picked++;
                foreach (var s in WaitPlayerFree())
                    yield return s;
            }

            // 2) 每只动物：先收产物（挤奶/剪毛），再摸
            foreach (var animal in loc.animals.Values.OrderBy(a => Util.Manhattan(a.TilePoint, Player.TilePoint)).ToList())
            {
                bool needPet = !animal.wasPet.Value;
                int toolIdx = -1;
                if (animal.isAdult() && animal.currentProduce.Value != null && animal.GetHarvestType() == FarmAnimalHarvestType.HarvestWithTool)
                {
                    toolIdx = Util.FindTool<Tool>(t => animal.CanGetProduceWithTool(t));
                    if (toolIdx < 0)
                        stats.Problems.Add($"{animal.displayName}可以收{(animal.GetAnimalData()?.HarvestTool == "Shears" ? "羊毛，需要剪刀" : "奶，需要挤奶桶")}（Marnie 那里买）");
                }
                if (!needPet && toolIdx < 0)
                    continue;
                if (needPet && Game1.timeOfDay >= 1900 && toolIdx < 0)
                {
                    stats.TooLate++;
                    continue;
                }

                FreezeAnimals(loc);
                var target = animal.TilePoint;
                foreach (var s in GoAdjacent(target, o))
                    yield return s;
                if (!o.Ok)
                {
                    stats.Problems.Add($"{animal.displayName}走不过去");
                    continue;
                }
                // 走路时动物可能挪了一点，重新瞄准
                target = animal.TilePoint;
                Face(target);

                if (toolIdx >= 0)
                {
                    if (Player.freeSpotsInInventory() == 0)
                        stats.Problems.Add("背包满了，收不了奶/毛");
                    else
                    {
                        animal.pauseTimer = Math.Max(animal.pauseTimer, 3000);
                        foreach (var s in UseToolOnce(toolIdx, target, o))
                            yield return s;
                        foreach (var s in WaitPlayerFree())
                            yield return s;
                        if (animal.currentProduce.Value == null)
                            stats.Harvested++;
                        else
                            stats.Problems.Add($"{animal.displayName}没收成功");
                    }
                }

                if (needPet && Game1.timeOfDay < 1900 && Game1.activeClickableMenu == null)
                {
                    animal.pet(Player);
                    foreach (var s in WaitTicks(10))
                        yield return s;
                    if (animal.wasPet.Value)
                        stats.Petted++;
                    // 摸的时候有时会弹出动物信息框，关掉
                    if (Game1.activeClickableMenu is AnimalQueryMenu || Game1.activeClickableMenu is DialogueBox)
                        Game1.exitActiveMenu();
                }
                else if (needPet)
                    stats.TooLate++;
            }
        }
    }
}
