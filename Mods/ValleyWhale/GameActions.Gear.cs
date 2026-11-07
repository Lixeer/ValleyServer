using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace BlindWhaleFarmer
{
    /// <summary>装备：钓鱼挑最好的鱼竿并装上鱼饵/钓具，背包里有更好的鞋子、戒指就换上。</summary>
    internal partial class GameActions
    {
        /// <summary>
        /// 鱼竿好坏：铱金(3/4) &gt; 玻璃纤维(2) &gt; 竹竿(0) &gt; 训练用鱼竿。
        /// 训练用鱼竿的工具等级是 1，但游戏规定它钓不到难度 50 以上的鱼（GameLocation 里的 TrainingRod 判断），所以排最后。
        /// </summary>
        private static int RodScore(FishingRod rod) =>
            rod.QualifiedItemId == "(T)TrainingRod" ? -1 : rod.UpgradeLevel;

        private static int BestRodIndex()
        {
            int best = -1, bestScore = int.MinValue;
            for (int i = 0; i < Player.Items.Count; i++)
            {
                if (Player.Items[i] is FishingRod rod && RodScore(rod) > bestScore)
                {
                    best = i;
                    bestScore = RodScore(rod);
                }
            }
            return best;
        }

        /// <summary>
        /// 鱼竿能装鱼饵（玻璃纤维以上）/钓具（铱金以上）但空着时，从背包里拿来装上。返回装了什么，没装返回 null。
        /// 鱼饵类别 -21，钓具类别 -22，能不能装由鱼竿自己判断（canThisBeAttached）。
        /// </summary>
        private static string FillRodAttachments(FishingRod rod)
        {
            var added = new List<string>();
            void Fill(int category)
            {
                for (int i = 0; i < Player.Items.Count; i++)
                {
                    if (Player.Items[i] is SObject o && o.Category == category && rod.canThisBeAttached(o))
                    {
                        Player.Items[i] = null;
                        var old = rod.attach(o);
                        if (old != null)
                            Player.addItemToInventory(old);
                        added.Add(o.DisplayName + (o.Stack > 1 ? $"x{o.Stack}" : ""));
                        return;
                    }
                }
            }
            if (rod.CanUseBait() && rod.GetBait() == null)
                Fill(SObject.baitCategory);
            if (rod.CanUseTackle() && rod.GetTackle().All(t => t == null))
                Fill(SObject.tackleCategory);
            return added.Count > 0 ? $"给 {rod.DisplayName} 装上了 {string.Join("、", added)}" : null;
        }

        private static int BootsScore(Boots b) => b == null ? -1 : b.defenseBonus.Value + b.immunityBonus.Value;

        /// <summary>戒指好坏没有统一的数值，用游戏里的售价当参考（越稀有越贵）。</summary>
        private static int RingScore(Ring r) => r == null ? -1 : r.salePrice();

        /// <summary>
        /// 背包里有更好的鞋子（防御+免疫更高）或戒指（空着的手指先戴上，否则换掉售价最低的那只）就换上，
        /// 换下来的放回背包原来那一格。用游戏的 Farmer.Equip，戒指的光照、加成等效果和手动换装一样生效。
        /// 返回换了什么，没换返回 null。
        /// </summary>
        public static string AutoEquip()
        {
            if (!StardewModdingAPI.Context.IsWorldReady || Game1.eventUp || Game1.activeClickableMenu != null)
                return null;
            var changes = new List<string>();

            for (int i = 0; i < Player.Items.Count; i++)
            {
                if (Player.Items[i] is Boots b && BootsScore(b) > BootsScore(Player.boots.Value))
                {
                    Player.Items[i] = null;
                    var old = Player.Equip(b, Player.boots);
                    Player.Items[i] = old;
                    changes.Add($"穿上了 {b.DisplayName}（防御 +{b.defenseBonus.Value}，免疫 +{b.immunityBonus.Value}）");
                }
            }

            for (int i = 0; i < Player.Items.Count; i++)
            {
                if (Player.Items[i] is not Ring r)
                    continue;
                // 结婚戒指要留着求婚，戴上也没有效果
                if (r.QualifiedItemId == "(O)801")
                    continue;
                var slot = Player.leftRing.Value == null ? Player.leftRing
                         : Player.rightRing.Value == null ? Player.rightRing
                         : RingScore(Player.leftRing.Value) <= RingScore(Player.rightRing.Value) ? Player.leftRing : Player.rightRing;
                if (slot.Value != null && RingScore(r) <= RingScore(slot.Value))
                    continue;
                Player.Items[i] = null;
                var old = Player.Equip(r, slot);
                Player.Items[i] = old;
                changes.Add($"戴上了 {r.DisplayName}" + (old != null ? $"（换下 {old.DisplayName}）" : ""));
            }

            return changes.Count > 0 ? string.Join("；", changes) : null;
        }
    }
}
