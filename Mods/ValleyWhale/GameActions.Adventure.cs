using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Enchantments;
using StardewValley.Inventories;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Monsters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using SObject = StardewValley.Object;
using xLocation = xTile.Dimensions.Location;

namespace BlindWhaleFarmer
{
    /// <summary>烹饪、蟹笼、战斗、下矿洞。</summary>
    internal sealed partial class GameActions
    {
        // ───────────────────────── 烹饪 ─────────────────────────

        private static string IngredientLabel(string id, int count)
        {
            string name = int.TryParse(id, out int n) && n < 0 ? "任意" + SObject.GetCategoryDisplayName(n) : Util.ObjectName(id);
            return $"{name}x{count}";
        }

        /// <summary>厨房里能用的冰箱（农舍冰箱 + 迷你冰箱）。</summary>
        private static List<IInventory> KitchenContainers(GameLocation loc)
        {
            var list = new List<IInventory>();
            var fridge = loc.GetFridge();
            if (fridge != null)
                list.Add(fridge.Items);
            foreach (var obj in loc.objects.Values)
                if (obj is Chest c && c.fridge.Value && c.bigCraftable.Value)
                    list.Add(c.Items);
            return list;
        }

        public IEnumerable<object> Cook(string recipeSpec, int count)
        {
            var known = Player.cookingRecipes.Keys.ToList();
            if (known.Count == 0)
            {
                yield return ActionResult.Fail("你还没学会任何菜谱（看电视“烹饪节目”(周日)、村民来信、升级好感都能学到）");
                yield break;
            }

            // 先回到有厨房的地方（农舍升级一次后才有厨房）
            var o = new Outcome();
            var kitchen = FindTileAction(Loc, "kitchen");
            if (kitchen == null)
            {
                if (Utility.getHomeOfFarmer(Player) is FarmHouse home && home.upgradeLevel < 1)
                {
                    yield return ActionResult.Fail("农舍还没有厨房：需要找木匠 Robin（ScienceHouse）升级房子（10000g + 450 木材）");
                    yield break;
                }
                foreach (var s in TravelInner("FarmHouse", o))
                    yield return s;
                if (!o.Ok)
                {
                    yield return ActionResult.Fail("回家做饭失败：" + o.Message);
                    yield break;
                }
                kitchen = FindTileAction(Loc, "kitchen");
                if (kitchen == null)
                {
                    yield return ActionResult.Fail("家里找不到厨房");
                    yield break;
                }
            }
            foreach (var s in GoAdjacent(kitchen.Value, o))
                yield return s;

            var containers = KitchenContainers(Loc);
            var extra = containers.SelectMany(c => c).Where(i => i != null).ToList();

            string key = known.FirstOrDefault(k => k.Equals(recipeSpec, StringComparison.OrdinalIgnoreCase))
                ?? known.FirstOrDefault(k => new CraftingRecipe(k, true).DisplayName.Equals(recipeSpec, StringComparison.OrdinalIgnoreCase))
                ?? known.FirstOrDefault(k => k.Contains(recipeSpec ?? "", StringComparison.OrdinalIgnoreCase) || new CraftingRecipe(k, true).DisplayName.Contains(recipeSpec ?? "", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(recipeSpec) || key == null)
            {
                var lines = known.Select(k =>
                {
                    var r = new CraftingRecipe(k, true);
                    string mark = r.doesFarmerHaveIngredientsInInventory(extra) ? "【现在能做】" : "";
                    return $"{r.DisplayName}({k}){mark}：{string.Join("、", r.recipeList.Select(kv => IngredientLabel(kv.Key, kv.Value)))}";
                });
                string head = string.IsNullOrWhiteSpace(recipeSpec) ? "已学会的菜谱" : $"没学会“{recipeSpec}”。已学会的菜谱";
                yield return ActionResult.Fail(head + "（背包和冰箱里的材料都能用）：\n" + string.Join("\n", lines));
                yield break;
            }

            var recipe = new CraftingRecipe(key, true);
            int made = 0;
            Item last = null;
            for (int i = 0; i < Math.Clamp(count, 1, 20); i++)
            {
                extra = containers.SelectMany(c => c).Where(it => it != null).ToList();
                if (!recipe.doesFarmerHaveIngredientsInInventory(extra))
                    break;
                var dish = recipe.createItem();
                if (!Player.couldInventoryAcceptThisItem(dish))
                    break;
                // 与烹饪界面 CraftingPage 点击菜谱时的逻辑一致
                recipe.consumeIngredients(containers);
                Player.addItemToInventoryBool(dish);
                Player.NotifyQuests(q => q.OnRecipeCrafted(recipe, dish));
                Player.cookedRecipe(dish.ItemId);
                Game1.stats.checkForCookingAchievements();
                Game1.playSound("coin");
                last = dish;
                made++;
                foreach (var s in WaitTicks(10))
                    yield return s;
            }
            if (made > 0)
                yield return ActionResult.Success($"做了 {made} 份 {recipe.DisplayName}（{Util.ItemLabel(last)}）");
            else
                yield return ActionResult.Fail($"材料不够或背包满了。{recipe.DisplayName} 需要：{string.Join("、", recipe.recipeList.Select(kv => IngredientLabel(kv.Key, kv.Value)))}");
        }

        // ───────────────────────── 蟹笼 ─────────────────────────

        /// <summary>收当前地点所有蟹笼的收获，没鱼饵的补上鱼饵。</summary>
        public IEnumerable<object> CheckCrabPots(string location)
        {
            var o = new Outcome();
            if (!string.IsNullOrWhiteSpace(location) && !Loc.NameOrUniqueName.Equals(location, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var s in TravelInner(location, o))
                    yield return s;
                if (!o.Ok)
                {
                    yield return ActionResult.Fail(o.Message);
                    yield break;
                }
            }
            var loc = Loc;
            var pots = loc.objects.Pairs.Where(p => p.Value is CrabPot).Select(p => new Point((int)p.Key.X, (int)p.Key.Y)).ToList();
            if (pots.Count == 0)
            {
                yield return ActionResult.Fail("这里没有蟹笼（在鱼店买或用配方制作，用 place_item 放到水边的水面上）");
                yield break;
            }

            int collected = 0, baited = 0;
            var got = new List<string>();
            string note = null;
            foreach (var tile in Util.NearestOrder(Player.TilePoint, pots))
            {
                var v = new Vector2(tile.X, tile.Y);
                if (!loc.objects.TryGetValue(v, out var obj) || obj is not CrabPot pot)
                    continue;
                bool ready = pot.readyForHarvest.Value && pot.heldObject.Value != null;
                bool needBait = pot.NeedsBait(Player) && pot.bait.Value == null;
                int baitIdx = Player.Items.ToList().FindIndex(i => i is SObject b && b.Category == SObject.baitCategory);
                if (!ready && !(needBait && baitIdx >= 0))
                    continue;

                foreach (var s in GoAdjacent(tile, o))
                    yield return s;
                if (!o.Ok)
                    continue;

                if (ready)
                {
                    if (Player.freeSpotsInInventory() == 0)
                    {
                        note = "背包满了，有蟹笼没收";
                        break;
                    }
                    string name = pot.heldObject.Value.DisplayName;
                    // 先换成工具，避免手里拿着鱼饵时触发放鱼饵而不是收获
                    int safe = Util.FindTool<Tool>();
                    if (safe >= 0)
                        Player.CurrentToolIndex = safe;
                    loc.checkAction(new xLocation(tile.X, tile.Y), Game1.viewport, Player);
                    foreach (var s in WaitTicks(10))
                        yield return s;
                    if (pot.heldObject.Value == null)
                    {
                        collected++;
                        got.Add(name);
                    }
                }

                if (pot.NeedsBait(Player) && pot.bait.Value == null)
                {
                    baitIdx = Player.Items.ToList().FindIndex(i => i is SObject b && b.Category == SObject.baitCategory);
                    if (baitIdx < 0)
                    {
                        note = "鱼饵用完了（鱼店有卖，或者用配方制作）";
                        continue;
                    }
                    var bait = Player.Items[baitIdx];
                    Player.CurrentToolIndex = baitIdx;
                    // 与手拿鱼饵点蟹笼的逻辑一致：放入后由调用方扣掉一个
                    if (pot.performObjectDropInAction(bait, false, Player))
                    {
                        Player.Items[baitIdx] = bait.ConsumeStack(1);
                        baited++;
                    }
                    foreach (var s in WaitTicks(6))
                        yield return s;
                }
            }

            int empty = loc.objects.Values.OfType<CrabPot>().Count(p => p.NeedsBait(Player) && p.bait.Value == null);
            string msg = $"收了 {collected} 个蟹笼{(got.Count > 0 ? "：" + string.Join("、", got) : "")}，放了 {baited} 份鱼饵；这里共 {pots.Count} 个蟹笼，{empty} 个还没鱼饵";
            if (note != null)
                msg += "（" + note + "）";
            yield return collected > 0 || baited > 0 ? ActionResult.Success(msg) : ActionResult.Fail(msg);
        }

        // ───────────────────────── 战斗 ─────────────────────────

        private sealed class FightStats
        {
            public int Kills, Swings, Eaten;
            /// <summary>找不到路走过去、等了也没过来的怪。</summary>
            public readonly HashSet<Monster> Unreachable = new();
            /// <summary>打了不掉血、这次不再打的怪。</summary>
            public readonly HashSet<Monster> Immune = new();
        }

        private static IEnumerable<Monster> LiveMonsters(GameLocation loc) =>
            loc.characters.OfType<Monster>().Where(m => m.Health > 0 && !m.IsInvisible);

        /// <summary>radius 格内最近的活怪（会主动打人的）；没有返回 null。</summary>
        private static Monster NearestThreat(int radius)
        {
            if (Loc == null)
                return null;
            var me = Player.TilePoint;
            return LiveMonsters(Loc)
                .Where(m => m.DamageToFarmer > 0 && !CannotHurt(m) && Math.Max(Math.Abs(m.TilePoint.X - me.X), Math.Abs(m.TilePoint.Y - me.Y)) <= radius)
                .OrderBy(m => Util.Manhattan(m.TilePoint, me))
                .FirstOrDefault();
        }

        /// <summary>上一次 fight 走不过去的怪：只要它还没贴到身边，提醒里就不再催 AI 去打。</summary>
        private static HashSet<Monster> KnownUnreachable = new();

        /// <summary>身边有会打人的怪时的提醒：状态里和每个动作结果后面都会带上，让 AI 先清怪再干活。</summary>
        public static string ThreatWarning()
        {
            if (!StardewModdingAPI.Context.IsWorldReady || Game1.eventUp)
                return null;
            var near = LiveMonsters(Loc).Where(m => m.DamageToFarmer > 0 && !CannotHurt(m)).ToList();
            var me = Player.TilePoint;
            var close = near.Where(m => Util.Manhattan(m.TilePoint, me) <= 6)
                .Where(m => !KnownUnreachable.Contains(m) || Util.Manhattan(m.TilePoint, me) <= 1)
                .OrderBy(m => Util.Manhattan(m.TilePoint, me)).ToList();
            if (close.Count == 0)
                return null;
            string list = string.Join("、", close.Take(4).Select(m => $"{m.displayName}({m.TilePoint.X},{m.TilePoint.Y})"));
            return $"⚠ 附近有 {close.Count} 只怪：{list}，生命 {Player.health}/{Player.maxHealth}。先用 fight 清掉再干别的；生命低又没有回血食物就撤退";
        }

        /// <summary>会给怪加血的怪：暗影萨满每次施法约四成概率给附近血量比例最低的怪（常常是它自己）加 60 血，血少于 30 会逃跑。</summary>
        private static bool IsHealer(Monster m) => m is ShadowShaman;

        /// <summary>
        /// 岩石蟹（长得像石头的怪）缩在壳里、停着不动：这时武器只有 0 伤害（RockCrab.takeDamage），要用镐子敲（敲一下就醒，
        /// 走动时武器打得动；敲 5 下壳就碎）。竹节虫（isStickBug）不吃镐子，不算。
        /// </summary>
        private static bool ShellClosed(Monster m) =>
            m is RockCrab crab && !crab.isStickBug.Value && !crab.shellGone.Value && crab.Sprite.currentFrame % 4 == 0;

        /// <summary>现在手上的东西打不动的怪：没有镐子时缩壳的岩石蟹；最好的武器没有除虫附魔时的装甲虫。</summary>
        private static bool CannotHurt(Monster m)
        {
            if (ShellClosed(m))
                return Util.FindTool<Pickaxe>() < 0;
            if (m is Bug bug && bug.isArmoredBug.Value)
            {
                int best = BestWeapon();
                return best < 0 || Player.Items[best] is not MeleeWeapon w || !w.hasEnchantmentOfType<BugKillerEnchantment>();
            }
            return false;
        }

        /// <summary>伤害最高的近战武器；没有就用镰刀凑合。</summary>
        private static int BestWeapon()
        {
            int best = -1, bestDmg = -1;
            for (int i = 0; i < Player.Items.Count; i++)
            {
                if (Player.Items[i] is MeleeWeapon w)
                {
                    int dmg = w.isScythe() ? 0 : w.minDamage.Value + w.maxDamage.Value;
                    if (dmg > bestDmg)
                    {
                        best = i;
                        bestDmg = dmg;
                    }
                }
            }
            return best;
        }

        /// <summary>背包里回血最多的食物。</summary>
        private static int BestHealingFood() =>
            Player.Items.Select((it, i) => (it, i))
                .Where(x => x.it is SObject o && o.Edibility > 0 && o.healthRecoveredOnConsumption() > 0)
                .OrderByDescending(x => ((SObject)x.it).healthRecoveredOnConsumption())
                .Select(x => x.i).DefaultIfEmpty(-1).First();

        private static int DirToward(Point from, Point to)
        {
            int dx = to.X - from.X, dy = to.Y - from.Y;
            if (Math.Abs(dx) > Math.Abs(dy))
                return dx > 0 ? 1 : 3;
            return dy < 0 ? 0 : 2;
        }

        /// <summary>
        /// 站在原地朝哪个方向挥能砍到这只怪；砍不到返回 -1。
        /// 用游戏自己的 MeleeWeapon.getAreaOfEffect 算判定区（和真正挥砍时 DoDamage 用的同一个函数）：
        /// 匕首的判定区比剑、锤窄短，有的武器还带“额外范围”（addedAreaOfEffect）往四周撑大；剑挥动时每帧位置不同，取全部帧的并集。
        /// </summary>
        private static int ReachDirection(MeleeWeapon weapon, Monster m)
        {
            // 判定区盖住了也不一定打得到：隔着石头、墙就打不到（游戏的 isMonsterDamageApplicable）
            if (!HasDamageLineOfSight(m))
                return -1;
            var box = m.GetBoundingBox();
            int primary = DirToward(Player.StandingPixel, box.Center);
            int facing = Player.FacingDirection;
            try
            {
                // 先试正对着的方向，再试另一条轴上靠近它的方向（怪在斜角时两边都可能砍得到）
                int dx = box.Center.X - Player.StandingPixel.X, dy = box.Center.Y - Player.StandingPixel.Y;
                int secondary = primary is 1 or 3 ? (dy < 0 ? 0 : 2) : (dx > 0 ? 1 : 3);
                foreach (int dir in new[] { primary, secondary })
                {
                    Player.FacingDirection = dir;
                    var at = Player.GetToolLocation(ignoreClick: true);
                    Vector2 t1 = Vector2.Zero, t2 = Vector2.Zero;
                    for (int frame = 0; frame <= 5; frame++)
                    {
                        var area = weapon.getAreaOfEffect((int)at.X, (int)at.Y, dir, ref t1, ref t2, Player.GetBoundingBox(), frame);
                        if (area.Intersects(box))
                            return dir;
                    }
                }
                return -1;
            }
            catch
            {
                // 算不出来（比如模组武器重写了判定）：退回到按距离估算
                return Vector2.Distance(box.Center.ToVector2(), Player.StandingPixel.ToVector2()) <= 90f ? primary : -1;
            }
            finally
            {
                Player.FacingDirection = facing;
            }
        }

        /// <summary>
        /// 照抄游戏 GameLocation.isMonsterDamageApplicable：怪不贴身（曼哈顿距离 &gt; 1）时，
        /// 从她的格子一格格走到怪的格子（先横后竖、先竖后横两条路，有一条通就行），
        /// 中间有不能穿过的物体（石头等）或挡伤害的地形就打不到。会飞的怪和无视遮挡的怪不受影响。
        /// </summary>
        private static bool HasDamageLineOfSight(Monster m)
        {
            if (m.isGlider.Value || m.ignoreDamageLOS.Value)
                return true;
            Point from = Player.TilePoint, to = m.TilePoint;
            if (Util.Manhattan(from, to) <= 1)
                return true;
            bool Clear(bool horizontalBias)
            {
                int dx = to.X - from.X, dy = to.Y - from.Y, x = from.X, y = from.Y;
                while (dx != 0 || dy != 0)
                {
                    bool stepX = horizontalBias ? Math.Abs(dx) >= Math.Abs(dy) : Math.Abs(dy) < Math.Abs(dx);
                    if (stepX) { x += Math.Sign(dx); dx -= Math.Sign(dx); }
                    else { y += Math.Sign(dy); dy -= Math.Sign(dy); }
                    if ((Loc.objects.TryGetValue(new Vector2(x, y), out var o) && !o.isPassable()) || Loc.BlocksDamageLOS(x, y))
                        return false;
                }
                return true;
            }
            return Clear(true) || Clear(false);
        }

        /// <summary>她和怪之间（怪在 3 格以内）挡着的、紧挨着她的那块能敲碎的石头；没有返回 null。</summary>
        private static Point? BlockingStoneNextToMe(Monster m)
        {
            Point me = Player.TilePoint, to = m.TilePoint;
            if (Util.Manhattan(me, to) > 3)
                return null;
            foreach (var p in new[] { new Point(me.X + Math.Sign(to.X - me.X), me.Y), new Point(me.X, me.Y + Math.Sign(to.Y - me.Y)) })
            {
                if (p == me)
                    continue;
                if (Loc.objects.TryGetValue(new Vector2(p.X, p.Y), out var o) && o.IsBreakableStone())
                    return p;
            }
            return null;
        }

        /// <summary>朝怪物挥一次武器（和左键挥武器同一套流程）。</summary>
        private IEnumerable<object> SwingAt(int weaponIdx, Monster m, int dir)
        {
            for (int i = 0; i < 60 && (Player.UsingTool || Player.FarmerSprite.isUsingWeapon()); i++)
                yield return null;
            if (Player.Items[weaponIdx] is not MeleeWeapon weapon)
                yield break;
            Player.CurrentToolIndex = weaponIdx;
            var center = m.GetBoundingBox().Center;
            Player.faceDirection(dir);
            Player.lastClick = new Vector2(center.X, center.Y);
            Player.completelyStopAnimatingOrDoingAction();
            Player.CanMove = false;
            Player.UsingTool = true;
            Player.canReleaseTool = true;
            weapon.setFarmerAnimating(Player);
            for (int i = 0; i < 60 && (Player.UsingTool || Player.FarmerSprite.isUsingWeapon()); i++)
                yield return null;
        }

        /// <summary>
        /// 打附近的怪。生命低于 minHealth 比例时吃回血最多的食物；没食物就以 "!" 开头失败（调用方应撤退）。
        /// </summary>
        private IEnumerable<object> FightInner(int radius, float minHealth, int maxSeconds, FightStats st, Outcome o)
        {
            int weaponIdx = BestWeapon();
            if (weaponIdx < 0)
            {
                o.Fail("!背包里没有武器（冒险者公会 AdventureGuild 可以买剑，进矿洞第一次也会送一把）");
                yield break;
            }
            int start = Game1.ticks;
            var unreachable = new HashSet<Monster>();
            var everUnreachable = st.Unreachable;
            var waited = new Dictionary<Monster, int>();
            Monster chasing = null;
            int unreachableReset = Game1.ticks;
            var walk = new Outcome();
            var noEffect = new Dictionary<Monster, int>();
            // 连着 8 下砍中也不掉血就别打了（会飞的怪多半只是没砍中，木乃伊倒地时等它起来）
            void NoteSwing(Monster m, int hpBefore)
            {
                if (m.Health < hpBefore || m.isGlider.Value || (m is Mummy mummy && mummy.reviveTimer.Value > 0))
                {
                    noEffect.Remove(m);
                    return;
                }
                noEffect.TryGetValue(m, out int n);
                noEffect[m] = ++n;
                if (n >= 8)
                    st.Immune.Add(m);
            }
            while (Game1.ticks - start < maxSeconds * 60)
            {
                if (Game1.activeClickableMenu != null || Game1.eventUp || Game1.isWarping || Player.health <= 0)
                    break;

                // 先保命
                if (Player.health < Player.maxHealth * minHealth)
                {
                    int food = BestHealingFood();
                    if (food < 0)
                    {
                        o.Fail($"!生命只剩 {Player.health}/{Player.maxHealth}，背包里没有能回血的食物，赶紧撤退");
                        yield break;
                    }
                    foreach (var s in Eat(food.ToString()))
                        if (s is not ActionResult)
                            yield return s;
                    st.Eaten++;
                    continue;
                }

                if (Game1.ticks - unreachableReset > 300)
                {
                    unreachable.Clear();
                    unreachableReset = Game1.ticks;
                }
                var me = Player.TilePoint;
                // 会加血的怪（暗影萨满）先打：不然它一直给自己和别的怪回血。打残了它会逃跑，所以盯上以后追出 radius 也要追到死
                if (chasing != null && (chasing.Health <= 0 || !Loc.characters.Contains(chasing) || unreachable.Contains(chasing)
                                        || Util.Manhattan(chasing.TilePoint, me) > radius + 10))
                    chasing = null;
                var target = chasing ?? LiveMonsters(Loc)
                    .Where(m => !unreachable.Contains(m) && !st.Immune.Contains(m) && Util.Manhattan(m.TilePoint, me) <= radius)
                    .OrderBy(m => IsHealer(m) ? 0 : ShellClosed(m) ? 2 : 1) // 缩壳的岩石蟹不动也不打人，放到最后
                    .ThenBy(m => Vector2.Distance(m.GetBoundingBox().Center.ToVector2(), Player.StandingPixel.ToVector2()))
                    .FirstOrDefault();
                if (target == null)
                    break;
                if (IsHealer(target))
                    chasing = target;

                if (CannotHurt(target))
                {
                    st.Immune.Add(target);
                    continue;
                }

                bool shellClosed = ShellClosed(target);
                if (shellClosed && Util.Manhattan(target.TilePoint, me) == 1)
                {
                    foreach (var s in UseToolOnce(Util.FindTool<Pickaxe>(), target.TilePoint, walk))
                        yield return s;
                    st.Swings++;
                    // 体力见底敲不动了
                    if (!walk.Ok)
                        st.Immune.Add(target);
                    walk.Succeed();
                    continue;
                }

                int swingDir = !shellClosed && Player.Items[weaponIdx] is MeleeWeapon held ? ReachDirection(held, target) : -1;
                if (swingDir >= 0)
                {
                    int hpBefore = target.Health;
                    foreach (var s in SwingAt(weaponIdx, target, swingDir))
                        yield return s;
                    st.Swings++;
                    if (target.Health <= 0 || !Loc.characters.Contains(target))
                        st.Kills++;
                    else
                        NoteSwing(target, hpBefore);
                    continue;
                }

                // 靠近：只走几步就重新判断，怪会动
                var goals = Pathfinder.AdjacentStandTiles(Loc, target.TilePoint);
                // 敲壳要站在旁边，不能走到蟹那一格上
                if (!shellClosed && Pathfinder.IsPassable(Loc, target.TilePoint.X, target.TilePoint.Y))
                    goals.Add(target.TilePoint);
                var path = goals.Count > 0 ? Pathfinder.FindPath(Loc, me, goals, 8000) : null;
                if (!shellClosed && (path == null || path.Count < 2) && Util.Manhattan(target.TilePoint, me) <= 1)
                {
                    // 已经贴着它了，判定区却没算到（碰撞框偏一点）：没地方再靠近，直接朝它挥
                    int hpBefore = target.Health;
                    foreach (var s in SwingAt(weaponIdx, target, DirToward(Player.StandingPixel, target.GetBoundingBox().Center)))
                        yield return s;
                    st.Swings++;
                    if (target.Health <= 0 || !Loc.characters.Contains(target))
                        st.Kills++;
                    else
                        NoteSwing(target, hpBefore);
                    continue;
                }
                if ((path == null || path.Count < 2) && InDungeon)
                {
                    // 被杂草、木桶、石头挡住：穿过去的路线上第一个障碍就在身边就清掉它（挥一两下就回来重新判断），
                    // 不在身边就先走到它跟前
                    var clearGoals = StandOrClearTiles(target.TilePoint);
                    var clearPath = clearGoals.Count > 0 ? Pathfinder.FindPath(Loc, me, clearGoals, 8000, clearable: ClearableObstacle) : null;
                    int block = clearPath?.FindIndex(1, p => ClearableObstacle(p)) ?? -1;
                    if (block == 1)
                    {
                        foreach (var s in ClearObstacle(clearPath[1], walk, maxHits: 2, stopOnThreat: false))
                            yield return s;
                        walk.Succeed();
                        continue;
                    }
                    if (block > 1)
                    {
                        foreach (var s in WalkPath(clearPath.Take(Math.Min(block, 4)).ToList(), walk))
                            yield return s;
                        walk.Succeed();
                        continue;
                    }
                }
                if (path == null || path.Count < 2)
                {
                    // 绕不过去、而且挡在中间的就是她身边的一块石头：用镐子敲掉再打（隔着石头挥剑是打不到的）
                    if (BlockingStoneNextToMe(target) is Point stone && Util.FindTool<Pickaxe>() is int pick && pick >= 0)
                    {
                        for (int k = 0; k < 6 && Loc.objects.ContainsKey(new Vector2(stone.X, stone.Y)); k++)
                        {
                            foreach (var s in UseToolOnce(pick, stone, walk))
                                yield return s;
                            if (!walk.Ok)
                                break;
                        }
                        walk.Succeed();
                        if (!Loc.objects.ContainsKey(new Vector2(stone.X, stone.Y)))
                            continue;
                    }
                    // 会飞的怪（蝙蝠等）过一会儿自己会飞过来；地上的怪离得近也多半在朝我们走（被石头挡住的路它可能绕得过来），原地等它几秒
                    waited.TryGetValue(target, out int w);
                    if (target.isGlider.Value || (Util.Manhattan(target.TilePoint, me) <= 5 && w < 240))
                    {
                        waited[target] = w + 15;
                        foreach (var s in WaitTicks(15))
                            yield return s;
                    }
                    else
                    {
                        unreachable.Add(target);
                        everUnreachable.Add(target);
                    }
                    continue;
                }
                foreach (var s in WalkPath(path.Take(Math.Min(path.Count, 4)).ToList(), walk))
                    yield return s;
            }
            o.Succeed();
        }

        public IEnumerable<object> Fight(int radius, int minHealthPercent)
        {
            if (Blocked(out string why))
            {
                yield return ActionResult.Fail(why);
                yield break;
            }
            if (!LiveMonsters(Loc).Any())
            {
                yield return ActionResult.Success("附近没有怪物");
                yield break;
            }
            var st = new FightStats();
            var o = new Outcome();
            foreach (var s in FightInner(Math.Clamp(radius, 2, 40), Math.Clamp(minHealthPercent, 10, 90) / 100f, 120, st, o))
                yield return s;
            int left = LiveMonsters(Loc).Count(m => Util.Manhattan(m.TilePoint, Player.TilePoint) <= radius);
            string msg = $"挥了 {st.Swings} 下，打倒 {st.Kills} 只，吃了 {st.Eaten} 次东西，生命 {Player.health}/{Player.maxHealth}，附近还剩 {left} 只";
            var blocked = st.Unreachable.Where(m => m.Health > 0 && Loc.characters.Contains(m)).ToList();
            var immune = st.Immune.Where(m => m.Health > 0 && Loc.characters.Contains(m)).ToList();
            KnownUnreachable = new HashSet<Monster>(blocked.Concat(immune));
            if (blocked.Count > 0)
                msg += $"。够不着：{string.Join("、", blocked.Take(4).Select(m => $"{m.displayName}({m.TilePoint.X},{m.TilePoint.Y})"))}"
                     + " 和你之间没有能走的路（被石头或墙隔开）。它过不来就不用管，继续干别的；挡路的话用 use_tool 拿镐子敲掉中间的石头再 fight";
            if (immune.Count > 0)
                msg += $"。打不动：{string.Join("、", immune.Take(4).Select(m => $"{m.displayName}({m.TilePoint.X},{m.TilePoint.Y})"))}"
                     + "（缩壳的岩石蟹要用镐子敲，没镐子或没体力时打不了；装甲虫要带除虫附魔的武器）。先绕开它，继续干别的";
            yield return o.Ok ? ActionResult.Success(msg) : ActionResult.Fail(o.Message.TrimStart('!') + "。" + msg);
        }

        // ───────────────────────── 下矿洞 ─────────────────────────

        /// <summary>
        /// 矿洞里还没拿空的宝箱（每 10 层的奖励箱、宝藏房的箱子）。它们放在 overlayObjects 里，
        /// 但 GameLocation.objects 是叠加了 overlayObjects 的字典，所以从 objects 里就能找到。
        /// </summary>
        private static IEnumerable<(Point tile, Chest chest)> MineChests(MineShaft mine) =>
            mine.objects.Pairs
                .Where(p => p.Value is Chest c && !c.playerChest.Value && c.GetItemsForPlayer().Count > 0)
                .Select(p => (new Point((int)p.Key.X, (int)p.Key.Y), (Chest)p.Value))
                .OrderBy(p => Util.Manhattan(p.Item1, Player.TilePoint));

        /// <summary>
        /// 开矿洞宝箱：和玩家点击一样走 checkAction。第一下掀开盖子，之后每点一下拿出一样东西（举过头顶展示，
        /// 背包满时弹出拿取界面）。拿不了（走不到、背包满）时通过 note 说明，调用方就不再反复尝试这个箱子。
        /// </summary>
        private IEnumerable<object> OpenMineChest(MineShaft mine, Point tile, List<string> took, Action<string> note)
        {
            var o = new Outcome();
            foreach (var s in GoAdjacent(tile, o))
                yield return s;
            if (!o.Ok)
            {
                note($"({tile.X},{tile.Y}) 有个宝箱走不过去（{o.Message}），可以敲开周围的石头后用 interact 打开");
                yield break;
            }
            Player.faceDirection(DirToward(Player.TilePoint, tile));
            for (int click = 0; click < 12; click++)
            {
                if (!(mine.objects.TryGetValue(new Vector2(tile.X, tile.Y), out var obj) && obj is Chest chest) || chest.GetItemsForPlayer().Count == 0)
                    yield break;
                var before = chest.GetItemsForPlayer().Where(i => i != null).Select(i => i.DisplayName).ToList();
                mine.checkAction(new xLocation(tile.X, tile.Y), Game1.viewport, Player);
                // 掀盖子、举起物品的动画
                for (int t = 0; t < 90 && (Player.freezePause > 0 || Player.isEmoting || Player.FarmerSprite.PauseForSingleAnimation); t++)
                    yield return null;
                foreach (var s in WaitTicks(30))
                    yield return s;
                var after = chest.GetItemsForPlayer().Where(i => i != null).Select(i => i.DisplayName).ToList();
                foreach (var name in after)
                    before.Remove(name);
                if (Game1.activeClickableMenu is ItemGrabMenu)
                {
                    // 背包满了，游戏弹出了拿取界面：先关掉，交给 AI 腾地方
                    Game1.exitActiveMenu();
                    note($"({tile.X},{tile.Y}) 的宝箱里还有 {string.Join("、", after)}，背包满了拿不下：腾出位置后 interact 这个箱子");
                    yield break;
                }
                took.AddRange(before);
                if (Game1.activeClickableMenu is DialogueBox)
                    Game1.exitActiveMenu();
            }
        }

        /// <summary>矿洞里的矿石巨石（2x2，任何镐子都能敲），按离她的距离排序；corner 是左上角。</summary>
        private static IEnumerable<(Point corner, ResourceClump clump)> MineBoulders(MineShaft mine) =>
            mine.resourceClumps
                .Where(c => c.parentSheetIndex.Value is 752 or 754 or 756 or 758)
                .Select(c => (new Point((int)c.Tile.X, (int)c.Tile.Y), c))
                .OrderBy(b => Util.Manhattan(b.Item1, Player.TilePoint));

        private static IEnumerable<Point> BoulderTiles(Point corner) =>
            new[] { corner, new Point(corner.X + 1, corner.Y), new Point(corner.X, corner.Y + 1), new Point(corner.X + 1, corner.Y + 1) };

        /// <summary>走到这几格中任意一格的旁边（巨石占 2x2，哪一面能站就从哪一面敲）。</summary>
        private IEnumerable<object> GoAdjacentToAny(IEnumerable<Point> tiles, Outcome o)
        {
            var set = tiles.ToHashSet();
            var me = Player.TilePoint;
            if (set.Any(t => Util.Manhattan(t, me) == 1))
            {
                o.Succeed();
                yield break;
            }
            var stands = set.SelectMany(t => Pathfinder.AdjacentStandTiles(Loc, t)).Where(p => !set.Contains(p)).Distinct().ToList();
            var path = stands.Count > 0 ? Pathfinder.FindPath(Loc, me, stands, 8000) : null;
            if (path == null && InDungeon)
            {
                var around = set.SelectMany(StandOrClearTiles).Where(p => !set.Contains(p)).Distinct().ToList();
                foreach (var s in GoToAnyClearing(around, o, "巨石旁边"))
                    yield return s;
                yield break;
            }
            if (path == null)
            {
                o.Fail("走不到巨石旁边");
                yield break;
            }
            foreach (var s in WalkPath(path, o))
                yield return s;
        }

        private static Point? NearestMineTile(GameLocation loc, int tileIndex)
        {
            Point? best = null;
            int bestD = int.MaxValue;
            var me = Player.TilePoint;
            int w = Pathfinder.MapWidth(loc), h = Pathfinder.MapHeight(loc);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (loc.getTileIndexAt(x, y, "Buildings", "mine") == tileIndex)
                    {
                        int d = Util.Manhattan(me, new Point(x, y));
                        if (d < bestD)
                        {
                            bestD = d;
                            best = new Point(x, y);
                        }
                    }
            return best;
        }

        /// <summary>
        /// 自动下矿：打附近的怪、敲石头找楼梯、下楼，直到下了 levels 层或者体力/生命/时间不够。
        /// 在矿洞入口（Mine）时会先下到第 1 层，或者用电梯到 start_level（必须是 5 的倍数且到过）。
        /// </summary>
        public IEnumerable<object> ExploreMine(int levels, int? startLevel)
        {
            var o = new Outcome();
            if (Loc is not MineShaft)
            {
                if (!Loc.Name.Equals("Mine", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var s in TravelInner("Mine", o))
                        yield return s;
                    if (!o.Ok)
                    {
                        yield return ActionResult.Fail("去矿洞入口失败（春季第5天后才开放）：" + o.Message);
                        yield break;
                    }
                }
                int target = startLevel ?? 1;
                if (target > 1 && (target % 5 != 0 || target > MineShaft.lowestLevelReached))
                {
                    yield return ActionResult.Fail($"电梯只能到 5 的倍数层，而且要到过（你最深到过第 {MineShaft.lowestLevelReached} 层）");
                    yield break;
                }
                var entry = FindTileAction(Loc, target > 1 ? "MineElevator" : "Mine");
                if (entry.HasValue)
                {
                    foreach (var s in GoAdjacent(entry.Value, o))
                        yield return s;
                }
                if (target > 1)
                    Game1.enterMine(target); // 等同在电梯界面选了这一层
                else if (entry.HasValue)
                    Loc.checkAction(new xLocation(entry.Value.X, entry.Value.Y), Game1.viewport, Player);
                else
                    Game1.enterMine(1);
                for (int t = 0; t < 600 && Loc is not MineShaft; t++)
                    yield return null;
                foreach (var s in WaitSceneSettled())
                    yield return s;
                if (Loc is not MineShaft)
                {
                    yield return ActionResult.Fail("没能进入矿洞");
                    yield break;
                }
            }

            levels = Math.Clamp(levels, 1, 20);
            int startLvl = ((MineShaft)Loc).mineLevel;
            int descended = 0, rocks = 0;
            var st = new FightStats();
            string stop = null;
            int pick = Util.FindTool<Pickaxe>();
            if (pick < 0)
            {
                yield return ActionResult.Fail("没有镐子，没法在矿洞里敲石头");
                yield break;
            }

            int guard = 0;
            int lastHp = Player.health;
            var loot = new List<string>();
            var chestNotes = new List<string>();
            var skippedChests = new HashSet<Point>();
            var skippedBoulders = new HashSet<Point>();
            int boulders = 0;
            while (descended < levels && guard++ < 400)
            {
                if (Loc is not MineShaft mine)
                {
                    stop = "离开了矿洞";
                    break;
                }
                if (Game1.isWarping || Game1.fadeToBlack || Game1.globalFade)
                {
                    // 下楼梯/进矿洞的淡入淡出还没结束，等它结束再继续，不算停下
                    foreach (var s in WaitSceneSettled())
                        yield return s;
                    continue;
                }
                if (Blocked(out string why))
                {
                    // 宝箱、对话等：交给 AI 处理
                    stop = why;
                    break;
                }
                if (Player.Stamina < 15)
                {
                    stop = "体力不够了";
                    break;
                }
                if (Game1.timeOfDay >= 2300)
                {
                    stop = "已经 23 点了，该回家睡觉";
                    break;
                }

                // 1) 清理靠近的怪（先保命再干活）。上一轮挨了打、身边却没有怪：多半是骷髅扔骨头、暗影萨满放法术这种远程怪，
                //    只打 6 格以内永远碰不到它，会一直挨打、吃东西、敲不成石头（夏21 第 75、83 层就这样耗了几分钟），所以扩大范围去找它
                bool sniped = Player.health < lastHp && NearestThreat(2) == null;
                // 附近有萨满时多给点时间：它会回血、会逃，20 秒常常打不死
                bool healerNear = LiveMonsters(mine).Any(m => IsHealer(m) && Util.Manhattan(m.TilePoint, Player.TilePoint) <= 10);
                int swingsBefore = st.Swings;
                foreach (var s in FightInner(sniped || healerNear ? 16 : 6, 0.4f, sniped || healerNear ? 45 : 20, st, o))
                    yield return s;
                // 扩大范围也一下没打着（远程怪隔着墙/岩浆，走不过去）：这一轮敲石头时不再因为远处的伤害停手，否则会原地打转
                bool shooterUnreachable = sniped && st.Swings == swingsBefore;
                lastHp = Player.health;
                if (!o.Ok && o.Message.StartsWith("!"))
                {
                    stop = o.Message.Substring(1);
                    break;
                }
                o.Succeed();

                // 2) 有宝箱先开（每 10 层的奖励箱、宝藏房），再找楼梯
                if (MineChests(mine).Where(c => !skippedChests.Contains(c.tile)).Select(c => (Point?)c.tile).FirstOrDefault() is Point chestTile)
                {
                    var took = new List<string>();
                    string chestNote = null;
                    foreach (var s in OpenMineChest(mine, chestTile, took, n => chestNote = n))
                        yield return s;
                    loot.AddRange(took);
                    // 点了也没拿到东西（打不开）：记下来，不再反复试
                    if (chestNote == null && took.Count == 0 && MineChests(mine).Any(c => c.tile == chestTile))
                        chestNote = $"({chestTile.X},{chestTile.Y}) 的宝箱没打开，可以试试 interact";
                    if (chestNote != null)
                    {
                        skippedChests.Add(chestTile);
                        chestNotes.Add(chestNote);
                    }
                    continue;
                }

                // 3) 有楼梯就下
                var ladder = NearestMineTile(mine, 173);
                if (ladder.HasValue)
                {
                    foreach (var s in GoAdjacent(ladder.Value, o))
                        yield return s;
                    if (o.Ok)
                    {
                        mine.checkAction(new xLocation(ladder.Value.X, ladder.Value.Y), Game1.viewport, Player);
                        for (int t = 0; t < 300 && Loc == mine; t++)
                            yield return null;
                        foreach (var s in WaitTicks(30))
                            yield return s;
                        if (Loc != mine)
                        {
                            descended++;
                            continue;
                        }
                    }
                    o.Succeed();
                }

                // 3) 敲最近的石头找楼梯
                var stones = mine.objects.Pairs.Where(p => p.Value.IsBreakableStone())
                    .Select(p => new Point((int)p.Key.X, (int)p.Key.Y)).ToList();
                bool hit = false;
                foreach (var stone in stones.OrderBy(p => Util.Manhattan(p, Player.TilePoint)).Take(6))
                {
                    foreach (var s in GoAdjacent(stone, o))
                        yield return s;
                    if (!o.Ok)
                    {
                        o.Succeed();
                        continue;
                    }
                    int hpBefore = Player.health;
                    for (int k = 0; k < 8 && mine.objects.ContainsKey(new Vector2(stone.X, stone.Y)); k++)
                    {
                        // 被打了（包括远处飞来的骨头、法术）或者有怪贴上来：先停手，回到循环开头去打它。
                        // 只有刚才确认打不到那只远程怪时，远处的伤害才不打断（生命低于四成照样停下吃东西）
                        bool hurt = Player.health < hpBefore && !shooterUnreachable;
                        if (hurt || NearestThreat(2) != null || Player.health < Player.maxHealth * 0.4f)
                            break;
                        foreach (var s in UseToolOnce(pick, stone, o))
                            yield return s;
                        if (!o.Ok)
                            break;
                    }
                    if (!mine.objects.ContainsKey(new Vector2(stone.X, stone.Y)))
                        rocks++;
                    hit = true;
                    break;
                }
                if (!o.Ok)
                {
                    stop = o.Message;
                    break;
                }
                if (hit)
                    continue;

                // 4) 小石头都够不着：多半是矿石巨石（2x2）挡了路，任何镐子都能敲碎（游戏里 752/754/756/758 只要求是镐子）
                bool smashed = false;
                foreach (var (corner, clump) in MineBoulders(mine).Take(3))
                {
                    if (skippedBoulders.Contains(corner))
                        continue;
                    var target = BoulderTiles(corner).OrderBy(p => Util.Manhattan(p, Player.TilePoint)).First();
                    foreach (var s in GoAdjacentToAny(BoulderTiles(corner), o))
                        yield return s;
                    if (!o.Ok)
                    {
                        o.Succeed();
                        skippedBoulders.Add(corner);
                        continue;
                    }
                    target = BoulderTiles(corner).OrderBy(p => Util.Manhattan(p, Player.TilePoint)).First();
                    for (int k = 0; k < 25 && mine.resourceClumps.Contains(clump); k++)
                    {
                        if (NearestThreat(2) != null || Player.health < Player.maxHealth * 0.4f || Player.Stamina < 15)
                            break;
                        foreach (var s in UseToolOnce(pick, target, o))
                            yield return s;
                        if (!o.Ok)
                            break;
                    }
                    o.Succeed();
                    if (!mine.resourceClumps.Contains(clump))
                    {
                        boulders++;
                        smashed = true;
                    }
                    else if (NearestThreat(2) == null && Player.health >= Player.maxHealth * 0.4f && Player.Stamina >= 15)
                        skippedBoulders.Add(corner); // 敲了 25 下还没碎：别卡在这块上
                    break;
                }
                if (smashed || (MineBoulders(mine).Any(b => !skippedBoulders.Contains(b.corner)) && Player.Stamina >= 15))
                    continue;

                // 5) 没石头可敲：有些层要把怪全打完楼梯才出现
                if (LiveMonsters(mine).Any())
                {
                    foreach (var s in FightInner(60, 0.4f, 60, st, o))
                        yield return s;
                    if (!o.Ok && o.Message.StartsWith("!"))
                    {
                        stop = o.Message.Substring(1);
                        break;
                    }
                    o.Succeed();
                    if (NearestMineTile(mine, 173).HasValue)
                        continue;
                }
                stop = "这一层找不到楼梯，也没有能走到的石头了（可以试试 use_tool 敲别处，或者 interact 竖井/出口）";
                break;
            }

            int now = Loc is MineShaft m2 ? m2.mineLevel : 0;
            string msg = $"从第 {startLvl} 层下到第 {(now > 0 ? now.ToString() : "?")} 层（下了 {descended} 层），敲了 {rocks} 块石头，打倒 {st.Kills} 只怪，吃了 {st.Eaten} 次东西；生命 {Player.health}/{Player.maxHealth}，体力 {(int)Player.Stamina}";
            if (boulders > 0)
                msg += $"；敲碎了 {boulders} 块挡路的矿石巨石";
            if (loot.Count > 0)
                msg += "；开宝箱拿到：" + string.Join("、", loot);
            if (chestNotes.Count > 0)
                msg += "；" + string.Join("；", chestNotes);
            if (stop != null)
                msg += "；停下原因：" + stop;
            yield return descended > 0 || rocks > 0 || st.Kills > 0 || loot.Count > 0 ? ActionResult.Success(msg) : ActionResult.Fail(msg);
        }
    }
}
