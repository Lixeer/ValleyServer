using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace BlindWhaleFarmer
{
    internal sealed partial class GameActions
    {
        // ───────────────────────── 花钱建议 ─────────────────────────

        /// <summary>背包 + 农场所有箱子里某种物品的总数（判断材料够不够）。</summary>
        private static int CountEverywhere(string qualifiedId)
        {
            int n = Player.Items.Where(i => i?.QualifiedItemId == qualifiedId).Sum(i => i.Stack);
            if (Game1.getFarm() is { } farm)
                foreach (var (_, chest) in PlayerChests(farm))
                    n += chest.GetItemsForPlayer().Where(i => i?.QualifiedItemId == qualifiedId).Sum(i => i.Stack);
            return n;
        }

        /// <summary>
        /// 根据现在的钱、背包、工具、建筑，列出“现在买得起、还没买、值得买”的大件，供 get_state 显示。
        /// 价格都来自游戏：背包 2000/10000（GameLocation 买背包）、房子 10000g+450 木材 / 65000g+100 硬木（GameLocation 升级房子）、
        /// 工具 2000/5000/10000/25000 + 5 根对应金属锭（ShopBuilder.GetToolUpgradeConventionalPrice）、建筑（Data/Buildings）。
        /// 钱少于 2000 不显示。
        /// </summary>
        public static string SpendingAdvice()
        {
            int money = Player.Money;
            if (money < 2000)
                return null;
            var tips = new List<string>();

            if (Player.MaxItems < 24)
                tips.Add("背包扩到 24 格：2000g，皮埃尔杂货店（SeedShop）柜台上的背包，interact 它");
            else if (Player.MaxItems < 36 && money >= 10000)
                tips.Add("背包扩到 36 格：10000g，皮埃尔杂货店柜台上的背包");

            // 工具：只提示材料已经够的那一级（锭在背包或农场箱子里），升级要在铁匠铺等 2 天
            var bars = new[] { ("(O)334", "铜锭"), ("(O)335", "铁锭"), ("(O)336", "金锭"), ("(O)337", "铱锭") };
            int[] prices = { 2000, 5000, 10000, 25000 };
            foreach (var (tool, label) in new (Tool, string)[]
                     {
                         (Player.Items.OfType<WateringCan>().FirstOrDefault(), "水壶（浇得更多）"),
                         (Player.Items.OfType<Pickaxe>().FirstOrDefault(), "镐子（矿洞敲得快，钢镐能敲农场大石头）"),
                         (Player.Items.OfType<Axe>().FirstOrDefault(), "斧头（铜斧砍大树桩、钢斧砍大原木，出硬木）"),
                         (Player.Items.OfType<Hoe>().FirstOrDefault(), "锄头"),
                     })
            {
                if (tool == null || tool.UpgradeLevel >= 4)
                    continue;
                int next = tool.UpgradeLevel;
                var (barId, barName) = bars[next];
                if (money >= prices[next] && CountEverywhere(barId) >= 5)
                    tips.Add($"升级{label}：{prices[next]}g + 5 {barName}，upgrade_tool（克林特铁匠铺，要等 2 天，期间不能用这把工具）");
            }

            int wood = CountEverywhere("(O)388"), stone = CountEverywhere("(O)390"), hardwood = CountEverywhere("(O)709");
            if (Player.HouseUpgradeLevel == 0 && money >= 10000)
                tips.Add($"升级房子：10000g + 450 木材（你有 {wood}），upgrade_house（有厨房能 cook 做菜，回体力的料理更便宜）");
            else if (Player.HouseUpgradeLevel == 1 && money >= 65000)
                tips.Add($"再升级房子：65000g + 100 硬木（你有 {hardwood}）");

            var farm = Game1.getFarm();
            bool Has(string type) => farm?.buildings.Any(b => b.buildingType.Value == type) == true;
            if (farm != null)
            {
                if (!Has("Coop") && !Has("Big Coop") && !Has("Deluxe Coop") && money >= 4000)
                    tips.Add($"盖鸡舍：4000g + 300 木材 + 100 石头（你有 {wood}/{stone}），build Coop，再 buy_animal 买鸡，每天下蛋");
                if (!Has("Silo") && money >= 100 && Has("Coop"))
                    tips.Add("盖筒仓：100g + 100 石头 + 10 粘土 + 5 铜锭，build Silo（割草自动存干草，冬天喂动物不用买）");
                if (!Has("Barn") && !Has("Big Barn") && !Has("Deluxe Barn") && money >= 6000 && Has("Coop"))
                    tips.Add($"盖畜棚：6000g + 350 木材 + 150 石头（你有 {wood}/{stone}），build Barn，养牛产奶");
            }

            if (Player.FishingLevel >= 6 && money >= 7500 && !Player.Items.OfType<FishingRod>().Any(r => RodScore(r) >= 3))
                tips.Add("买铱金鱼竿：7500g，鱼店（FishShop），能装鱼饵和钓具，钓鱼收入明显变高");
            else if (Player.FishingLevel >= 2 && money >= 1800 && !Player.Items.OfType<FishingRod>().Any(r => RodScore(r) >= 2))
                tips.Add("买玻璃纤维棒：1800g，鱼店（FishShop），能装鱼饵，咬钩快一倍");

            if (tips.Count == 0)
                return null;
            return "【花钱建议】钱放着不会变多：先把能提高每天收入或省时间的买了，留 1000g 左右买种子就行。现在买得起、还没买的：\n- "
                   + string.Join("\n- ", tips);
        }
    }
}
