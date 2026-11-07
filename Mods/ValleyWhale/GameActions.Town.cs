using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Extensions;
using StardewValley.GameData.Buildings;
using StardewValley.Menus;
using StardewValley.TerrainFeatures;
using StardewValley.TokenizableStrings;
using StardewValley.Tools;
using SObject = StardewValley.Object;
using xLocation = xTile.Dimensions.Location;

namespace BlindWhaleFarmer
{
    /// <summary>镇上的服务：工具升级（Clint）、告示板任务、买动物（Marnie）、建造和升级房子（Robin）。</summary>
    internal sealed partial class GameActions
    {
        /// <summary>去某个店并确认店主在场。</summary>
        private IEnumerable<object> GoToShopkeeper(string location, string npc, string counterAction, Outcome o)
        {
            if (!Loc.NameOrUniqueName.Equals(location, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var s in TravelInner(location, o))
                    yield return s;
                if (!o.Ok)
                    yield break;
            }
            if (!Loc.characters.Any(c => c.Name == npc))
            {
                o.Fail($"{npc} 现在不在店里（看营业时间：Clint 9:00-16:00，Marnie 9:00-16:00 周一周二休息，Robin 9:00-17:00 周二休息）");
                yield break;
            }
            var counter = counterAction != null ? FindTileAction(Loc, counterAction) : null;
            if (counter.HasValue)
            {
                foreach (var s in GoAdjacent(counter.Value, o))
                    yield return s;
            }
            o.Succeed();
        }

        private IEnumerable<object> CloseDialogues()
        {
            for (int i = 0; i < 10 && Game1.activeClickableMenu is DialogueBox; i++)
            {
                foreach (var s in Dialogue(null))
                    if (s is not ActionResult)
                        yield return s;
            }
        }

        // ───────────────────────── 砸晶球 ─────────────────────────

        /// <summary>
        /// 找 Clint 砸晶球：和玩家在 GeodeMenu 里拿起晶球、放到铁砧上一样（每个 25g，铁砧的动画结束后东西进背包）。
        /// </summary>
        public IEnumerable<object> CrackGeodes(string itemSpec, int? count)
        {
            var filter = ItemFilter(itemSpec);
            var geodes = Player.Items.Where(i => i != null && Utility.IsGeode(i) && filter(i)).ToList();
            if (geodes.Count == 0)
            {
                yield return ActionResult.Fail("背包里没有" + (string.IsNullOrWhiteSpace(itemSpec) ? "晶球" : $"符合“{itemSpec}”的晶球"));
                yield break;
            }

            var o = new Outcome();
            if (Game1.activeClickableMenu is not GeodeMenu)
            {
                if (Blocked(out string why))
                {
                    yield return ActionResult.Fail(why);
                    yield break;
                }
                foreach (var s in GoToShopkeeper("Blacksmith", "Clint", "Blacksmith", o))
                    yield return s;
                if (!o.Ok)
                {
                    yield return ActionResult.Fail(o.Message);
                    yield break;
                }
                Game1.activeClickableMenu = new GeodeMenu();
            }
            var menu = (GeodeMenu)Game1.activeClickableMenu;

            int left = Math.Max(count ?? int.MaxValue, 1), cost = 0;
            var found = new List<string>();
            string stop = null;
            foreach (var geode in geodes)
            {
                while (left > 0 && stop == null && Player.Items.Contains(geode) && geode.Stack > 0)
                {
                    if (Player.Money < 25)
                    {
                        stop = "钱不够了（每个 25g）";
                        break;
                    }
                    // 拿起来放到铁砧上。剩下的马上放回原来的格子，开出来的东西才不会占了它
                    int slot = Player.Items.IndexOf(geode);
                    Player.Items[slot] = null;
                    menu.heldItem = geode;
                    var spot = menu.geodeSpot.bounds.Center;
                    menu.receiveLeftClick(spot.X, spot.Y);
                    bool started = menu.geodeAnimationTimer > 0;
                    Player.Items[slot] = menu.heldItem;
                    menu.heldItem = null;
                    if (!started)
                    {
                        stop = "背包满了，开出来的东西没地方放，先空出一格";
                        break;
                    }
                    cost += 25;
                    left--;

                    Item treasure = null;
                    for (int t = 0; t < 400 && menu.geodeAnimationTimer > 0; t++)
                    {
                        treasure = menu.geodeTreasure ?? treasure;
                        yield return null;
                    }
                    if (treasure != null)
                        found.Add(treasure.Stack > 1 ? $"{treasure.DisplayName}x{treasure.Stack}" : treasure.DisplayName);
                }
            }

            if (Game1.activeClickableMenu == menu)
            {
                if (menu.readyToClose())
                    menu.exitThisMenu();
                else
                {
                    menu.emergencyShutDown();
                    Game1.exitActiveMenu();
                }
            }
            for (int i = 0; i < 120 && Game1.activeClickableMenu == menu; i++)
                yield return null;

            if (cost == 0)
            {
                yield return ActionResult.Fail(stop ?? "一个也没砸开");
                yield break;
            }
            string msg = $"砸开 {cost / 25} 个晶球，花了 {cost}g，现在有 {Player.Money}g，开出来：{(found.Count > 0 ? string.Join("、", found) : "（没看到结果，查一下背包）")}，都在背包里";
            int remaining = Player.Items.Where(i => i != null && Utility.IsGeode(i)).Sum(i => i.Stack);
            if (remaining > 0)
                msg += $"；背包里还有 {remaining} 个晶球";
            if (stop != null)
                msg += "；停下原因：" + stop;
            yield return ActionResult.Success(msg);
        }

        // ───────────────────────── 工具升级 ─────────────────────────

        public static string ToolUpgradeStatus()
        {
            var t = Game1.player.toolBeingUpgraded.Value;
            if (t == null)
                return null;
            int days = Game1.player.daysLeftForToolUpgrade.Value;
            return days > 0 ? $"{t.DisplayName} 正在铁匠铺升级，还要 {days} 天" : $"{t.DisplayName} 已经升级好了，去铁匠铺（Blacksmith）用 upgrade_tool 领取";
        }

        public IEnumerable<object> UpgradeTool(string toolSpec)
        {
            var o = new Outcome();
            var pending = Player.toolBeingUpgraded.Value;
            if (pending != null && Player.daysLeftForToolUpgrade.Value > 0)
            {
                yield return ActionResult.Fail(ToolUpgradeStatus() + "，同一时间只能升级一件");
                yield break;
            }

            foreach (var s in GoToShopkeeper("Blacksmith", "Clint", "Blacksmith", o))
                yield return s;
            if (!o.Ok)
            {
                yield return ActionResult.Fail(o.Message);
                yield break;
            }

            // 领取升级好的工具：和走到柜台跟 Clint 说话一样
            if (pending != null)
            {
                if (Player.freeSpotsInInventory() == 0)
                {
                    yield return ActionResult.Fail("背包满了，空出一格再来领工具");
                    yield break;
                }
                var counter = FindTileAction(Loc, "Blacksmith");
                if (counter.HasValue)
                    Loc.checkAction(new xLocation(counter.Value.X, counter.Value.Y), Game1.viewport, Player);
                for (int t = 0; t < 120 && Player.toolBeingUpgraded.Value != null; t++)
                    yield return null;
                foreach (var s in WaitTicks(60))
                    yield return s;
                foreach (var s in CloseDialogues())
                    yield return s;
                yield return Player.toolBeingUpgraded.Value == null
                    ? ActionResult.Success($"领到了升级后的 {pending.DisplayName}")
                    : ActionResult.Fail("没领到工具（Clint 可能不在柜台）");
                yield break;
            }

            int idx = Util.FindToolIndex(toolSpec);
            if (idx < 0 || Player.Items[idx] is not Tool current)
            {
                yield return ActionResult.Fail($"背包里没有“{toolSpec}”（可升级：axe、pickaxe、hoe、watering_can）");
                yield break;
            }
            if (!Utility.TryOpenShopMenu("ClintUpgrade", Loc) || Game1.activeClickableMenu is not ShopMenu shop)
            {
                yield return ActionResult.Fail("Clint 的升级服务没开");
                yield break;
            }
            yield return null;
            var offer = shop.forSale.OfType<Tool>()
                .FirstOrDefault(t => t.GetType() == current.GetType() && t.UpgradeLevel == current.UpgradeLevel + 1 && shop.itemPriceAndStock.ContainsKey(t));
            if (offer == null)
            {
                string list = string.Join("、", shop.forSale.Where(s => shop.itemPriceAndStock.ContainsKey(s)).Select(s =>
                {
                    var info = shop.itemPriceAndStock[s];
                    return $"{s.DisplayName} {info.Price}g + {info.TradeItemCount ?? 1}个{Util.ObjectName(info.TradeItem)}";
                }));
                shop.exitThisMenu(false);
                yield return ActionResult.Fail($"{current.DisplayName} 现在不能升级（已是最高级或者没解锁）。可升级项目：{list}");
                yield break;
            }
            var stock = shop.itemPriceAndStock[offer];
            string cost = $"{stock.Price}g + {stock.TradeItemCount ?? 1}个{Util.ObjectName(stock.TradeItem)}";
            ActionResult buy = null;
            foreach (var s in Buy(shop.forSale.Where(x => shop.itemPriceAndStock.ContainsKey(x)).ToList().IndexOf(offer).ToString(), 1))
            {
                if (s is ActionResult r)
                {
                    buy = r;
                    break;
                }
                yield return s;
            }
            bool started = Player.toolBeingUpgraded.Value != null;
            foreach (var s in CloseDialogues())
                yield return s;
            if (Game1.activeClickableMenu is ShopMenu stillOpen)
                stillOpen.exitThisMenu(false);
            yield return started
                ? ActionResult.Success($"把 {current.DisplayName} 交给 Clint 升级成 {offer.DisplayName}（花费 {cost}），2 天后来铁匠铺用 upgrade_tool 领取。升级期间这件工具不能用！")
                : ActionResult.Fail($"升级失败（需要 {cost}）：{buy?.Message}");
        }

        // ───────────────────────── 告示板任务 ─────────────────────────

        public static string DailyQuestHint()
        {
            var q = Game1.questOfTheDay;
            if (q == null || q.accepted.Value || !Game1.CanAcceptDailyQuest())
                return null;
            return $"镇上告示板（Town，皮埃尔杂货店门口）今天有委托任务：{q.GetName()}（用 quest_board 查看/接受）";
        }

        public IEnumerable<object> QuestBoard(bool accept)
        {
            var o = new Outcome();
            if (!Loc.Name.Equals("Town", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var s in TravelInner("Town", o))
                    yield return s;
                if (!o.Ok)
                {
                    yield return ActionResult.Fail(o.Message);
                    yield break;
                }
            }
            // 每日任务的告示板动作是 "Billboard 3"（另一个 Billboard 是日历）
            Point? board = null;
            int w = Pathfinder.MapWidth(Loc), h = Pathfinder.MapHeight(Loc);
            for (int x = 0; x < w && board == null; x++)
                for (int y = 0; y < h && board == null; y++)
                    if (Loc.doesTileHaveProperty(x, y, "Action", "Buildings") is string a && a.Trim() == "Billboard 3")
                        board = new Point(x, y);
            if (board.HasValue)
            {
                foreach (var s in GoAdjacent(board.Value, o))
                    yield return s;
            }

            var q = Game1.questOfTheDay;
            if (q == null)
            {
                yield return ActionResult.Success("告示板今天没有委托任务");
                yield break;
            }
            string desc = $"委托：{q.GetName()}\n{q.GetDescription()}\n目标：{string.Join("；", q.GetObjectiveDescriptions() ?? new List<string>())}" +
                (q.GetMoneyReward() > 0 ? $"\n报酬：{q.GetMoneyReward()}g" : "");
            if (q.accepted.Value || !Game1.CanAcceptDailyQuest())
            {
                yield return ActionResult.Success("今天的委托已经接过了。\n" + desc);
                yield break;
            }
            if (!accept)
            {
                yield return ActionResult.Success(desc + "\n（要接受就用 quest_board accept=true）");
                yield break;
            }
            // 与告示板界面点“接受任务”按钮的逻辑一致
            q.dailyQuest.Value = true;
            q.dayQuestAccepted.Value = Game1.Date.TotalDays;
            q.accepted.Value = true;
            q.canBeCancelled.Value = true;
            q.daysLeft.Value = 2;
            Player.questLog.Add(q);
            Player.acceptedDailyQuest.Set(newValue: true);
            Game1.playSound("newArtifact");
            yield return ActionResult.Success("接受了委托（2 天内完成）。\n" + desc);
        }

        // ───────────────────────── 买动物 ─────────────────────────

        private long NewMultiplayerId()
        {
            try
            {
                var mp = reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").GetValue();
                return mp.getNewID();
            }
            catch
            {
                return Utility.RandomLong();
            }
        }

        private static string AnimalDisplayName(string key)
        {
            if (Game1.farmAnimalData.TryGetValue(key, out var data) && data.DisplayName != null)
                return TokenParser.ParseText(data.DisplayName);
            return key;
        }

        public IEnumerable<object> BuyAnimal(string typeSpec, string name, int count)
        {
            var o = new Outcome();
            foreach (var s in GoToShopkeeper("AnimalShop", "Marnie", "AnimalShop", o))
                yield return s;
            if (!o.Ok)
            {
                yield return ActionResult.Fail(o.Message);
                yield break;
            }

            var farm = Game1.getFarm();
            var stock = Utility.getPurchaseAnimalStock(farm);
            string Label(SObject s) => $"{AnimalDisplayName(s.Name)}({s.Name}) {s.salePrice()}g" + (s.Type != null ? $"【{(s.Type.Length > 0 ? s.Type : "缺少需要的建筑")}】" : "");
            var pick = string.IsNullOrWhiteSpace(typeSpec) ? null :
                stock.FirstOrDefault(s => s.Name.Equals(typeSpec, StringComparison.OrdinalIgnoreCase) || AnimalDisplayName(s.Name).Equals(typeSpec, StringComparison.OrdinalIgnoreCase))
                ?? stock.FirstOrDefault(s => s.Name.Contains(typeSpec, StringComparison.OrdinalIgnoreCase) || AnimalDisplayName(s.Name).Contains(typeSpec, StringComparison.OrdinalIgnoreCase));
            if (pick == null)
            {
                yield return ActionResult.Fail("Marnie 这里能买的动物：" + string.Join("、", stock.Select(Label)));
                yield break;
            }
            if (pick.Type != null)
            {
                yield return ActionResult.Fail($"还不能买 {AnimalDisplayName(pick.Name)}：{(pick.Type.Length > 0 ? pick.Type : "需要先建对应的建筑")}（用 build 找 Robin 建）");
                yield break;
            }

            int price = pick.salePrice();
            var bought = new List<string>();
            string stop = null;
            for (int n = 0; n < Math.Clamp(count, 1, 12); n++)
            {
                if (Player.Money < price)
                {
                    stop = $"钱不够（{price}g 一只）";
                    break;
                }
                // 与 PurchaseAnimalsMenu 一致：有的动物会随机变成另一种颜色（比如白鸡/褐鸡）
                string type = pick.Name;
                if (Game1.farmAnimalData.TryGetValue(type, out var data) && data.AlternatePurchaseTypes != null)
                {
                    foreach (var alt in data.AlternatePurchaseTypes)
                    {
                        if (GameStateQuery.CheckConditions(alt.Condition))
                        {
                            type = Game1.random.ChooseFrom(alt.AnimalIds);
                            break;
                        }
                    }
                }
                var animal = new FarmAnimal(type, NewMultiplayerId(), Player.UniqueMultiplayerID);
                var home = farm.buildings.FirstOrDefault(b => !b.isUnderConstruction() && b.GetIndoors() is AnimalHouse h && !h.isFull() && animal.CanLiveIn(b));
                if (home == null)
                {
                    stop = $"没有能住 {animal.displayType} 的空位（鸡舍/畜棚满了或者没建）";
                    break;
                }
                string animalName = !string.IsNullOrWhiteSpace(name) && n == 0 && !Utility.areThereAnyOtherAnimalsWithThisName(name.Trim()) ? name.Trim() : null;
                for (int tries = 0; animalName == null && tries < 20; tries++)
                {
                    string candidate = StardewValley.Dialogue.randomName();
                    if (!Utility.areThereAnyOtherAnimalsWithThisName(candidate))
                        animalName = candidate;
                }
                animalName ??= type + Game1.random.Next(1000, 9999);
                animal.Name = animalName;
                animal.displayName = animalName;
                ((AnimalHouse)home.GetIndoors()).adoptAnimal(animal);
                Player.Money -= price;
                bought.Add($"{animal.displayType}“{animalName}”→{home.buildingType.Value}");
                Game1.playSound("purchaseClick");
                foreach (var s in WaitTicks(10))
                    yield return s;
            }
            string msg = bought.Count > 0 ? $"买了 {bought.Count} 只：{string.Join("、", bought)}，花了 {bought.Count * price}g，剩 {Player.Money}g" : "一只也没买成";
            if (stop != null)
                msg += "（" + stop + "）";
            yield return bought.Count > 0 ? ActionResult.Success(msg) : ActionResult.Fail(msg);
        }

        // ───────────────────────── 建造（Robin） ─────────────────────────

        private static string BlueprintCost(CarpenterMenu.BlueprintEntry bp) =>
            $"{bp.BuildCost}g" + (bp.BuildMaterials != null && bp.BuildMaterials.Count > 0
                ? " + " + string.Join(" + ", bp.BuildMaterials.Select(m => $"{m.Amount}个{Util.ObjectName(m.ItemId)}"))
                : "") + $"，工期 {bp.BuildDays} 天";

        /// <summary>这块地能不能放下这个建筑（和 buildStructure 的检查一致，必须在农场上调用）。</summary>
        private static bool CanPlace(GameLocation farm, BuildingData data, Point tile)
        {
            for (int dy = 0; dy < data.Size.Y; dy++)
                for (int dx = 0; dx < data.Size.X; dx++)
                {
                    var v = new Vector2(tile.X + dx, tile.Y + dy);
                    // 不压耕地、树等地形（游戏允许压草，但耕地和作物会被毁掉，这里保守一点）
                    if (farm.terrainFeatures.TryGetValue(v, out var tf) && tf is not Grass)
                        return false;
                    if (!farm.isBuildable(v))
                        return false;
                    if (Player.GetBoundingBox().Intersects(new Rectangle((int)v.X * 64, (int)v.Y * 64, 64, 64)))
                        return false;
                }
            if (data.AdditionalPlacementTiles != null)
                foreach (var extra in data.AdditionalPlacementTiles)
                    foreach (var p in extra.TileArea.GetPoints())
                        if (!farm.isBuildable(new Vector2(tile.X + p.X, tile.Y + p.Y), extra.OnlyNeedsToBePassable))
                            return false;
            return true;
        }

        public IEnumerable<object> Build(string buildingSpec, int? x, int? y)
        {
            var o = new Outcome();
            foreach (var s in GoToShopkeeper("ScienceHouse", "Robin", "Carpenter", o))
                yield return s;
            if (!o.Ok)
            {
                yield return ActionResult.Fail(o.Message);
                yield break;
            }
            if (Game1.IsThereABuildingUnderConstruction("Robin") || Player.daysUntilHouseUpgrade.Value > 0)
            {
                yield return ActionResult.Fail("Robin 正在施工，同一时间只能建一个，等完工再来");
                yield break;
            }

            var farm = Game1.getFarm();
            // 用木匠界面对象拿到可建列表、价格和扣材料的逻辑（不显示界面）
            var menu = new CarpenterMenu("Robin", farm);
            var bp = string.IsNullOrWhiteSpace(buildingSpec) ? null :
                menu.Blueprints.FirstOrDefault(b => b.Id.Equals(buildingSpec, StringComparison.OrdinalIgnoreCase) || b.DisplayName.Equals(buildingSpec, StringComparison.OrdinalIgnoreCase))
                ?? menu.Blueprints.FirstOrDefault(b => b.Id.Contains(buildingSpec, StringComparison.OrdinalIgnoreCase) || b.DisplayName.Contains(buildingSpec, StringComparison.OrdinalIgnoreCase));
            if (bp == null)
            {
                var lines = menu.Blueprints.Select(b =>
                {
                    menu.SetNewActiveBlueprint(b);
                    string ok = menu.DoesFarmerHaveEnoughResourcesToBuild() ? "【材料够】" : "";
                    string up = b.IsUpgrade ? $"（由 {b.UpgradeFrom} 升级）" : "";
                    return $"{b.DisplayName}({b.Id}){up}{ok}：{BlueprintCost(b)}";
                });
                yield return ActionResult.Fail("Robin 可以建：\n" + string.Join("\n", lines) + "\n（升级农舍用 upgrade_house）");
                yield break;
            }
            menu.SetNewActiveBlueprint(bp);
            if (!menu.DoesFarmerHaveEnoughResourcesToBuild())
            {
                yield return ActionResult.Fail($"材料或钱不够：{bp.DisplayName} 需要 {BlueprintCost(bp)}，你有 {Player.Money}g");
                yield break;
            }

            // 和游戏一样：确认之后到农场上选位置
            if (Loc != farm)
            {
                foreach (var s in TravelInner("Farm", o))
                    yield return s;
                if (!o.Ok)
                {
                    yield return ActionResult.Fail("去农场失败：" + o.Message);
                    yield break;
                }
            }

            if (bp.IsUpgrade)
            {
                var target = farm.buildings.FirstOrDefault(b => b.buildingType.Value == bp.UpgradeFrom && !b.isUnderConstruction() && b.daysUntilUpgrade.Value <= 0);
                if (target == null)
                {
                    yield return ActionResult.Fail($"农场上没有可以升级的 {bp.UpgradeFrom}");
                    yield break;
                }
                // 与木匠界面“升级”操作一致
                menu.ConsumeResources();
                target.upgradeName.Value = bp.Id;
                target.daysUntilUpgrade.Value = Math.Max(bp.BuildDays, 1);
                target.showUpgradeAnimation(farm);
                Game1.playSound("axe");
                if (bp.BuildDays < 1)
                    target.FinishConstruction();
                else
                    Game1.netWorldState.Value.MarkUnderConstruction("Robin", target);
                yield return ActionResult.Success($"Robin 开始把 ({target.tileX.Value},{target.tileY.Value}) 的 {bp.UpgradeFrom} 升级成 {bp.DisplayName}，{bp.BuildDays} 天后完工，花费 {BlueprintCost(bp)}");
                yield break;
            }

            var data = bp.Data;
            Point? spot = null;
            if (x.HasValue && y.HasValue)
            {
                if (!CanPlace(farm, data, new Point(x.Value, y.Value)))
                {
                    yield return ActionResult.Fail($"({x},{y}) 放不下 {bp.DisplayName}（{data.Size.X}x{data.Size.Y}，坐标是左上角；不能压到建筑、石头树木、耕地，必须在可建造区域）。不传坐标我会自动找位置");
                    yield break;
                }
                spot = new Point(x.Value, y.Value);
            }
            else
            {
                // 在农舍附近由近到远找第一块放得下的地
                var house = farm.GetMainFarmHouseEntry();
                int w = Pathfinder.MapWidth(farm), h = Pathfinder.MapHeight(farm);
                var candidates = new List<Point>();
                for (int cx = 1; cx < w - data.Size.X; cx++)
                    for (int cy = 1; cy < h - data.Size.Y; cy++)
                        candidates.Add(new Point(cx, cy));
                foreach (var c in candidates.OrderBy(c => Util.Manhattan(c, house)))
                {
                    if (Util.Manhattan(c, house) < 6)
                        continue; // 别堵在家门口
                    if (CanPlace(farm, data, c))
                    {
                        spot = c;
                        break;
                    }
                }
                if (spot == null)
                {
                    yield return ActionResult.Fail($"农场上找不到能放下 {bp.DisplayName}（{data.Size.X}x{data.Size.Y}）的空地，先 clear_area 清出一块");
                    yield break;
                }
            }

            if (!farm.buildStructure(bp.Id, new Vector2(spot.Value.X, spot.Value.Y), Player, out var constructed, bp.MagicalConstruction))
            {
                yield return ActionResult.Fail($"在 ({spot.Value.X},{spot.Value.Y}) 建造失败");
                yield break;
            }
            // 与木匠界面 tryToBuild 成功后的处理一致
            if (constructed.isUnderConstruction())
                Game1.netWorldState.Value.MarkUnderConstruction("Robin", constructed);
            menu.ConsumeResources();
            Game1.playSound("axe");
            yield return ActionResult.Success($"Robin 开始在农场 ({spot.Value.X},{spot.Value.Y}) 建 {bp.DisplayName}（{data.Size.X}x{data.Size.Y}），{bp.BuildDays} 天后完工，花费 {BlueprintCost(bp)}");
        }

        public IEnumerable<object> UpgradeHouse()
        {
            if (Player.daysUntilHouseUpgrade.Value > 0)
            {
                yield return ActionResult.Fail($"房子正在升级，还要 {Player.daysUntilHouseUpgrade.Value} 天");
                yield break;
            }
            var o = new Outcome();
            foreach (var s in GoToShopkeeper("ScienceHouse", "Robin", "Carpenter", o))
                yield return s;
            if (!o.Ok)
            {
                yield return ActionResult.Fail(o.Message);
                yield break;
            }
            if (Game1.IsThereABuildingUnderConstruction("Robin"))
            {
                yield return ActionResult.Fail("Robin 正在施工别的建筑，等完工再来");
                yield break;
            }
            int level = Player.houseUpgradeLevel.Value;
            string need = level switch
            {
                0 => "10000g + 450 木材（第一次升级：有厨房）",
                1 => "65000g + 100 硬木（第二次：育婴室）",
                2 => "100000g（第三次：地窖）",
                _ => null
            };
            if (need == null)
            {
                yield return ActionResult.Fail("房子已经升到最高级了");
                yield break;
            }
            int moneyBefore = Player.Money;
            // 游戏自己的“升级房子”确认逻辑：检查钱和材料、扣除、Robin 说话
            reflection.GetMethod(Loc, "houseUpgradeAccept").Invoke();
            foreach (var s in WaitTicks(20))
                yield return s;
            string said = Game1.activeClickableMenu is DialogueBox db ? db.getCurrentString() : "";
            foreach (var s in CloseDialogues())
                yield return s;
            yield return Player.daysUntilHouseUpgrade.Value > 0
                ? ActionResult.Success($"Robin 开始升级房子（花费 {moneyBefore - Player.Money}g 和材料），{Player.daysUntilHouseUpgrade.Value} 天后完工")
                : ActionResult.Fail($"没能升级（需要 {need}）：{said}");
        }
    }
}
