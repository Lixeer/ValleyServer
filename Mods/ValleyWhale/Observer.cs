using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Monsters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace BlindWhaleFarmer
{
    /// <summary>把游戏画面“翻译”成纯文本，供不带视觉能力的大模型理解。</summary>
    internal sealed class Observer
    {
        private readonly ModConfig config;
        private readonly IReflectionHelper reflection;

        public Observer(ModConfig config, IReflectionHelper reflection)
        {
            this.config = config;
            this.reflection = reflection;
        }

        public const string Legend =
            "@你 .可走 #不可走 ~水 D出口/门 N村民 a动物 p宠物/马 M怪物 B建筑 $出货箱 " +
            "o石头/矿 w杂草 /树枝 *可捡的采集品 ?远古斑点(用锄头挖) X物品/机器/箱子 &机器有产物可收取 R大型障碍(大树桩/大石头) " +
            "T树 t小树苗 S树桩 F果树 b灌木 \"草 _地板/小路 =空耕地(干) :空耕地(已浇水) v作物(需浇水) c作物(已浇水/不需浇水) !作物可收获 d枯死作物 f家具 L矿洞下楼梯 H矿洞竖井";

        public string Build()
        {
            var sb = new StringBuilder();
            var player = Game1.player;
            var loc = Game1.currentLocation;
            var pt = player.TilePoint;

            sb.AppendLine("## 基本状态");
            string weather = Game1.isRaining ? (Game1.isLightning ? "雷雨" : "下雨") : Game1.isSnowing ? "下雪" : "晴";
            sb.AppendLine($"时间：{Util.Stamp()}（{Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)}），天气：{weather}，明天天气：{WeatherName(Game1.weatherForTomorrow)}");
            sb.AppendLine($"金钱：{player.Money}g  体力：{(int)player.Stamina}/{player.MaxStamina}{(player.exhausted.Value ? "（筋疲力尽：体力≤0 时跑不动，明早只有一半体力）" : "")}  生命：{player.health}/{player.maxHealth}  今日运气：{player.DailyLuck:+0.00;-0.00}");
            sb.AppendLine($"技能等级：耕种{player.FarmingLevel} 采矿{player.MiningLevel} 采集{player.ForagingLevel} 钓鱼{player.FishingLevel} 战斗{player.CombatLevel}");
            Safe(sb, () => AppendCrabPotSummary(sb));
            Safe(sb, () =>
            {
                if (GameActions.ToolUpgradeStatus() is string tool)
                    sb.AppendLine("工具升级：" + tool);
                if (player.daysUntilHouseUpgrade.Value > 0)
                    sb.AppendLine($"农舍正在升级，还要 {player.daysUntilHouseUpgrade.Value} 天");
                var building = Game1.getFarm().buildings.FirstOrDefault(b => b.isUnderConstruction());
                if (building != null)
                    sb.AppendLine($"Robin 正在农场施工：{building.buildingType.Value}（还要 {Math.Max(building.daysOfConstructionLeft.Value, building.daysUntilUpgrade.Value)} 天）");
                if (GameActions.DailyQuestHint() is string quest)
                    sb.AppendLine(quest);
                sb.AppendLine($"农场类型：{FarmTypeName()}（攻略“农场类型”一节有这种地图的要点）");
                if (GameActions.SpendingAdvice() is string spend)
                    sb.AppendLine(spend);
            });
            Safe(sb, () =>
            {
                var fest = Knowledge.TodaysFestival();
                if (fest != null && Game1.timeOfDay <= fest.End && !Game1.isFestival())
                    sb.AppendLine($"今天是{fest}：建议参加（节日上打招呼加好感，主活动有奖励）。先 get_guide topic=节日 看这个节日做什么，"
                        + $"{Util.TimeString(fest.Start)}-{Util.TimeString(fest.End)} 之间用 festival enter 入场 → greet → start。"
                        + $"节日当天镇上商店都关门；不参加的话这段时间别走 {fest.Location}（travel_to 会拦下）");
                if (Game1.CurrentEvent is { isFestival: true })
                    sb.AppendLine(GameActions.FestivalMainRunning(Game1.CurrentEvent)
                        ? "【节日主活动已经开始了】马上用 festival start 加入（会自动过对白、捡彩蛋或钓鱼），大家要一起看完，别 festival leave"
                        : Game1.IsMasterGame
                            ? "你正在节日里：时间不走，普通工具不能用。用 festival greet 打招呼、festival start 开始主活动、festival leave 离开"
                            : "你正在节日里：时间不走，普通工具不能用。用 festival greet 打招呼；主活动只有主机能开始，用 festival start 做好准备并等主机开始；festival leave 离开");
                if (GameActions.ThreatWarning() is string threat)
                    sb.AppendLine(threat);
                if (GameActions.BedtimeWarning() is string bed)
                    sb.AppendLine(bed);
            });
            if (player.mailbox.Count > 0)
                sb.AppendLine($"邮箱里有 {player.mailbox.Count} 封未读信件：邮箱在 Farm 的 ({player.getMailboxPosition().X},{player.getMailboxPosition().Y})，interact 这个坐标阅读。信里常有任务、物品，有的剧情要先读信才会触发（比如威利送鱼竿）");

            Safe(sb, () =>
            {
                if (GameActions.RomanceLine() is string romance)
                    sb.AppendLine(romance);
                if (GameActions.OtherPlayersLine() is string others)
                    sb.AppendLine(others);
                if (GameActions.TakeChat() is string chat)
                    sb.AppendLine(chat);
            });

            sb.AppendLine();
            sb.AppendLine("## 当前界面");
            Safe(sb, () => sb.AppendLine(DescribeMenu()));

            sb.AppendLine();
            sb.AppendLine("## 位置");
            sb.AppendLine($"地点：{loc.NameOrUniqueName}（{loc.DisplayName}），{(loc.IsOutdoors ? "室外" : "室内")}，地图大小 {Pathfinder.MapWidth(loc)}x{Pathfinder.MapHeight(loc)}");
            sb.AppendLine($"你站在 ({pt.X},{pt.Y})，面朝{Util.DirName(player.FacingDirection)}");
            Safe(sb, () =>
            {
                if (loc.canFishHere() && Util.FindTool<StardewValley.Tools.FishingRod>() >= 0 && HasWaterNearby(loc, pt, 30))
                    sb.AppendLine("附近有水，可以用 fish 工具钓鱼（会自动找岸边）");
            });
            Safe(sb, () => AppendMine(sb, loc, pt));

            sb.AppendLine();
            sb.AppendLine("## 背包（序号: 物品）");
            Safe(sb, () => AppendInventory(sb));
            Safe(sb, () => AppendDonationHints(sb));

            sb.AppendLine();
            sb.AppendLine("## 本地出口（travel_to 可以自动走多段路，下面只是本地点的直接出口）");
            Safe(sb, () =>
            {
                var exits = Exits.Get(loc);
                if (exits.Count == 0)
                    sb.AppendLine("无");
                foreach (var e in exits.OrderBy(e => Util.Manhattan(e.Tile, pt)))
                    sb.AppendLine("- " + e.Describe());
            });

            sb.AppendLine();
            sb.AppendLine("## 人物");
            Safe(sb, () => AppendCharacters(sb, loc, pt));
            Safe(sb, () => AppendVillagerWhereabouts(sb, loc));
            Safe(sb, () => AppendAnimals(sb, loc, pt));

            sb.AppendLine();
            sb.AppendLine("## 作物（本地点全部）");
            Safe(sb, () =>
            {
                AppendCrops(sb, loc, pt);
                if (loc is not Farm)
                {
                    CountCrops(Game1.getFarm(), out int needWater, out int ready, out int total);
                    sb.AppendLine($"（农场概况：共 {total} 株作物，需浇水 {needWater}，可收获 {ready}）");
                }
            });

            sb.AppendLine();
            sb.AppendLine($"## 附近物体（半径 {config.ScanRadius} 格内，按距离排序）");
            Safe(sb, () => AppendNearby(sb, loc, pt));

            sb.AppendLine();
            sb.AppendLine($"## 文字地图（以你为中心，左上角坐标=({pt.X - config.MapRadiusX},{pt.Y - config.MapRadiusY})，每个字符一格，向右 x+1，向下 y+1）");
            sb.AppendLine("图例：" + Legend);
            Safe(sb, () => AppendMap(sb, loc, pt));

            Safe(sb, () => AppendQuests(sb));
            return sb.ToString();
        }

        /// <summary>所有地点的蟹笼：哪里有收获、哪里没鱼饵。</summary>
        private static void AppendCrabPotSummary(StringBuilder sb)
        {
            var parts = new List<string>();
            foreach (var l in Game1.locations)
            {
                int total = 0, ready = 0, noBait = 0;
                foreach (var obj in l.objects.Values)
                {
                    if (obj is not CrabPot pot)
                        continue;
                    total++;
                    if (pot.readyForHarvest.Value && pot.heldObject.Value != null)
                        ready++;
                    else if (pot.NeedsBait(Game1.player) && pot.bait.Value == null)
                        noBait++;
                }
                if (total > 0)
                    parts.Add($"{l.NameOrUniqueName} {total}个（可收 {ready}，没饵 {noBait}）");
            }
            if (parts.Count > 0)
                sb.AppendLine("蟹笼：" + string.Join("；", parts) + "（check_crab_pots location=地点）");
        }

        /// <summary>背包里能捐给博物馆、能交给社区中心收集包的东西。</summary>
        private static void AppendDonationHints(StringBuilder sb)
        {
            var museum = Knowledge.MuseumDonatable();
            if (museum.Count > 0)
                sb.AppendLine($"可以捐给博物馆：{string.Join("、", museum.Select(i => i.DisplayName))}（用 donate_museum）");

            if (Knowledge.CommunityCenterUnavailable() != null)
                return;
            var hints = new List<string>();
            foreach (var b in Knowledge.Bundles().Where(b => !b.Complete && Knowledge.IsBundleAvailable(b)))
            {
                var have = Knowledge.InventoryMatches(b);
                if (have.Count > 0)
                    hints.Add($"{string.Join("、", have)}→{b.DisplayName}");
            }
            if (hints.Count > 0)
                sb.AppendLine($"可以交给社区中心收集包：{string.Join("；", hints.Take(8))}（用 donate_bundles；bundle_info 看还缺什么）");
        }

        /// <summary>动物：总体待办（任何地点都显示）+ 当前地点里每只动物的状态。</summary>
        private static void AppendAnimals(StringBuilder sb, GameLocation loc, Point pt)
        {
            var houses = GameActions.AnimalBuildings();
            var all = houses.SelectMany(h => h.house.animals.Values).Concat(Game1.getFarm().animals.Values).ToList();
            if (all.Count == 0)
                return;
            int unpet = all.Count(a => !a.wasPet.Value);
            int toolReady = all.Count(a => a.isAdult() && a.currentProduce.Value != null
                && a.GetHarvestType() == StardewValley.GameData.FarmAnimals.FarmAnimalHarvestType.HarvestWithTool);
            int floor = houses.Sum(h => h.house.objects.Values.Count(o => o.IsSpawnedObject));
            int hayMissing = houses.Sum(h => Math.Max(0, h.house.animalsThatLiveHere.Count
                - h.house.objects.Values.Count(o => o.QualifiedItemId == "(O)178")));
            sb.AppendLine($"（动物共 {all.Count} 只：今天没摸 {unpet}，可挤奶/剪毛 {toolReady}，屋里地上待捡产物 {floor} 个，食槽缺干草 {hayMissing} 份，" +
                $"筒仓干草 {Game1.getFarm().piecesOfHay.Value}。有待办就用 care_animals）");

            foreach (var a in loc.animals.Values.OrderBy(a => Util.Manhattan(a.TilePoint, pt)).Take(12))
            {
                var t = a.TilePoint;
                string state = a.isAdult() ? (a.currentProduce.Value != null ? "有产物" : "") : "幼崽";
                sb.AppendLine($"动物 {a.displayName}（{a.type.Value}）({t.X},{t.Y}) 好感{a.friendshipTowardFarmer.Value}/1000" +
                    $"{(a.wasPet.Value ? " 今天已摸" : " 今天没摸")}{(state.Length > 0 ? " " + state : "")}");
            }
        }

        private static string WeatherName(string code) => code switch
        {
            "Sun" => "晴",
            "Rain" => "下雨（不用浇水）",
            "Storm" => "雷雨（不用浇水）",
            "Snow" => "下雪",
            "Wind" => "刮风",
            "Festival" => "节日",
            "GreenRain" => "绿雨",
            _ => code
        };

        private static bool HasWaterNearby(GameLocation loc, Point pt, int r)
        {
            for (int x = pt.X - r; x <= pt.X + r; x += 2)
                for (int y = pt.Y - r; y <= pt.Y + r; y += 2)
                    if (loc.isTileOnMap(x, y) && loc.isTileFishable(x, y))
                        return true;
            return false;
        }

        /// <summary>矿洞：层数、下楼梯/竖井/出口/电梯的位置。楼梯要先敲石头才会出现。</summary>
        private static void AppendMine(StringBuilder sb, GameLocation loc, Point pt)
        {
            if (loc is not MineShaft mine)
                return;
            var found = new List<(Point p, string what)>();
            int w = Pathfinder.MapWidth(loc), h = Pathfinder.MapHeight(loc);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    string what = MineTile(loc, x, y);
                    if (what != null)
                        found.Add((new Point(x, y), what));
                }
            int monsters = loc.characters.Count(c => c is Monster);
            sb.AppendLine($"矿洞第 {mine.mineLevel} 层，怪物 {monsters} 只。每5层可以坐电梯；敲碎石头有机会出现下楼梯。用 explore_mine 可以自动打怪、找楼梯、往下走；fight 只打附近的怪。");
            if (found.Count == 0)
                sb.AppendLine("这一层还没有出现下楼梯，继续用镐敲石头（clear_area 或 use_tool pickaxe）。");
            foreach (var (p, what) in found.OrderBy(f => Util.Manhattan(f.p, pt)))
                sb.AppendLine($"{what} ({p.X},{p.Y})，用 interact 使用");
        }

        private static string MineTile(GameLocation loc, int x, int y)
        {
            if (loc is not MineShaft)
                return null;
            return loc.getTileIndexAt(x, y, "Buildings", "mine") switch
            {
                173 => "下楼梯",
                174 => "竖井（跳下去会连降几层并掉血）",
                115 => "出口梯子（离开矿洞）",
                112 => "电梯",
                _ => null
            };
        }

        private static void Safe(StringBuilder sb, Action section)
        {
            try
            {
                section();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"（这一部分读取失败：{ex.GetType().Name} {ex.Message}）");
            }
        }

        private void AppendInventory(StringBuilder sb)
        {
            var items = Game1.player.Items;
            int empty = 0;
            var parts = new List<string>();
            for (int i = 0; i < Game1.player.MaxItems && i < items.Count; i++)
            {
                if (items[i] == null)
                {
                    empty++;
                    continue;
                }
                string mark = i == Game1.player.CurrentToolIndex ? "（手持）" : "";
                parts.Add($"{i}: {Util.ItemLabel(items[i])}{mark}");
            }
            foreach (var p in parts)
                sb.AppendLine(p);
            sb.AppendLine($"空位：{empty} 个");
        }

        /// <summary>
        /// 其他地点的村民现在在哪（按地点分组，一行一个地点），方便规划去认识人、找人送礼。
        /// 只列能社交的村民；地点用英文内部名，可以直接 travel_to。interact / give_gift 传名字会自动找过去。
        /// </summary>
        private static void AppendVillagerWhereabouts(StringBuilder sb, GameLocation here)
        {
            var byLocation = new Dictionary<string, List<string>>();
            Utility.ForEachVillager(n =>
            {
                if (n.CanSocialize && n.currentLocation != null && n.currentLocation != here && !n.IsInvisible)
                {
                    string mark = "";
                    if (!Game1.player.friendshipData.TryGetValue(n.Name, out var f))
                        mark = "(未认识)";
                    else if (f.TalkedToToday)
                        mark = "✓";
                    if (n.isBirthday())
                        mark += "【生日】";
                    string key = n.currentLocation.NameOrUniqueName;
                    if (!byLocation.TryGetValue(key, out var names))
                        byLocation[key] = names = new List<string>();
                    names.Add(n.displayName + mark);
                }
                return true;
            });
            if (byLocation.Count == 0)
                return;
            sb.AppendLine("其他地点的村民（✓=今天聊过，未认识的标出来了；interact/give_gift 传名字会自动走过去）：");
            foreach (var (location, names) in byLocation.OrderByDescending(kv => kv.Value.Count))
                sb.AppendLine($"  {location}：{string.Join("、", names)}");
        }

        private void AppendCharacters(StringBuilder sb, GameLocation loc, Point pt)
        {
            int count = 0;
            foreach (var npc in loc.characters.OrderBy(c => Util.Manhattan(c.TilePoint, pt)))
            {
                if (npc.IsInvisible)
                    continue;
                var t = npc.TilePoint;
                string extra = "";
                if (npc is Monster m)
                    extra = $" [怪物 HP {m.Health}]";
                else if (npc is Pet)
                    extra = " [宠物]";
                else if (npc is Horse)
                    extra = " [马]";
                else if (npc.IsVillager)
                {
                    if (Game1.player.friendshipData.TryGetValue(npc.Name, out var f))
                        extra = $" [好感 {f.Points / 250}心({f.Points}) 今天{(f.TalkedToToday ? "已" : "未")}聊天 本周送礼{f.GiftsThisWeek}/2{(f.GiftsToday > 0 ? " 今天已送礼" : "")}]";
                    else
                        extra = " [还没认识]";
                    if (npc.isBirthday())
                        extra += " 【今天生日！】";
                    // 背包里对他来说是最爱/喜欢/讨厌的东西，帮助送礼决策
                    if (Util.Manhattan(npc.TilePoint, pt) <= 20)
                    {
                        var tastes = Knowledge.InventoryTastes(npc);
                        var parts = new List<string>();
                        foreach (int taste in new[] { NPC.gift_taste_love, NPC.gift_taste_like, NPC.gift_taste_hate })
                            if (tastes.TryGetValue(taste, out var items))
                                parts.Add($"{Knowledge.TasteName(taste)}:{string.Join("、", items.Take(5))}");
                        if (parts.Count > 0)
                            extra += " 背包礼物[" + string.Join(" ", parts) + "]";
                    }
                }
                sb.AppendLine($"- {npc.displayName}（{npc.Name}）在 ({t.X},{t.Y}){extra}");
                if (++count >= 20)
                    break;
            }
            // 动物由 AppendAnimals 单独列出（带好感和产物状态）
            if (count == 0 && loc.animals.Length == 0)
                sb.AppendLine("无");
        }

        public static void CountCrops(GameLocation loc, out int needWater, out int ready, out int total)
        {
            needWater = ready = total = 0;
            if (loc == null)
                return;
            foreach (var pair in loc.terrainFeatures.Pairs)
            {
                if (pair.Value is HoeDirt hd && hd.crop != null && !hd.crop.dead.Value)
                {
                    total++;
                    if (hd.readyForHarvest())
                        ready++;
                    else if (NeedsWater(hd))
                        needWater++;
                }
            }
        }

        public static bool NeedsWater(HoeDirt hd)
            => hd.crop != null && !hd.crop.dead.Value && !hd.isWatered() && hd.needsWatering() && !hd.readyForHarvest();

        public static int DaysLeft(Crop crop)
        {
            if (crop.fullyGrown.Value && crop.dayOfCurrentPhase.Value > 0)
                return crop.dayOfCurrentPhase.Value;
            int days = 0;
            var phases = crop.phaseDays;
            for (int i = crop.currentPhase.Value; i < phases.Count - 1; i++)
                days += phases[i];
            return Math.Max(0, days - crop.dayOfCurrentPhase.Value);
        }

        private void AppendCrops(StringBuilder sb, GameLocation loc, Point pt)
        {
            var needWater = new List<Point>();
            var ready = new List<Point>();
            var dead = new List<Point>();
            var emptyDirt = new List<Point>();
            var growing = new Dictionary<string, (int count, int minDays)>();

            foreach (var pair in loc.terrainFeatures.Pairs)
            {
                if (pair.Value is not HoeDirt hd)
                    continue;
                var p = new Point((int)pair.Key.X, (int)pair.Key.Y);
                if (hd.crop == null)
                {
                    if (!loc.Objects.ContainsKey(pair.Key))
                        emptyDirt.Add(p);
                    continue;
                }
                if (hd.crop.dead.Value)
                {
                    dead.Add(p);
                    continue;
                }
                if (hd.readyForHarvest())
                {
                    ready.Add(p);
                    continue;
                }
                if (NeedsWater(hd))
                    needWater.Add(p);
                string name = Util.ObjectName(hd.crop.indexOfHarvest.Value);
                int left = DaysLeft(hd.crop);
                growing[name] = growing.TryGetValue(name, out var g) ? (g.count + 1, Math.Min(g.minDays, left)) : (1, left);
            }

            int total = ready.Count + growing.Values.Sum(g => g.count) + dead.Count;
            if (total == 0 && emptyDirt.Count == 0)
            {
                sb.AppendLine("无");
                return;
            }
            Func<List<Point>, string> near = l => Util.Coords(l.OrderBy(p => Util.Manhattan(p, pt)), 30);
            if (ready.Count > 0)
                sb.AppendLine($"可收获 {ready.Count} 株：{near(ready)}");
            if (needWater.Count > 0)
                sb.AppendLine($"需要浇水 {needWater.Count} 株：{near(needWater)}");
            foreach (var g in growing)
                sb.AppendLine($"生长中：{g.Key} x{g.Value.count}（最快约 {g.Value.minDays} 天后成熟）");
            if (dead.Count > 0)
                sb.AppendLine($"枯死作物 {dead.Count} 株（可用镰刀清除）：{near(dead)}");
            if (emptyDirt.Count > 0)
                sb.AppendLine($"空耕地 {emptyDirt.Count} 格（可播种）：{near(emptyDirt)}");
        }

        /// <summary>农场地图类型（Farm.*_layout；7 是 1.6 的草原农场等数据定义的农场，看 GetFarmTypeID）。</summary>
        public static string FarmTypeName() => Game1.whichFarm switch
        {
            Farm.default_layout => "标准农场",
            Farm.riverlands_layout => "河边农场（水多地少）",
            Farm.forest_layout => "森林农场（有大树桩和采集品）",
            Farm.mountains_layout => "山顶农场（有矿石）",
            Farm.combat_layout => "荒野农场（晚上有怪）",
            Farm.fourCorners_layout => "四角农场",
            Farm.beach_layout => "海滩农场（沙地上洒水器不起作用）",
            _ => Game1.GetFarmTypeID() == "MeadowlandsFarm" ? "草原农场（开局有鸡舍和两只鸡）" : Game1.GetFarmTypeID(),
        };

        public static string ClumpName(ResourceClump rc) => rc.parentSheetIndex.Value switch
        {
            600 => "大树桩（需要铜斧）",
            602 => "大原木（需要钢斧）",
            622 => "陨石（需要金镐）",
            672 => "大石头（需要钢镐）",
            752 or 754 or 756 or 758 => "矿石巨石（需要镐子）",
            44 or 46 => "巨型作物（用斧头）",
            _ => "大型障碍物"
        };

        /// <summary>单个格子的分类：地图字符、名称、建议做法。</summary>
        public static (char ch, string name, string hint) ClassifyFeature(GameLocation loc, Vector2 v)
        {
            if (loc.Objects.TryGetValue(v, out SObject o))
            {
                if (o.IsBreakableStone())
                    return ('o', o.DisplayName, "镐子");
                if (o.IsWeeds())
                    return ('w', o.DisplayName, "镰刀");
                if (o.IsTwig())
                    return ('/', o.DisplayName, "斧头");
                if (o.QualifiedItemId == "(O)590" || o.QualifiedItemId == "(O)SeedSpot")
                    return ('?', o.DisplayName, "锄头");
                if (o.IsSpawnedObject || o.isForage())
                    return ('*', o.DisplayName, "interact 捡起");
                if (o is CrabPot pot)
                {
                    if (pot.readyForHarvest.Value && pot.heldObject.Value != null)
                        return ('&', $"蟹笼（有收获：{pot.heldObject.Value.DisplayName}）", "check_crab_pots");
                    if (pot.NeedsBait(Game1.player) && pot.bait.Value == null)
                        return ('X', "蟹笼（没放鱼饵）", "check_crab_pots");
                    return ('X', "蟹笼（等待中）", "");
                }
                if (o.readyForHarvest.Value && o.heldObject.Value != null)
                    return ('&', $"{o.DisplayName}（产物：{o.heldObject.Value.DisplayName}）", "interact 收取");
                if (o is Chest { giftbox.Value: true } gift)
                    return ('*', $"礼物箱（里面有：{string.Join("、", gift.Items.Where(i => i != null).Select(i => $"{i.DisplayName}x{i.Stack}"))}）", "interact 打开");
                if (o is Chest)
                    return ('X', o.DisplayName, "箱子");
                if (o.heldObject.Value != null && o.MinutesUntilReady > 0)
                    return ('X', $"{o.DisplayName}（加工中：{o.heldObject.Value.DisplayName}，还需 {o.MinutesUntilReady} 分钟）", "");
                return ('X', o.DisplayName, o.bigCraftable.Value ? "机器/设备" : "");
            }

            if (loc.terrainFeatures.TryGetValue(v, out var tf))
            {
                switch (tf)
                {
                    case HoeDirt hd:
                        if (hd.crop == null)
                            return hd.isWatered() ? (':', "空耕地(湿)", "") : ('=', "空耕地(干)", "");
                        string cropName = Util.ObjectName(hd.crop.indexOfHarvest.Value);
                        if (hd.crop.dead.Value)
                            return ('d', "枯死的" + cropName, "镰刀");
                        if (hd.readyForHarvest())
                            return ('!', cropName + "(可收获)", "interact 或 harvest_crops");
                        if (NeedsWater(hd))
                            return ('v', cropName + "(需浇水)", "水壶");
                        return ('c', cropName, "");
                    case Tree tree:
                        if (tree.stump.Value)
                            return ('S', "树桩", "斧头");
                        if (tree.growthStage.Value >= 5)
                            return ('T', "树", "斧头（大树要砍很多下）");
                        return ('t', "小树苗", "斧头/镰刀");
                    case FruitTree:
                        return ('F', "果树", "");
                    case Grass:
                        return ('"', "草", "镰刀");
                    case Flooring:
                        return ('_', "地板", "");
                }
            }
            return ('\0', null, null);
        }

        private void AppendNearby(StringBuilder sb, GameLocation loc, Point pt)
        {
            int r = config.ScanRadius;
            var groups = new Dictionary<string, List<Point>>();
            var hints = new Dictionary<string, string>();

            void Add(string name, string hint, Point p)
            {
                if (!groups.TryGetValue(name, out var l))
                    groups[name] = l = new List<Point>();
                l.Add(p);
                if (!string.IsNullOrEmpty(hint))
                    hints[name] = hint;
            }

            foreach (var pair in loc.Objects.Pairs)
            {
                var p = new Point((int)pair.Key.X, (int)pair.Key.Y);
                if (Util.Manhattan(p, pt) > r * 2 || Math.Abs(p.X - pt.X) > r || Math.Abs(p.Y - pt.Y) > r)
                    continue;
                var (_, name, hint) = ClassifyFeature(loc, pair.Key);
                if (name != null)
                    Add(name, hint, p);
            }
            foreach (var pair in loc.terrainFeatures.Pairs)
            {
                var p = new Point((int)pair.Key.X, (int)pair.Key.Y);
                if (Math.Abs(p.X - pt.X) > r || Math.Abs(p.Y - pt.Y) > r)
                    continue;
                if (pair.Value is HoeDirt || pair.Value is Flooring || pair.Value is Grass)
                    continue; // 作物单独列出；草和地板太多，只在地图上显示
                var (_, name, hint) = ClassifyFeature(loc, pair.Key);
                if (name != null)
                    Add(name, hint, p);
            }
            foreach (var rc in loc.resourceClumps)
            {
                var p = new Point((int)rc.Tile.X, (int)rc.Tile.Y);
                if (Math.Abs(p.X - pt.X) > r || Math.Abs(p.Y - pt.Y) > r)
                    continue;
                Add(ClumpName(rc) + $"（{rc.width.Value}x{rc.height.Value}，坐标是左上角）", "", p);
            }
            foreach (var b in loc.buildings)
            {
                var p = new Point(b.tileX.Value, b.tileY.Value);
                string name = b is ShippingBin ? "出货箱（站在旁边用 ship_items 出货）" : $"建筑 {b.buildingType.Value}（{b.tilesWide.Value}x{b.tilesHigh.Value}，坐标是左上角）";
                Add(name, "", p);
            }
            foreach (var f in loc.furniture)
            {
                var p = new Point((int)f.TileLocation.X, (int)f.TileLocation.Y);
                if (Math.Abs(p.X - pt.X) > r || Math.Abs(p.Y - pt.Y) > r)
                    continue;
                Add("家具 " + f.DisplayName, "", p);
            }
            int debris = loc.debris.Count(d => d.item != null || d.itemId.Value != null);
            if (debris > 0)
                sb.AppendLine($"地上有 {debris} 堆掉落物（走近会自动捡起）");

            // 可交互的地图格（商店柜台、告示板、邮箱等）
            for (int x = pt.X - r; x <= pt.X + r; x++)
            {
                for (int y = pt.Y - r; y <= pt.Y + r; y++)
                {
                    if (!loc.isTileOnMap(x, y))
                        continue;
                    string action = loc.doesTileHaveProperty(x, y, "Action", "Buildings");
                    if (string.IsNullOrEmpty(action))
                        continue;
                    string kind = action.Split(' ')[0];
                    if (kind is "Warp" or "LockedDoorWarp" or "Door" or "WarpCommunityCenter")
                        continue;
                    Add($"可交互地图格[{kind}]", "interact", new Point(x, y));
                }
            }

            if (groups.Count == 0)
            {
                sb.AppendLine("无");
                return;
            }
            foreach (var g in groups.OrderBy(g => g.Value.Min(p => Util.Manhattan(p, pt))).Take(40))
            {
                string hint = hints.TryGetValue(g.Key, out var h) ? $" [{h}]" : "";
                sb.AppendLine($"- {g.Key}{hint}：{Util.Coords(g.Value.OrderBy(p => Util.Manhattan(p, pt)), 12)}");
            }
        }

        private void AppendMap(StringBuilder sb, GameLocation loc, Point pt)
        {
            int rx = config.MapRadiusX, ry = config.MapRadiusY;
            var overlay = new Dictionary<Point, char>();

            foreach (var b in loc.buildings)
            {
                char c = b is ShippingBin ? '$' : 'B';
                for (int x = 0; x < b.tilesWide.Value; x++)
                    for (int y = 0; y < b.tilesHigh.Value; y++)
                        overlay[new Point(b.tileX.Value + x, b.tileY.Value + y)] = c;
            }
            foreach (var rc in loc.resourceClumps)
                for (int x = 0; x < rc.width.Value; x++)
                    for (int y = 0; y < rc.height.Value; y++)
                        overlay[new Point((int)rc.Tile.X + x, (int)rc.Tile.Y + y)] = 'R';
            foreach (var ltf in loc.largeTerrainFeatures)
            {
                var bb = ltf.getBoundingBox();
                for (int x = bb.Left / 64; x <= (bb.Right - 1) / 64; x++)
                    for (int y = bb.Top / 64; y <= (bb.Bottom - 1) / 64; y++)
                        overlay[new Point(x, y)] = 'b';
            }
            foreach (var f in loc.furniture)
            {
                var bb = f.GetBoundingBox();
                if (f.isPassable())
                    continue;
                for (int x = bb.Left / 64; x <= (bb.Right - 1) / 64; x++)
                    for (int y = bb.Top / 64; y <= (bb.Bottom - 1) / 64; y++)
                        overlay[new Point(x, y)] = 'f';
            }
            foreach (var e in Exits.Get(loc))
                overlay[e.Tile] = 'D';
            foreach (var w in loc.warps)
                overlay[new Point(w.X, w.Y)] = 'D';
            foreach (var a in loc.animals.Values)
                overlay[a.TilePoint] = 'a';
            foreach (var npc in loc.characters)
            {
                if (npc.IsInvisible)
                    continue;
                overlay[npc.TilePoint] = npc is Monster ? 'M' : (npc is Pet || npc is Horse) ? 'p' : 'N';
            }
            overlay[pt] = '@';

            for (int y = pt.Y - ry; y <= pt.Y + ry; y++)
            {
                var line = new StringBuilder();
                line.Append($"{y,4} ");
                for (int x = pt.X - rx; x <= pt.X + rx; x++)
                    line.Append(TileChar(loc, x, y, overlay));
                sb.AppendLine(line.ToString().TrimEnd());
            }
        }

        private static char TileChar(GameLocation loc, int x, int y, Dictionary<Point, char> overlay)
        {
            var p = new Point(x, y);
            if (overlay.TryGetValue(p, out char oc))
            {
                if (oc == 'D' || oc == '@' || oc == 'N' || oc == 'M' || oc == 'a' || oc == 'p')
                    return oc;
            }
            if (!loc.isTileOnMap(x, y))
                return ' ';
            if (MineTile(loc, x, y) is string mt)
                return mt.StartsWith("下楼梯") ? 'L' : mt.StartsWith("竖井") ? 'H' : 'D';
            var (ch, _, _) = ClassifyFeature(loc, new Vector2(x, y));
            if (ch != '\0')
                return ch;
            if (overlay.TryGetValue(p, out oc))
                return oc;
            if (loc.isWaterTile(x, y))
                return '~';
            return Pathfinder.IsPassable(loc, x, y) ? '.' : '#';
        }

        private void AppendQuests(StringBuilder sb)
        {
            var quests = Game1.player.questLog;
            if (quests.Count == 0)
                return;
            sb.AppendLine();
            sb.AppendLine("## 任务");
            foreach (var q in quests.Take(8))
            {
                string obj = "";
                try
                {
                    obj = q.GetObjectiveDescriptions()?.FirstOrDefault() ?? "";
                }
                catch
                {
                    // 有些任务没有目标描述
                }
                sb.AppendLine($"- {q.GetName()}：{obj}{(q.completed.Value ? "（已完成）" : "")}");
            }
        }

        public string DescribeMenu()
        {
            var sb = new StringBuilder();
            if (Game1.eventUp && GameActions.FestivalMainRunning(Game1.CurrentEvent))
                sb.AppendLine("节日主活动进行中：用 festival start 接着参加（会自动过对白、捡彩蛋或钓鱼）。");
            else if (Game1.eventUp && Game1.CurrentEvent is { isFestival: true })
                sb.AppendLine("你在节日里：时间停止，移动和干活的工具都不能用。用 leave_festival 离开节日回去继续干活（对话框打开时先用 dialogue 说完）。");
            else if (Game1.eventUp && Game1.CurrentEvent != null)
                sb.AppendLine($"正在播放剧情/节日事件{(Game1.CurrentEvent.skippable ? "（可以用 skip_event 跳过）" : "")}。剧情中出现对话时用 dialogue 推进。");

            var menu = Game1.activeClickableMenu;
            if (menu == null)
            {
                if (sb.Length == 0)
                    sb.Append("无（可以自由行动）");
                return sb.ToString().TrimEnd();
            }

            switch (menu)
            {
                case DialogueBox db:
                    string speaker = db.characterDialogue?.speaker?.displayName;
                    sb.AppendLine($"对话框{(speaker != null ? $"（{speaker}）" : "")}：{db.getCurrentString()}");
                    if (db.isQuestion && db.responses != null)
                    {
                        sb.AppendLine("选项（用 dialogue 并传 choice 序号回答）：");
                        for (int i = 0; i < db.responses.Length; i++)
                            sb.AppendLine($"  {i}: {db.responses[i].responseText}");
                    }
                    else
                        sb.AppendLine("（用 dialogue 不带参数继续/关闭对话）");
                    break;

                case ReadyCheckDialog ready:
                    sb.AppendLine(GameActions.ReadyCheckHint(ready));
                    break;

                case NamingMenu:
                    sb.AppendLine("要起名字（孩子或者刚出生的动物）：用 set_name 传一个名字");
                    break;

                case GeodeMenu:
                    sb.AppendLine("铁匠砸晶球的界面。不用点这里，直接用 crack_geodes 砸，开出什么会告诉你；不砸就 close_menu。");
                    break;

                case ShopMenu shop:
                    sb.AppendLine($"商店界面（{shop.ShopId}）。你的钱：{Game1.player.Money}g。用 buy 购买、sell 出售、close_menu 离开。商品：");
                    int i2 = 0;
                    foreach (var s in shop.forSale)
                    {
                        if (!shop.itemPriceAndStock.TryGetValue(s, out var info))
                            continue;
                        string stock = info.Stock == int.MaxValue ? "" : $" 库存{info.Stock}";
                        string trade = info.TradeItem != null ? $" +{info.TradeItemCount ?? 1}个{Util.ObjectName(info.TradeItem)}" : "";
                        sb.AppendLine($"  {i2}: {s.DisplayName} {info.Price}g{trade}{stock}");
                        if (++i2 >= 60)
                            break;
                    }
                    break;

                case LevelUpMenu lvl:
                    if (lvl.isProfessionChooser)
                    {
                        var left = reflection.GetField<List<string>>(lvl, "leftProfessionDescription", false)?.GetValue();
                        var right = reflection.GetField<List<string>>(lvl, "rightProfessionDescription", false)?.GetValue();
                        sb.AppendLine("升级了，需要选择职业（用 choose_profession，left 或 right）：");
                        sb.AppendLine("  left: " + (left != null ? string.Join(" ", left) : "?"));
                        sb.AppendLine("  right: " + (right != null ? string.Join(" ", right) : "?"));
                    }
                    else
                        sb.AppendLine("升级提示界面（会自动确认，也可以 close_menu）");
                    break;

                case ShippingMenu:
                    sb.AppendLine("每日出货结算界面（会自动确认；如果没有，用 menu_click 点 OK 按钮）");
                    break;

                case LetterViewerMenu letter:
                    var msg = reflection.GetField<List<string>>(letter, "mailMessage", false)?.GetValue();
                    sb.AppendLine("信件：" + (msg != null ? string.Join(" ", msg) : "(无法读取)"));
                    var attached = letter.itemsToGrab.Where(c => c.item != null).Select(c => Util.ItemLabel(c.item)).ToList();
                    if (attached.Count > 0)
                        sb.AppendLine("附带物品：" + string.Join("、", attached));
                    if (letter.moneyIncluded > 0)
                        sb.AppendLine($"附带 {letter.moneyIncluded}g");
                    if (!string.IsNullOrEmpty(letter.learnedRecipe))
                        sb.AppendLine("学会了配方：" + letter.learnedRecipe);
                    sb.AppendLine("（读完用 close_menu 关闭，附带物品和任务会自动收下）");
                    break;

                default:
                    sb.AppendLine($"菜单：{menu.GetType().Name}。如果不需要，用 close_menu 关闭。可点击的元素（menu_click 传序号）：");
                    var comps = MenuComponents(menu);
                    for (int i = 0; i < comps.Count && i < 40; i++)
                    {
                        var c = comps[i];
                        string label = string.Join(" / ", new[] { c.name, c.label, (c as ClickableTextureComponent)?.hoverText }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
                        sb.AppendLine($"  {i}: {label}");
                    }
                    break;
            }
            return sb.ToString().TrimEnd();
        }

        public static List<ClickableComponent> MenuComponents(IClickableMenu menu)
        {
            try
            {
                if (menu.allClickableComponents == null)
                    menu.populateClickableComponentList();
            }
            catch
            {
                // 某些菜单不支持
            }
            return menu.allClickableComponents?.Where(c => c != null && c.visible).ToList() ?? new List<ClickableComponent>();
        }
    }
}
