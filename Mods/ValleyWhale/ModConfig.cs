using StardewModdingAPI;

namespace BlindWhaleFarmer
{
    public class ModConfig
    {
        /// <summary>DeepSeek API Key。留空时读取环境变量 DEEPSEEK_API_KEY。</summary>
        public string ApiKey { get; set; } = "";

        /// <summary>OpenAI 兼容接口地址（不含 /chat/completions）。</summary>
        public string BaseUrl { get; set; } = "https://api.deepseek.com";

        /// <summary>模型名。deepseek-v4-flash 支持工具调用，速度快、便宜（旧名 deepseek-chat 已于 2026-07-24 停用）。</summary>
        public string Model { get; set; } = "deepseek-v4-flash";

        public float Temperature { get; set; } = 0.3f;

        public int MaxTokens { get; set; } = 4096;

        public int RequestTimeoutSeconds { get; set; } = 120;

        /// <summary>开关 AI 的快捷键。</summary>
        public SButton ToggleKey { get; set; } = SButton.F8;

        /// <summary>联机时弹出中文输入框的键（游戏聊天框里看不到输入法的候选词）。</summary>
        public SButton ChatInputKey { get; set; } = SButton.F6;

        /// <summary>联机时 AI 怎么安排自己：solo 自己玩、command 主要听指令、hybrid 指令做完接着自己玩。</summary>
        public string CompanionMode { get; set; } = GameActions.ModeHybrid;

        /// <summary>接管键：中止 AI 正在执行的动作、拒绝之后的所有游戏动作，由玩家自己操作；再按一次交还给 AI。</summary>
        public SButton TakeOverKey { get; set; } = SButton.F7;

        /// <summary>等待模型回复时冻结游戏时间，避免思考浪费游戏内时间。</summary>
        public bool FreezeTimeWhileThinking { get; set; } = true;

        /// <summary>一次回复里最多执行多少个工具调用。</summary>
        public int MaxActionsPerStep { get; set; } = 6;

        /// <summary>提示词里保留多少条最近的行动记录。</summary>
        public int HistoryLines { get; set; } = 25;

        /// <summary>文字地图以玩家为中心的横向/纵向半径（格）。</summary>
        public int MapRadiusX { get; set; } = 14;
        public int MapRadiusY { get; set; } = 9;

        /// <summary>附近物体列表的搜索半径（格）。</summary>
        public int ScanRadius { get; set; } = 16;

        /// <summary>在屏幕左上角显示 AI 的想法和当前动作。</summary>
        public bool ShowHud { get; set; } = true;

        /// <summary>自动点掉出货结算界面、无需选择的升级界面。</summary>
        public bool AutoConfirmNightMenus { get; set; } = true;

        /// <summary>走路时顺手采集野生采集物的范围（格，含斜角）。0 = 不顺手采；1 = 只采紧挨着的；更大会离开路线几步去采再回来。最大 10。</summary>
        public int ForageRadius { get; set; } = 1;

        /// <summary>额外写给 AI 的目标/要求，例如“优先赚钱，第一年春天种满防风草”。</summary>
        public string ExtraInstructions { get; set; } = "";

        /// <summary>攻略文件（相对模组目录或绝对路径）。想改打法就复制一份改名后指到这里，更新模组时不会被覆盖；留空用 guide/攻略.md（旧名 guide/zh-CN.md 也会自动换成它）。</summary>
        public string GuideFile { get; set; } = Guide.DefaultFile;

        /// <summary>在 DSH 设置页改过的攻略：篇名（攻略、钓鱼、采矿、节日……）→ 全文。有内容的篇目优先于 guide/ 里的文件。</summary>
        public System.Collections.Generic.Dictionary<string, string> GuideOverrides { get; set; } = new();

        /// <summary>把每次发送给模型的完整提示词写到日志文件，方便调试。</summary>
        public bool LogPrompts { get; set; } = false;

        /// <summary>开启本地 MCP 服务器，让 WorkBuddy / Claude Code / Cursor 等 MCP 客户端来玩（只监听本机 127.0.0.1）。</summary>
        public bool EnableMcp { get; set; } = true;

        /// <summary>MCP 服务器端口，地址为 http://127.0.0.1:端口/mcp。</summary>
        public int McpPort { get; set; } = 18080;

        /// <summary>MCP 客户端在思考（两次调用之间）时冻结游戏时间。超过 McpIdleSeconds 没有调用就恢复走时。</summary>
        public bool McpFreezeTime { get; set; } = true;

        public int McpIdleSeconds { get; set; } = 120;

        /// <summary>MCP 客户端超过 McpIdleSeconds 没有调用（AI 停下或断开）时把整个游戏暂停（怪物、时钟都停），按任意键接手或 AI 再次调用时继续。</summary>
        public bool PauseWhenAiStops { get; set; } = true;

        /// <summary>每次游玩最多玩几个游戏日（睡一觉算一天），到了就让 AI 停下汇报。0 = 不限。读档后重新计。</summary>
        public int SessionDays { get; set; } = 0;

        /// <summary>每次游玩最多玩多少现实分钟（从这次第一个动作开始算），到了就让 AI 回家睡觉存档后停下。0 = 不限。</summary>
        public int SessionMinutes { get; set; } = 0;
    }
}
