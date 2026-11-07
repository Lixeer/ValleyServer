using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BlindWhaleFarmer
{
    /// <summary>暴露给大模型的工具：JSON Schema 定义 + 调用分发。</summary>
    internal sealed class Tools
    {
        private sealed record Param(string Name, string Type, string Desc, bool Required);

        private sealed class Spec
        {
            public string Name;
            public string Desc;
            public Param[] Params;
            public int MaxSeconds;
            public Func<JsonObject, Func<IEnumerable<object>>> Make;
        }

        private readonly MainThread main;
        private readonly Dictionary<string, Spec> specs = new();

        /// <summary>处理界面/剧情的工具：剧情或界面打开时可以打断卡住的长动作（见 MainThread.RunAction）。</summary>
        private static readonly HashSet<string> InterfaceTools = new() { "dialogue", "skip_event", "close_menu", "menu_click", "leave_festival", "choose_profession", "set_name" };

        /// <summary>背包满了时附在结果后面的处理顺序：先存，后出货，别急着卖。</summary>
        private const string BagFullAdvice =
            "背包满了的处理顺序：① 用 tidy_backpack 一键整理（低级鱼竿、多余武器、矿石、鱼等存进农场箱子），或者 store_items 存指定的东西；"
            + "② 没箱子就 craft Chest（50 木材）、place_item 放在农场再存；③ 今天就要换钱的作物放进出货箱 ship_items；"
            + "④ 不要为了腾地方马上 sell 掉，尤其是收集包材料和等钓鱼 5 级再卖更值钱的鱼";

        /// <summary>有人说话也不从外面打断的工具：本身就在等人或者很短；钓鱼半路断了鱼竿还在水里，它自己在两竿之间停。</summary>
        private static readonly HashSet<string> NoChatInterrupt = new() { "say", "set_mode", "wait_chat", "follow", "propose", "sleep", "festival", "fish", "get_state", "get_guide" };

        /// <summary>打怪类工具：本来就会挨打，不因掉血中断。</summary>
        private static readonly HashSet<string> CombatTools = new() { "fight", "explore_mine" };
        private readonly Func<string, string> updateNotes;

        public JsonArray Schema { get; }

        public Tools(MainThread main, GameActions a, Func<string, string> updateNotes)
        {
            this.main = main;
            this.updateNotes = updateNotes;

            Add("move_to", "走到当前地点的某一格（自动寻路）。", 60,
                j => () => a.MoveTo(Int(j, "x"), Int(j, "y")),
                P("x", "integer", "目标 x"), P("y", "integer", "目标 y"));

            Add("travel_to", "自动走到另一个地点（会自动经过多个地图、开门）。地点用英文内部名，如 Farm、FarmHouse、Town、SeedShop(皮埃尔杂货店)、Blacksmith(铁匠)、ScienceHouse(木匠)、Saloon、Beach、FishShop、Mountain、Forest、BusStop、Mine、JojaMart、ManorHouse(镇长家)、CommunityCenter。", 180,
                j => () => a.TravelTo(Str(j, "location"), OptBool(j, "detour") == true),
                P("location", "string", "目标地点内部名"),
                P("detour", "boolean", "近路被挡住时绕路走。默认不绕，会先告诉你挡在哪、绕路怎么走，让你选开路还是绕路", false));

            Add("use_tool", "走到目标格旁边并对它使用工具。工具可写：axe/hoe/pickaxe/watering_can/scythe/weapon，或背包序号。可以砍树、敲石头、锄地、浇水、给水壶灌水（对水面使用水壶）、打怪。目标被清除后会自动停止。", 120,
                j => () => a.UseTool(Str(j, "tool"), Int(j, "x"), Int(j, "y"), OptInt(j, "times") ?? 1),
                P("tool", "string", "工具"), P("x", "integer", "目标 x"), P("y", "integer", "目标 y"),
                P("times", "integer", "最多使用几次（砍大树约需10次），默认1", false));

            Add("interact", "走到目标旁边并按“动作键”：和村民聊天、开门、捡采集品、收获作物、收取机器产物、读邮箱、看告示板。传 npc 名字可以自动追着村民走。传 item 则先拿着该物品再交互（用于送礼、往机器里放材料）。", 120,
                j => () => a.Interact(OptInt(j, "x"), OptInt(j, "y"), OptStr(j, "npc"), OptStr(j, "item")),
                P("x", "integer", "目标 x", false), P("y", "integer", "目标 y", false),
                P("npc", "string", "村民名字（英文或中文），与 x,y 二选一", false),
                P("item", "string", "交互时手里拿的物品（名称或背包序号），用于送礼/放材料", false));

            Add("place_item", "把背包里的物品放到某一格（种单颗种子、施肥、放置箱子/稻草人/机器等）。", 60,
                j => () => a.PlaceItem(Str(j, "item"), Int(j, "x"), Int(j, "y")),
                P("item", "string", "物品名称或背包序号"), P("x", "integer", "x"), P("y", "integer", "y"));

            Add("till_area", "用锄头开垦一个矩形区域里所有能开垦的空地（最多80格）。区域里有石头杂草的格子会跳过，先用 clear_area。", 300,
                j => () => a.TillArea(Int(j, "x1"), Int(j, "y1"), Int(j, "x2"), Int(j, "y2")),
                P("x1", "integer", "左上 x"), P("y1", "integer", "左上 y"), P("x2", "integer", "右下 x"), P("y2", "integer", "右下 y"));

            Add("plant_seeds", "在空耕地上批量播种（或施肥）。不给区域就在本地点所有空耕地上按由近到远播种。", 300,
                j => () => a.PlantSeeds(Str(j, "seed"), OptInt(j, "x1"), OptInt(j, "y1"), OptInt(j, "x2"), OptInt(j, "y2"), OptInt(j, "max") ?? 200),
                P("seed", "string", "种子名称或背包序号"),
                P("x1", "integer", "区域左上 x", false), P("y1", "integer", "区域左上 y", false),
                P("x2", "integer", "区域右下 x", false), P("y2", "integer", "区域右下 y", false),
                P("max", "integer", "最多种几格", false));

            Add("water_crops", "给本地点所有需要浇水的作物浇水（水壶空了会自动去最近的水源灌水）。", 400,
                j => () => a.WaterCrops(OptInt(j, "max") ?? 300),
                P("max", "integer", "最多浇几株", false));

            Add("harvest_crops", "收获本地点所有成熟的作物（需要镰刀的会自动用镰刀）。", 300,
                j => () => a.HarvestCrops(OptInt(j, "max") ?? 300),
                P("max", "integer", "最多收几株", false));

            Add("clear_area", "清理一个矩形区域里的杂物：石头用镐、杂草和草用镰刀、树枝/树苗/树桩用斧头、远古斑点用锄头。默认不砍大树。一次最多60处。", 400,
                j => () => a.ClearArea(Int(j, "x1"), Int(j, "y1"), Int(j, "x2"), Int(j, "y2"), OptBool(j, "include_trees") ?? false),
                P("x1", "integer", "左上 x"), P("y1", "integer", "左上 y"), P("x2", "integer", "右下 x"), P("y2", "integer", "右下 y"),
                P("include_trees", "boolean", "是否也砍大树（很耗体力）", false));

            Add("ship_items", "把物品放进农场的出货箱（每晚自动结算成钱）。不在农场会自动走回农场。", 180,
                j => () => a.ShipItems(Str(j, "item"), OptInt(j, "count")),
                P("item", "string", "物品名称或背包序号"), P("count", "integer", "数量，默认全部", false));

            Add("store_items", "把背包里的东西存进箱子，腾出背包（工具和武器只存写了全名的，比如“训练用鱼竿”；整理背包更推荐 tidy_backpack）。不传坐标就用本地点最近的箱子。", 120,
                j => () => a.StoreItems(OptStr(j, "items"), OptInt(j, "x"), OptInt(j, "y")),
                P("items", "string", "要存的物品名，多个用逗号分隔；不传或 all 表示除工具外全部存", false),
                P("x", "integer", "箱子 x", false), P("y", "integer", "箱子 y", false));

            Add("festival", "参加节日：enter 走去节日地点并入场（节日开放时间内）；greet 和在场的村民挨个打招呼（节日上说话也加好感）；"
                + "start 自动玩这个节日的小游戏（找彩蛋、夏威夷宴会放汤、花舞节邀舞伴、冬日星盛宴送神秘礼物、万灵节迷宫宝箱、冰钓比赛、展览会展示柜评比），再找刘易斯开始主活动并等它结束（结束后游戏把你送回农场）；leave 中途离开。节日当天先 get_guide 读“节日”专题。联机时只有主机能开始主活动：你是农场工人时 start 会先做好准备、说一声，等主机开始后自动参加（已经开始了就直接加入）；展览会的共用展示柜要问过主机再用 grange 摆。", 620,
                j => () => a.Festival(Str(j, "action")),
                P("action", "string", "enter / greet / start / leave / grange", true));

            Add("tidy_backpack", "整理背包：每类工具只留最好的一把（低级鱼竿、多余的武器等存起来），身上留吃的（料理；不够两样时再留回血最多的普通食物）、种子、肥料、鱼饵钓具、炸弹、任务物品、要摆放的机器，"
                + "其余（矿石、石头木材、宝石、鱼、作物、怪物掉落……）存进农场的箱子，一个满了换下一个。不在农场会先回农场。出门下矿/钓鱼前、背包快满时用。", 300,
                j => () => a.TidyBackpack(OptStr(j, "keep")),
                P("keep", "string", "额外要留在身上的物品名，多个用逗号分隔（比如今天要送的礼物、要交的收集包材料）", false));

            Add("take_items", "从箱子里取东西回背包。结果会列出箱子里还有什么。", 120,
                j => () => a.TakeItems(OptStr(j, "items"), OptInt(j, "x"), OptInt(j, "y")),
                P("items", "string", "要取的物品名，多个用逗号分隔；all 表示全部", false),
                P("x", "integer", "箱子 x", false), P("y", "integer", "箱子 y", false));

            Add("eat", "吃掉背包里的食物恢复体力和生命。", 30,
                j => () => a.Eat(Str(j, "item")),
                P("item", "string", "食物名称或背包序号"));

            Add("open_shop", "在商店所在地点打开商店界面（店主要在场、在营业时间）。不传 shop_id 就自动判断：SeedShop→杂货店，Blacksmith→铁匠，ScienceHouse→木匠，FishShop→鱼店，Saloon→酒吧。", 20,
                j => () => a.OpenShop(OptStr(j, "shop_id")),
                P("shop_id", "string", "商店ID，可省略", false));

            Add("buy", "在已打开的商店里购买。", 20,
                j => () => a.Buy(Str(j, "item"), OptInt(j, "count") ?? 1),
                P("item", "string", "商品名称或商店列表序号"), P("count", "integer", "数量", false));

            Add("sell", "在已打开的商店里出售背包物品（立即得钱，价格和出货箱一样）。", 20,
                j => () => a.Sell(Str(j, "item"), OptInt(j, "count")),
                P("item", "string", "物品名称或背包序号"), P("count", "integer", "数量，默认全部", false));

            Add("craft", "用已学会的配方制作物品（如 Chest 箱子、Scarecrow 稻草人、Basic Fertilizer 肥料）。", 20,
                j => () => a.Craft(Str(j, "recipe"), OptInt(j, "count") ?? 1),
                P("recipe", "string", "配方名（中英文都可以）"), P("count", "integer", "数量", false));

            Add("dialogue", "推进当前对话框；如果是选择题，传 choice 选项序号。", 20,
                j => () => a.Dialogue(OptInt(j, "choice")),
                P("choice", "integer", "选项序号（选择题才需要）", false));

            Add("menu_click", "点击当前界面里的某个可点击元素（序号见“当前界面”）。", 20,
                j => () => a.MenuClick(Int(j, "index")),
                P("index", "integer", "元素序号"));

            Add("close_menu", "关闭当前界面/信件/商店，或确认提示。", 20,
                j => () => a.CloseMenu());

            Add("choose_profession", "升级时选择职业。", 20,
                j => () => a.ChooseProfession(Str(j, "side")),
                P("side", "string", "left 或 right"));

            Add("skip_event", "跳过当前可以跳过的剧情事件。", 20,
                j => () => a.SkipEvent());

            Add("leave_festival", "离开正在参加的节日（复活节、花舞节等）。节日里时间停止、普通的移动/干活工具都不能用；离开后游戏会把你送回去（时间按游戏规则推进）。", 320,
                j => () => a.LeaveFestival());

            Add("fish", "钓鱼：自动在附近找岸边、抛竿、等咬钩、提竿并完成钓鱼小游戏，连钓多次。需要背包里有鱼竿。每次抛竿约消耗 8 体力、几十分钟游戏时间。也可以指定站立坐标和朝向。", 900,
                j => () => a.Fish(OptInt(j, "casts") ?? 5, OptInt(j, "x"), OptInt(j, "y"), OptStr(j, "direction")),
                P("casts", "integer", "抛竿次数 1~20，默认 5", false),
                P("x", "integer", "站立的 x（可省略，自动找）", false), P("y", "integer", "站立的 y", false),
                P("direction", "string", "朝向 up/down/left/right（可省略）", false));

            Add("care_animals", "照顾所有动物：自动走进每个鸡舍/畜棚，捡蛋和羊毛等地上的产物、清空自动采集器、给食槽放干草（用筒仓里的干草）、摸每只动物、用挤奶桶/剪刀收奶和毛，最后处理农场上在外面的动物。每天做一次（早上最好）。door=open 会打开动物门让它们出去吃草（晴天、非冬季），door=close 关门（下雨/晚上）。", 600,
                j => () => a.CareAnimals(OptStr(j, "door")),
                P("door", "string", "open/close，可省略（不动门）", false));

            Add("gift_info", "查询村民的送礼喜好（最爱/喜欢/讨厌）、生日、好感和本周送礼次数，以及背包里哪些东西他喜欢。不花游戏时间。", 10,
                j => () => a.GiftInfo(Str(j, "npc")),
                P("npc", "string", "村民名字（英文或中文）"));

            Add("give_gift", "给村民送礼：村民不在当前地点会自动找过去。每人每天 1 次、每周 2 次（生日不限）；生日送最爱的礼物效果 8 倍。送之前最好用 gift_info 确认他喜欢。送花束开始恋爱、送美人鱼吊坠求婚也用它，这两样不受次数限制。", 240,
                j => () => a.GiveGift(Str(j, "npc"), Str(j, "item")),
                P("npc", "string", "村民名字"), P("item", "string", "礼物（物品名称或背包序号）"));

            Add("donate_museum", "去博物馆（ArchaeologyHouse，8:00-18:00）把背包里所有还没捐过的矿物/古物捐掉，并领取捐赠奖励（种子、配方、家具等）。", 240,
                j => () => a.DonateMuseum());

            Add("bundle_info", "查看社区中心收集包：每个包还缺哪些材料、背包里有哪些能交。all=true 时也列出还没解锁的区域。", 10,
                j => () => a.BundleInfo(OptBool(j, "all") ?? false),
                P("all", "boolean", "是否包含未解锁区域", false));

            Add("donate_bundles", "去社区中心把背包里能交的材料全部交进收集包，完成的收集包会自动领奖励，整个房间完成时会播放修复过场。", 600,
                j => () => a.DonateBundles());

            Add("cook", "在厨房做菜（农舍升级后才有厨房；不在家会自动回家）。背包和冰箱里的材料都能用。不传 recipe 或写错时会列出已学会的菜谱和材料、标出现在能做的。料理回体力回血多，也是好礼物。", 120,
                j => () => a.Cook(OptStr(j, "recipe"), OptInt(j, "count") ?? 1),
                P("recipe", "string", "菜谱名（中英文都可以），可省略", false), P("count", "integer", "做几份，默认 1", false));

            Add("check_crab_pots", "收当前地点（或指定地点）所有蟹笼里的东西，并给没鱼饵的蟹笼放鱼饵。蟹笼用 place_item 放在水面上（紧挨岸边）。", 240,
                j => () => a.CheckCrabPots(OptStr(j, "location")),
                P("location", "string", "蟹笼所在地点（可省略=当前地点）", false));

            Add("fight", "打附近的怪物：自动靠近、挥最好的武器，生命低时自动吃回血食物；没有食物会停下让你撤退。最长约 2 分钟。", 150,
                j => () => a.Fight(OptInt(j, "radius") ?? 12, OptInt(j, "min_health_percent") ?? 40),
                P("radius", "integer", "多远以内的怪（格），默认 12", false),
                P("min_health_percent", "integer", "生命低于百分之多少就吃东西，默认 40", false));

            Add("explore_mine", "自动下矿洞：打附近的怪、用镐敲石头找楼梯、下楼，下够 levels 层或体力/生命/时间不够时停下。在矿洞外会先去矿洞入口；start_level 可用电梯直达（5 的倍数且到过）。出发前带上武器、镐子和回血食物。", 900,
                j => () => a.ExploreMine(OptInt(j, "levels") ?? 3, OptInt(j, "start_level")),
                P("levels", "integer", "这次往下走几层（1~20），默认 3", false),
                P("start_level", "integer", "从哪层开始（电梯层，可省略）", false));

            Add("upgrade_tool", "找铁匠 Clint（Blacksmith）升级工具（花钱+矿锭，2 天后取，期间工具不能用），或者领取已经升级好的工具（tool 可省略）。建议在下雨天升级水壶。", 240,
                j => () => a.UpgradeTool(OptStr(j, "tool")),
                P("tool", "string", "要升级的工具：axe/pickaxe/hoe/watering_can；领取时可省略", false));

            Add("crack_geodes", "找铁匠 Clint（Blacksmith，9:00-16:00）砸晶球，每个 25g，返回开出了什么。会自己走过去，不用打开界面。", 240,
                j => () => a.CrackGeodes(OptStr(j, "item"), OptInt(j, "count")),
                P("item", "string", "只砸哪种（如 冰封晶球），可省略=全部", false),
                P("count", "integer", "最多砸几个，可省略=全部", false));

            Add("quest_board", "去镇上皮埃尔店门口的告示板查看今天的委托任务；accept=true 接受（2 天内完成，有报酬和好感）。", 180,
                j => () => a.QuestBoard(OptBool(j, "accept") ?? false),
                P("accept", "boolean", "是否接受", false));

            Add("buy_animal", "去 Marnie 的牧场（AnimalShop）买动物，自动放进有空位的鸡舍/畜棚并起名。不传 type 会列出能买的动物和价格。", 240,
                j => () => a.BuyAnimal(OptStr(j, "type"), OptStr(j, "name"), OptInt(j, "count") ?? 1),
                P("type", "string", "动物种类，如 Chicken、Cow、Goat、Duck、Pig（中英文都可以）", false),
                P("name", "string", "名字（可省略，自动起名）", false), P("count", "integer", "买几只，默认 1", false));

            Add("build", "找木匠 Robin（ScienceHouse）在农场建造或升级建筑（鸡舍 Coop、畜棚 Barn、筒仓 Silo 等；Big Coop 等是升级）。不传 x,y 会在农舍附近自动找空地。不传 building 会列出能建的和价格。", 300,
                j => () => a.Build(OptStr(j, "building"), OptInt(j, "x"), OptInt(j, "y")),
                P("building", "string", "建筑名（中英文都可以），可省略", false),
                P("x", "integer", "建筑左上角 x（可省略）", false), P("y", "integer", "建筑左上角 y（可省略）", false));

            Add("upgrade_house", "找 Robin 升级农舍：第一次 10000g+450木材，升级后有厨房（可以 cook）和冰箱。", 240,
                j => () => a.UpgradeHouse());

            Add("sleep", "回家上床睡觉，结束这一天（会自动走回农舍）。体力恢复，作物生长。必须在凌晨2点前睡觉，否则会晕倒并损失金钱。会等到第二天早上（结算界面确认完）才返回。", 420,
                j => () => a.Sleep());

            Add("wait", "原地等待若干游戏分钟（10~600），比如等商店开门。", 900,
                j => () => a.Wait(OptInt(j, "minutes") ?? 60),
                P("minutes", "integer", "游戏内分钟数"));

            Add("say", "联机时在游戏聊天里说一句话，其他玩家能看到。只有联机时能用。", 10,
                j => () => a.Say(Str(j, "text")),
                P("text", "string", "要说的话（300 字以内）"));

            Add("set_name", "给孩子或者刚出生的动物起名（弹出起名界面时用）。", 20,
                j => () => a.SetName(Str(j, "name")),
                P("name", "string", "名字（1~12 个字）"));

            Add("set_mode", "切换联机模式：别的玩家让你换模式时用，会在聊天里说一声。solo 自己玩、command 主要听指令、hybrid 指令做完接着自己玩。别人没要求就不要自己换。只有联机时能用。", 10,
                j => () => a.SetMode(Str(j, "mode")),
                P("mode", "string", "solo、command 或 hybrid"));

            Add("wait_chat", "联机时等别人说话：有人说话就立刻返回，最多等 seconds 秒（5~300）。闲着等吩咐时用。", 320,
                j => () => a.WaitChat(OptInt(j, "seconds") ?? 60),
                P("seconds", "integer", "最多等几秒，默认 60", false));

            Add("follow", "联机时跟着别的玩家走，换地图也会跟过去，有人说话、出现剧情或界面就停下。只有联机时能用。", 620,
                j => () => a.Follow(OptInt(j, "seconds") ?? 120),
                P("seconds", "integer", "跟多少秒（10~600），默认 120", false));

            Add("propose", "联机时拿结婚戒指向别的玩家求婚：自动走到对方旁边，对方的窗口里会弹问题，等对方回答（最多 2 分钟）。背包里要有结婚戒指。先用 say 说一声。只有联机时能用。", 180,
                j => () => a.Propose());

            Schema = BuildSchema();
            Schema.Add(Fn("update_notes", "覆盖写入你的长期笔记（会在每一步提示给你）：写下当前目标、计划、重要发现（商店位置、NPC喜好、已开垦区域坐标等）。保持简洁，200字以内。",
                new[] { P("notes", "string", "完整的新笔记内容") }));
        }

        private static Param P(string name, string type, string desc, bool required = true) => new(name, type, desc, required);

        private void Add(string name, string desc, int maxSeconds, Func<JsonObject, Func<IEnumerable<object>>> make, params Param[] ps)
        {
            specs[name] = new Spec { Name = name, Desc = desc, Params = ps, MaxSeconds = maxSeconds, Make = make };
        }

        private JsonArray BuildSchema()
        {
            var arr = new JsonArray();
            foreach (var s in specs.Values)
                arr.Add(Fn(s.Name, s.Desc, s.Params));
            return arr;
        }

        private static JsonObject Fn(string name, string desc, Param[] ps)
        {
            var props = new JsonObject();
            var required = new JsonArray();
            foreach (var p in ps)
            {
                props[p.Name] = new JsonObject { ["type"] = p.Type, ["description"] = p.Desc };
                if (p.Required)
                    required.Add(p.Name);
            }
            return new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = name,
                    ["description"] = desc,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = props,
                        ["required"] = required
                    }
                }
            };
        }

        public bool IsKnown(string name) => name == "update_notes" || specs.ContainsKey(name);

        /// <summary>本次游玩的时长限制（见 PlaySession）；为 null 时不限制。</summary>
        public PlaySession Session { get; set; }

        public async Task<string> Execute(string name, string arguments)
        {
            JsonObject args;
            try
            {
                args = string.IsNullOrWhiteSpace(arguments) ? new JsonObject() : JsonNode.Parse(arguments) as JsonObject ?? new JsonObject();
            }
            catch (Exception ex)
            {
                return "失败：参数不是合法 JSON：" + ex.Message;
            }

            if (Session != null)
            {
                string denied = Session.Gate(name);
                if (denied != null)
                    return denied;
                Session.Touch();
            }

            if (name == "update_notes")
                return updateNotes(OptStr(args, "notes") ?? "");

            if (!specs.TryGetValue(name, out var spec))
                return $"失败：没有叫 {name} 的工具";

            Func<IEnumerable<object>> factory;
            try
            {
                factory = spec.Make(args);
            }
            catch (Exception ex)
            {
                return "失败：参数错误：" + ex.Message;
            }
            // 打怪类动作本来就会挨打；其他动作被怪打了要停下，让 AI 先清怪
            bool interruptOnDamage = !CombatTools.Contains(name) && !InterfaceTools.Contains(name);
            string result = await main.RunAction(factory, spec.MaxSeconds, InterfaceTools.Contains(name), interruptOnDamage, name, !InterfaceTools.Contains(name) && !NoChatInterrupt.Contains(name)).ConfigureAwait(false);
            if (name == "sleep")
                return result;
            if (result.Contains("背包满了") && name != "store_items" && name != "take_items")
                result += "\n" + BagFullAdvice;
            // 拿到更好的鞋子、戒指就自动换上（宝箱、商店、任务奖励都可能给）；
            // 身边有怪、深夜、体力见底的提醒也接在动作结果后面：MCP 客户端不一定每步都看 get_state
            string warning = null;
            try
            {
                // 世界没就绪时主线程可能不再 Tick，排队的 Invoke 会一直等，所以先判断
                if (StardewModdingAPI.Context.IsWorldReady)
                    warning = await main.Invoke(() =>
                    {
                        var equipped = GameActions.AutoEquip();
                        // 这次结果里有新的话就先不催，等下一步
                        string chat = GameActions.TakeChat();
                        string replyReminder = chat == null && name != "get_guide" ? GameActions.TakeReplyReminder() : null;
                        var parts = new[] { equipped != null ? "🎽 " + equipped : null, chat, replyReminder, GameActions.TakePickedOnTheWay(), GameActions.ThreatWarning(), GameActions.BedtimeWarning(urgentOnly: true) }.Where(w => w != null).ToList();
                        return parts.Count > 0 ? string.Join("\n", parts) : null;
                    }).ConfigureAwait(false);
            }
            catch
            {
                // 世界未就绪等情况下不提醒
            }
            return warning == null ? result : result + "\n" + warning;
        }

        // ── 参数解析：模型有时会把数字写成字符串，这里都兼容 ──

        private static int Int(JsonObject j, string key) => OptInt(j, key) ?? throw new ArgumentException($"缺少参数 {key}");

        private static int? OptInt(JsonObject j, string key)
        {
            var n = j[key];
            if (n == null)
                return null;
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out int i))
                    return i;
                if (v.TryGetValue(out double d))
                    return (int)Math.Round(d);
                if (v.TryGetValue(out string s) && int.TryParse(s.Trim(), out int si))
                    return si;
            }
            return null;
        }

        private static string Str(JsonObject j, string key) => OptStr(j, key) ?? throw new ArgumentException($"缺少参数 {key}");

        private static string OptStr(JsonObject j, string key)
        {
            var n = j[key];
            if (n == null)
                return null;
            if (n is JsonValue v && v.TryGetValue(out string s))
                return s;
            return n.ToJsonString().Trim('"');
        }

        private static bool? OptBool(JsonObject j, string key)
        {
            var n = j[key];
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out bool b))
                    return b;
                if (v.TryGetValue(out string s))
                    return s.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "是";
            }
            return null;
        }
    }
}
