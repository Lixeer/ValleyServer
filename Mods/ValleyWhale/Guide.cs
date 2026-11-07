using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlindWhaleFarmer
{
    /// <summary>
    /// 攻略文件（guide/*.md）：游戏常识和打法建议，和代码里的操作规则（Agent.Rules）分开放，
    /// 玩家可以直接改文本调整 AI 的打法、或者换成其他语言，不用重新编译。
    /// 主攻略（攻略.md）每次连接都带上；同目录下的其他 .md 是专题（钓鱼、采矿、节日……），AI 用 get_guide 按需读，不占常驻上下文。
    /// 玩家在 DSH 设置页改过的内容（ModConfig.GuideOverrides）优先于文件。
    /// </summary>
    internal static class Guide
    {
        public const string DefaultFile = "guide/攻略.md";

        /// <summary>主攻略的名字（get_guide 不传 topic 时读它）。</summary>
        public const string MainTopic = "攻略";

        /// <summary>以前的默认文件名。旧的 config.json、DSH 插件设置里可能还写着它，读的时候换成新文件名。</summary>
        private const string LegacyDefaultFile = "guide/zh-CN.md";

        private static readonly Dictionary<string, string> fromFiles = new();
        private static Dictionary<string, string> overrides = new();
        private static string mainName = MainTopic;

        /// <summary>当前主攻略全文（玩家改过的优先）；没有攻略时为空字符串（只用操作规则也能玩）。</summary>
        public static string Text => Get(mainName) ?? "";

        /// <summary>专题名字（不含主攻略），按文件名排序。</summary>
        public static IReadOnlyList<string> Topics =>
            fromFiles.Keys.Concat(overrides.Keys).Distinct().Where(t => t != mainName).OrderBy(t => t).ToList();

        /// <summary>读某一篇（主攻略或专题）；没有返回 null。</summary>
        public static string Get(string topic)
        {
            topic = string.IsNullOrWhiteSpace(topic) ? mainName : topic.Trim().Replace(".md", "");
            if (overrides.TryGetValue(topic, out var o) && !string.IsNullOrWhiteSpace(o))
                return o.Trim();
            return fromFiles.TryGetValue(topic, out var f) ? f : null;
        }

        /// <summary>玩家在 DSH 设置页改的内容：名字 → 全文（空的表示用文件里的）。</summary>
        public static void SetOverrides(Dictionary<string, string> map) =>
            overrides = map?.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value) ?? new();

        /// <summary>从模组目录读取主攻略和同目录的专题，返回一行给日志看的说明。file 可以是相对模组目录的路径或绝对路径。</summary>
        public static string Load(string modDir, string file)
        {
            if (string.IsNullOrWhiteSpace(file) || file.Replace('\\', '/').Trim() == LegacyDefaultFile)
                file = DefaultFile;
            string path = Path.IsPathRooted(file) ? file : Path.Combine(modDir, file);
            fromFiles.Clear();
            mainName = Path.GetFileNameWithoutExtension(path);
            try
            {
                if (!File.Exists(path))
                    return $"没有找到攻略文件 {path}，AI 只使用基本操作规则";
                foreach (var md in Directory.GetFiles(Path.GetDirectoryName(path), "*.md"))
                {
                    string topic = Path.GetFileNameWithoutExtension(md);
                    // 改名前的旧主攻略（zh-CN.md）更新模组后可能还留在游戏目录里，别把它当成专题
                    if (topic == Path.GetFileNameWithoutExtension(LegacyDefaultFile) && topic != mainName)
                        continue;
                    fromFiles[topic] = File.ReadAllText(md).Trim();
                }
                var topics = Topics;
                return $"已加载攻略 {file}（{Text.Length} 字）" + (topics.Count > 0 ? $"，专题：{string.Join("、", topics)}" : "");
            }
            catch (Exception ex)
            {
                fromFiles.Clear();
                return $"读取攻略文件 {path} 失败：{ex.Message}";
            }
        }
    }
}
