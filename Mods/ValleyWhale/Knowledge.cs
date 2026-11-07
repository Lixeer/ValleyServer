using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using SObject = StardewValley.Object;

namespace BlindWhaleFarmer
{
    /// <summary>
    /// 游戏数据里的“知识”：村民送礼喜好、社区中心收集包、博物馆捐赠。
    /// 观察（Observer）和动作（GameActions）共用这里的解析。
    /// </summary>
    internal static class Knowledge
    {
        // ───────────────────────── 节日 ─────────────────────────

        internal sealed class FestivalInfo
        {
            public string Name;
            public string Location;
            public int Start;
            public int End;

            /// <summary>现在走进节日地图会不会直接开始节日（和 Game1.warpFarmer 里的判断一样：开始时间 ≤ 现在 ≤ 结束时间）。</summary>
            public bool EnteringStartsFestival => Game1.timeOfDay >= Start && Game1.timeOfDay <= End;

            public override string ToString() => $"{Name}（{Location}，{Util.TimeString(Start)}-{Util.TimeString(End)}）";
        }

        /// <summary>今天的节日。和 Game1 一样读 Data\Festivals\&lt;季节&gt;&lt;日&gt; 的 conditions，例如 "Town/900 1400"。</summary>
        public static FestivalInfo TodaysFestival()
        {
            if (!Utility.isFestivalDay())
                return null;
            try
            {
                var data = Game1.temporaryContent.Load<Dictionary<string, string>>($"Data\\Festivals\\{Game1.currentSeason}{Game1.dayOfMonth}");
                string[] cond = data["conditions"].Split('/');
                string[] hours = ArgUtility.SplitBySpace(cond[1]);
                return new FestivalInfo
                {
                    Name = data.TryGetValue("name", out var n) ? n : "节日",
                    Location = cond[0],
                    Start = Convert.ToInt32(hours[0]),
                    End = Convert.ToInt32(hours[1])
                };
            }
            catch
            {
                return null;
            }
        }

        // ───────────────────────── 送礼 ─────────────────────────

        public static string TasteName(int taste) => taste switch
        {
            NPC.gift_taste_love or NPC.gift_taste_stardroptea => "最爱",
            NPC.gift_taste_like => "喜欢",
            NPC.gift_taste_dislike => "不喜欢",
            NPC.gift_taste_hate => "讨厌",
            _ => "一般"
        };

        public static bool IsGiftable(Item item) => item is SObject && item.canBeGivenAsGift();

        /// <summary>背包里每件可送的物品对这个村民来说是什么喜好（同一物品只列一次）。</summary>
        public static Dictionary<int, List<string>> InventoryTastes(NPC npc)
        {
            var result = new Dictionary<int, List<string>>();
            var seen = new HashSet<string>();
            foreach (var item in Game1.player.Items)
            {
                if (!IsGiftable(item) || !seen.Add(item.QualifiedItemId + "@" + item.Quality))
                    continue;
                int taste = npc.getGiftTasteForThisItem(item);
                if (taste == NPC.gift_taste_stardroptea)
                    taste = NPC.gift_taste_love;
                if (!result.TryGetValue(taste, out var list))
                    result[taste] = list = new List<string>();
                list.Add(item.DisplayName);
            }
            return result;
        }

        /// <summary>从 Data/NPCGiftTastes 读这个村民的最爱/喜欢/讨厌清单（不含全体通用喜好）。</summary>
        public static (List<string> love, List<string> like, List<string> hate) GiftTasteLists(string npcName)
        {
            var love = new List<string>();
            var like = new List<string>();
            var hate = new List<string>();
            if (Game1.NPCGiftTastes != null && Game1.NPCGiftTastes.TryGetValue(npcName, out string raw))
            {
                // 格式：最爱台词/最爱ID/喜欢台词/喜欢ID/不喜欢台词/ID/讨厌台词/ID/一般台词/ID
                var parts = raw.Split('/');
                love = ResolveIds(parts.ElementAtOrDefault(1));
                like = ResolveIds(parts.ElementAtOrDefault(3));
                hate = ResolveIds(parts.ElementAtOrDefault(7));
            }
            return (love, like, hate);
        }

        private static List<string> ResolveIds(string ids)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(ids))
                return list;
            foreach (string id in ids.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    if (int.TryParse(id, out int n) && n < 0)
                        list.Add("[类别]" + SObject.GetCategoryDisplayName(n));
                    else
                    {
                        var data = ItemRegistry.GetData(id.StartsWith("(") ? id : "(O)" + id);
                        if (data != null)
                            list.Add(data.DisplayName);
                    }
                }
                catch
                {
                    // 个别 ID 可能是上下文标签，跳过
                }
            }
            return list;
        }

        // ───────────────────────── 社区中心收集包 ─────────────────────────

        internal sealed class BundleInfo
        {
            public string Key;          // 例如 Pantry/0
            public string AreaName;     // 英文区域名
            public int Area;
            public int Index;
            public string DisplayName;
            public string Reward;
            public int SlotsNeeded;
            public int SlotsDone;
            public List<BundleIngredientDescription> Ingredients = new();
            public bool Complete => SlotsDone >= SlotsNeeded;
        }

        public static string AreaDisplayName(int area) => area switch
        {
            0 => "茶水间(Pantry)",
            1 => "工艺室(Crafts Room)",
            2 => "鱼缸(Fish Tank)",
            3 => "锅炉房(Boiler Room)",
            4 => "金库(Vault)",
            5 => "公告栏(Bulletin Board)",
            6 => "废弃的 Joja 超市",
            _ => "区域" + area
        };

        internal const string FarmhandCommunityCenterNote =
            "社区中心还没开放：开门的剧情只有主机能触发（第 5 天起的晴天 8:00-13:00，主机从巴士站走进小镇）。你怎么走都触发不了，门也打不开，别在这上面花时间：用 say 跟主机说一声，然后接着干别的";

        /// <summary>社区中心当前状态：不可用时返回原因，否则返回 null。</summary>
        public static string CommunityCenterUnavailable()
        {
            // 开门和路线都记在主机身上（游戏 WarpCommunityCenter 看的也是 MasterPlayer），联机时农场工人自己的记录里没有
            var master = Game1.MasterPlayer;
            if (master.mailReceived.Contains("JojaMember"))
                return "已经选了 Joja 路线，社区中心收集包不再可用";
            if (!master.mailReceived.Contains("ccDoorUnlock") && !Game1.IsMasterGame)
                return FarmhandCommunityCenterNote;
            if (!master.mailReceived.Contains("ccDoorUnlock"))
                // 事件 611439 的条件：j 4（玩满 4 天，即第 5 天起）/ t 800 1300 / w sunny / a 0 54（从巴士站那边进镇的落脚点）/ H（只有主机）
                return "社区中心还没开放：第 5 天起的晴天 8:00-13:00，从巴士站（BusStop）走进小镇（Town）就会触发镇长带你去社区中心的剧情"
                    + "（从农场 travel_to Town 正好经过巴士站；从海滩、森林等其他方向进镇不会触发）";
            if (master.hasCompletedCommunityCenter())
                return "社区中心已经全部修复完成";
            return null;
        }

        public static List<BundleInfo> Bundles()
        {
            var list = new List<BundleInfo>();
            var cc = Game1.getLocationFromName("CommunityCenter") as CommunityCenter;
            if (cc == null)
                return list;
            foreach (var pair in Game1.netWorldState.Value.BundleData)
            {
                var keyParts = pair.Key.Split('/');
                if (keyParts.Length < 2 || !int.TryParse(keyParts[1], out int index))
                    continue;
                var f = pair.Value.Split('/');
                if (f.Length < 3)
                    continue;
                var info = new BundleInfo
                {
                    Key = pair.Key,
                    AreaName = keyParts[0],
                    Area = CommunityCenter.getAreaNumberFromName(keyParts[0]),
                    Index = index,
                    DisplayName = f.Length > 6 && !string.IsNullOrWhiteSpace(f[6]) ? f[6] : f[0],
                    Reward = f[1]
                };
                cc.bundles.TryGetValue(index, out bool[] done);
                var raw = f[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i + 2 < raw.Length; i += 3)
                {
                    bool completed = done != null && i / 3 < done.Length && done[i / 3];
                    info.Ingredients.Add(new BundleIngredientDescription(raw[i], int.Parse(raw[i + 1]), int.Parse(raw[i + 2]), completed));
                    if (completed)
                        info.SlotsDone++;
                }
                info.SlotsNeeded = f.Length > 4 && int.TryParse(f[4], out int n) ? n : info.Ingredients.Count;
                list.Add(info);
            }
            return list.OrderBy(b => b.Area).ThenBy(b => b.Index).ToList();
        }

        /// <summary>收集包当前能不能看到/提交（对应区域的祝尼魔便条已经出现，且区域没完成）。</summary>
        public static bool IsBundleAvailable(BundleInfo b)
        {
            var cc = Game1.getLocationFromName("CommunityCenter") as CommunityCenter;
            if (cc == null || b.Area < 0 || b.Area > 5)
                return false;
            return !cc.areasComplete[b.Area] && cc.shouldNoteAppearInArea(b.Area);
        }

        public static string IngredientName(BundleIngredientDescription ing)
        {
            string name;
            if (ing.category.HasValue)
                name = "任意" + SObject.GetCategoryDisplayName(ing.category.Value);
            else
                name = ItemRegistry.GetDataOrErrorItem(ing.id.StartsWith("(") ? ing.id : "(O)" + ing.id).DisplayName;
            if (ing.preservesId != null)
                name += "(" + Util.ObjectName(ing.preservesId) + ")";
            string quality = ing.quality switch { 1 => " 银星+", 2 => " 金星+", 4 => " 铱星", _ => "" };
            return $"{name} x{ing.stack}{quality}";
        }

        /// <summary>和 Bundle.IsValidItemForThisIngredientDescription 相同的判断（不含腌制品的风味细节，提交时以游戏判断为准）。</summary>
        public static bool Matches(Item item, BundleIngredientDescription ing)
        {
            if (item == null || ing.completed || ing.quality > item.Quality || item.Stack < ing.stack)
                return false;
            if (ing.preservesId != null)
                return item is SObject o && o.preservedParentSheetIndex.Value == ing.preservesId;
            if (ing.category.HasValue)
                return (item.QualifiedItemId == "(O)107" && ing.category == -5) || item.Category == ing.category;
            return ItemRegistry.HasItemId(item, ing.id);
        }

        /// <summary>背包里能交给这个收集包的物品（每种材料只算一次）。</summary>
        public static List<string> InventoryMatches(BundleInfo b)
        {
            var result = new List<string>();
            if (b.Complete)
                return result;
            foreach (var ing in b.Ingredients.Where(i => !i.completed))
            {
                var item = Game1.player.Items.FirstOrDefault(it => Matches(it, ing));
                if (item != null)
                    result.Add(item.DisplayName);
            }
            return result;
        }

        // ───────────────────────── 博物馆 ─────────────────────────

        public static LibraryMuseum Museum => Game1.getLocationFromName("ArchaeologyHouse") as LibraryMuseum;

        /// <summary>背包里还没捐过、可以捐给博物馆的物品（同一种只算一个）。</summary>
        public static List<Item> MuseumDonatable()
        {
            var museum = Museum;
            var list = new List<Item>();
            if (museum == null)
                return list;
            var seen = new HashSet<string>();
            foreach (var item in Game1.player.Items)
            {
                if (item != null && museum.isItemSuitableForDonation(item) && seen.Add(item.QualifiedItemId))
                    list.Add(item);
            }
            return list;
        }
    }
}
