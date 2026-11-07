using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;

namespace BlindWhaleFarmer
{
    /// <summary>
    /// 本地 MCP 服务器（Streamable HTTP 传输；工具调用在客户端接受时用 SSE 回复并定时发心跳，其余用 JSON）。
    /// 让 WorkBuddy、Claude Code、Cursor 等 MCP 客户端直接调用和内置 AI 相同的工具来玩游戏，
    /// 思考由客户端完成，消耗的是客户端自己的额度。只监听 127.0.0.1。
    /// </summary>
    internal sealed class McpServer : IDisposable
    {
        private static readonly string[] SupportedVersions = { "2025-06-18", "2025-03-26", "2024-11-05" };

        private static string Instructions => Agent.SystemPrompt + McpNotes;

        /// <summary>只读取信息、不在游戏里执行动作的工具：不走 Tools、不记 transcript、结果不算“失败”。</summary>
        private static readonly HashSet<string> InfoTools = new() { "get_state", "get_guide" };

        private const string McpNotes = @"

## 通过 MCP 玩的额外说明
- 你看不到游戏，每次行动前先调用 get_state 读取当前状态（时间、界面、背包、地图、附近物体等），根据它决定下一步。
- 不确定该怎么玩时调用 get_guide 读攻略（游戏常识和第一年路线）。如果上面已经有“游戏常识”一节，就不用再读。
- 攻略还有专题（钓鱼、采矿、节日等），get_guide 传 topic 读：去钓鱼前看看哪片水域有值钱的鱼，下矿前看看要带什么，节日当天看看怎么参加。
- get_state 最前面的【你的长期笔记】是之前（可能是别的会话）用 update_notes 写下的目标和计划：接着它玩，计划有变化就用 update_notes 更新，换会话也不会丢。
- 一个工具执行完会返回结果文字；动作执行期间游戏时间照常流逝，你思考时游戏时间是冻结的。
- 长动作（fish、water_crops、wait 等）可能要几十秒到几分钟，fish 一次建议 casts 不超过 5。
- 游戏里按 F8 启动的内置 AI 和你不能同时操作；如果工具返回“内置 AI 正在运行”，请提醒玩家先按 F8 停止它。";

        private readonly ModEntry mod;
        private readonly HttpListener listener = new();
        private CancellationTokenSource cts;

        /// <summary>最近一次 MCP 请求的时间（UTC ticks），用于判断客户端是否在“玩”。</summary>
        private long lastActivityTicks;

        public int Port { get; }

        public McpServer(ModEntry mod, int port)
        {
            this.mod = mod;
            Port = port;
        }

        public bool IsActive(int idleSeconds)
        {
            long last = Interlocked.Read(ref lastActivityTicks);
            return last > 0 && DateTime.UtcNow.Ticks - last < TimeSpan.FromSeconds(Math.Max(10, idleSeconds)).Ticks;
        }

        public void Start()
        {
            listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            listener.Start();
            cts = new CancellationTokenSource();
            Task.Run(() => AcceptLoop(cts.Token));
        }

        public void Dispose()
        {
            cts?.Cancel();
            try
            {
                listener.Close();
            }
            catch
            {
                // 进程退出时可能已经关了
            }
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch
                {
                    if (ct.IsCancellationRequested || !listener.IsListening)
                        return;
                    continue;
                }
                // 每个请求单独处理：工具调用可能要几分钟，不能挡住 ping 等请求
                _ = Task.Run(() => Handle(ctx));
            }
        }

        private async Task Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var resp = ctx.Response;
            try
            {
                // 防 DNS rebinding：浏览器发来的请求会带 Origin，只接受本机来源
                string origin = req.Headers["Origin"];
                if (!string.IsNullOrEmpty(origin) && !IsLocalOrigin(origin))
                {
                    await Write(resp, 403, null);
                    return;
                }
                if (req.Url?.AbsolutePath.TrimEnd('/') == "/settings")
                {
                    await HandleSettings(req, resp);
                    return;
                }
                if (req.Url?.AbsolutePath.TrimEnd('/') != "/mcp")
                {
                    await Write(resp, 404, null);
                    return;
                }
                if (req.HttpMethod == "GET" || req.HttpMethod == "DELETE")
                {
                    // 不提供服务器主动推送的 SSE 流，也没有需要删除的会话
                    resp.AddHeader("Allow", "POST");
                    await Write(resp, 405, null);
                    return;
                }
                if (req.HttpMethod != "POST")
                {
                    await Write(resp, 405, null);
                    return;
                }

                string body;
                using (var rd = new StreamReader(req.InputStream, Encoding.UTF8))
                    body = await rd.ReadToEndAsync();
                Interlocked.Exchange(ref lastActivityTicks, DateTime.UtcNow.Ticks);

                JsonNode parsed;
                try
                {
                    parsed = JsonNode.Parse(body);
                }
                catch
                {
                    await Write(resp, 400, Error(null, -32700, "Parse error"));
                    return;
                }

                if (parsed is JsonArray batch)
                {
                    var results = new JsonArray();
                    foreach (var msg in batch)
                    {
                        var r = await Dispatch(msg as JsonObject);
                        if (r != null)
                            results.Add(r);
                    }
                    await Write(resp, results.Count > 0 ? 200 : 202, results.Count > 0 ? results : null);
                }
                else if (parsed is JsonObject call && call["method"]?.GetValue<string>() == "tools/call"
                         && (req.Headers["Accept"] ?? "").Contains("text/event-stream"))
                {
                    await WriteStreamed(resp, Dispatch(call));
                }
                else
                {
                    var r = await Dispatch(parsed as JsonObject);
                    await Write(resp, r != null ? 200 : 202, r);
                }
                // 工具调用结束时也算一次活动，避免长动作后立刻判定为空闲
                Interlocked.Exchange(ref lastActivityTicks, DateTime.UtcNow.Ticks);
            }
            catch (Exception ex)
            {
                // 这里绝不能再抛异常，否则响应发不出去，客户端会一直等到超时
                try
                {
                    mod.Monitor?.Log("MCP 请求处理出错：" + ex, LogLevel.Warn);
                }
                catch
                {
                    // 忽略日志失败
                }
                try
                {
                    await Write(resp, 500, Error(null, -32603, ex.Message));
                }
                catch
                {
                    // 连接可能已经断了
                }
            }
        }

        private static bool IsLocalOrigin(string origin)
        {
            return Uri.TryCreate(origin, UriKind.Absolute, out var u)
                && (u.Host == "localhost" || u.Host == "127.0.0.1" || u.Host == "[::1]" || u.Host == "::1");
        }

        /// <summary>处理一条 JSON-RPC 消息；通知（没有 id）返回 null。</summary>
        internal async Task<JsonNode> Dispatch(JsonObject msg)
        {
            if (msg == null)
                return Error(null, -32600, "Invalid Request");
            string method = msg["method"]?.GetValue<string>();
            var id = msg["id"]?.DeepCloneValue();
            if (id == null)
                return null; // notifications/initialized 等通知，或客户端发来的响应
            var p = msg["params"] as JsonObject;

            switch (method)
            {
                case "initialize":
                {
                    string want = p?["protocolVersion"]?.GetValue<string>();
                    string version = Array.IndexOf(SupportedVersions, want) >= 0 ? want : SupportedVersions[0];
                    return Result(id, new JsonObject
                    {
                        ["protocolVersion"] = version,
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                        ["serverInfo"] = new JsonObject { ["name"] = "dsh-stardew-blind-whale-farmer", ["title"] = "大肥鱼种白饭 Blind Whale Farmer（星露谷）", ["version"] = typeof(McpServer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0" },
                        ["instructions"] = Instructions
                    });
                }
                case "ping":
                    return Result(id, new JsonObject());
                case "tools/list":
                    return Result(id, new JsonObject { ["tools"] = ToolList() });
                case "tools/call":
                {
                    string name = p?["name"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(name))
                        return Error(id, -32602, "缺少工具名");
                    if (!InfoTools.Contains(name) && !mod.Tools.IsKnown(name))
                        return Error(id, -32602, $"没有叫 {name} 的工具");
                    string text = await CallTool(name, p["arguments"] as JsonObject);
                    bool isError = !text.StartsWith("成功") && !InfoTools.Contains(name);
                    return Result(id, new JsonObject
                    {
                        ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } },
                        ["isError"] = isError
                    });
                }
                default:
                    return Error(id, -32601, "Method not found: " + method);
            }
        }

        private JsonArray ToolList()
        {
            var list = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "get_state",
                    ["description"] = "读取当前游戏状态的文字描述：时间天气、体力金钱、当前界面（对话/商店/信件等）、位置、背包、出口、人物和动物、作物、附近物体坐标、以你为中心的字符地图、任务。每次决定行动前先调用。",
                    ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
                },
                new JsonObject
                {
                    ["name"] = "get_guide",
                    ["description"] = "读取攻略。不传 topic 读主攻略（游戏常识、装备、第一年路线）；传专题名读专题"
                                      + (Guide.Topics.Count > 0 ? $"（{string.Join("、", Guide.Topics)}）" : "")
                                      + "，比如去钓鱼前读“钓鱼”看哪里有什么鱼，下矿前读“采矿”，节日当天读“节日”。不花游戏时间。",
                    ["inputSchema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["topic"] = new JsonObject { ["type"] = "string", ["description"] = "专题名，不传读主攻略" }
                        }
                    }
                }
            };
            // Schema 是共用对象，复制一份再取用（JsonNode 只能有一个父节点）
            var schema = (JsonArray)JsonNode.Parse(mod.Tools.Schema.ToJsonString());
            foreach (var t in schema)
            {
                var fn = t["function"];
                list.Add(new JsonObject
                {
                    ["name"] = fn["name"].GetValue<string>(),
                    ["description"] = fn["description"].GetValue<string>(),
                    ["inputSchema"] = JsonNode.Parse(fn["parameters"].ToJsonString())
                });
            }
            return list;
        }

        private async Task<string> CallTool(string name, JsonObject args)
        {
            if (mod.Agent.Running)
                return "失败：游戏里的内置 AI 正在运行，请先在游戏里按 " + mod.Config.ToggleKey + " 停止它，再通过 MCP 操作";
            if (mod.PlayerInControl && !InfoTools.Contains(name) && name != "update_notes")
                return $"失败：玩家按 {mod.Config.TakeOverKey} 接管了游戏，现在由玩家自己操作，你的动作都会被拒绝。"
                       + $"不要再尝试游戏动作：停下来向玩家汇报刚才在做什么、遇到了什么问题，等玩家在游戏里再按 {mod.Config.TakeOverKey} 交还、并告诉你继续";

            string argText = args?.ToJsonString() ?? "{}";
            string result;
            try
            {
                if (name == "get_state")
                {
                    result = await mod.MainThread.Invoke(() =>
                        Context.IsWorldReady ? mod.Observer.Build() : "游戏还没有进入存档（在标题画面或正在加载）。请玩家先读取存档。").ConfigureAwait(false);
                    // 本次游玩进度和玩家的额外要求放最前面（它们可以在 DSH 设置里随时改，不在连接时的说明里）
                    var head = new List<string>();
                    if (mod.PlayerInControl)
                        head.Add($"【玩家接管中】玩家按 {mod.Config.TakeOverKey} 接管了游戏，你的游戏动作都会被拒绝。停下来向玩家汇报，等玩家交还");
                    if (mod.Session?.Status() is string session)
                        head.Add("【" + session + "】");
                    if (!string.IsNullOrWhiteSpace(mod.Config.ExtraInstructions))
                        head.Add("【玩家的额外要求】" + mod.Config.ExtraInstructions.Trim());
                    // 笔记按存档保存（模组 data 文件夹），内置 AI 每步都能看到；MCP 客户端换了会话也要能接着上次的计划玩
                    if (Context.IsWorldReady)
                        head.Add("【你的长期笔记】" + (string.IsNullOrWhiteSpace(mod.Agent?.Notes)
                            ? "（空。建议用 update_notes 写下目标和计划，换会话后也能接着玩）"
                            : mod.Agent.Notes.Trim()));
                    if (head.Count > 0)
                        result = string.Join("\n", head) + "\n\n" + result;
                }
                else if (name == "get_guide")
                {
                    string topic = args?["topic"]?.GetValue<string>();
                    string text = Guide.Get(topic);
                    string list = Guide.Topics.Count > 0 ? $"可读的专题：{string.Join("、", Guide.Topics)}" : "";
                    result = text != null
                        ? text + (string.IsNullOrWhiteSpace(topic) && list != "" ? "\n\n" + list + "（get_guide 传 topic）" : "")
                        : string.IsNullOrWhiteSpace(topic)
                            ? "没有加载攻略文件（模组目录 guide/ 下的 .md，config.json 的 GuideFile 指定），按基本操作规则玩即可"
                            : $"没有叫“{topic}”的专题。{list}";
                }
                else
                {
                    mod.Hud.SetAction($"[MCP] {name} {argText}");
                    result = await mod.Tools.Execute(name, argText).ConfigureAwait(false);
                    mod.Hud.SetAction($"[MCP] {name} → {Shorten(result, 80)}");
                }
            }
            catch (Exception ex)
            {
                result = $"错误：{ex.GetType().Name}: {ex.Message}";
            }
            if (!InfoTools.Contains(name))
                mod.WriteTranscript($"[{mod.CurrentStamp}] [MCP] {name}({Shorten(argText, 200)}) → {Shorten(result, 400)}");
            return result;
        }

        private static string Shorten(string s, int n)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        private static JsonObject Result(JsonNode id, JsonNode result) =>
            new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

        private static JsonObject Error(JsonNode id, int code, string message) =>
            new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

        /// <summary>这次游戏启动的标识：DSH 插件看到它变了（游戏重开过），就把设置再推一遍。</summary>
        private readonly string instance = Guid.NewGuid().ToString("N");

        /// <summary>DSH 插件上次推来的设置版本号（插件自己算的），用来判断要不要重推。</summary>
        private string lastRevision = "";

        /// <summary>
        /// /settings：给 DSH 的 dsh-stardew-blind-whale-farmer 插件用。GET 返回当前设置和本次游玩进度；POST {revision, settings} 应用设置。
        /// 和 /mcp 一样只监听 127.0.0.1，并拒绝网页来源（Origin 检查在 Handle 里）。
        /// </summary>
        private async Task HandleSettings(HttpListenerRequest req, HttpListenerResponse resp)
        {
            if (req.HttpMethod == "POST")
            {
                string body;
                using (var rd = new StreamReader(req.InputStream, Encoding.UTF8))
                    body = await rd.ReadToEndAsync();
                if (JsonNode.Parse(body) is not JsonObject msg || msg["settings"] is not JsonObject settings)
                {
                    await Write(resp, 400, new JsonObject { ["error"] = "需要 {revision, settings}" });
                    return;
                }
                string log = mod.ApplyRemoteSettings(settings);
                lastRevision = msg["revision"]?.GetValue<string>() ?? "";
                mod.Monitor?.Log(log, LogLevel.Info);
                await Write(resp, 200, SettingsSnapshot(log));
                return;
            }
            if (req.HttpMethod == "GET")
            {
                await Write(resp, 200, SettingsSnapshot(null));
                return;
            }
            resp.AddHeader("Allow", "GET, POST");
            await Write(resp, 405, null);
        }

        private JsonObject SettingsSnapshot(string message)
        {
            var c = mod.Config;
            return new JsonObject
            {
                ["instance"] = instance,
                ["revision"] = lastRevision,
                ["message"] = message,
                ["settings"] = new JsonObject
                {
                    ["sessionDays"] = c.SessionDays,
                    ["sessionMinutes"] = c.SessionMinutes,
                    ["freezeTime"] = c.McpFreezeTime,
                    ["idleSeconds"] = c.McpIdleSeconds,
                    ["extraInstructions"] = c.ExtraInstructions,
                    ["guideFile"] = c.GuideFile
                },
                ["session"] = mod.Session?.Status() ?? "不限时长"
            };
        }

        private const int KeepAliveMs = 15000;

        /// <summary>
        /// 工具调用用 SSE 回复：先发响应头，动作执行期间每 15 秒发一行注释（": keep-alive"），结束时发结果事件。
        /// 客户端用 Node 自带的 fetch 时，响应头 5 分钟内不到、或者 5 分钟没收到数据就会报 “fetch failed”，
        /// 而下矿、钓鱼最长要 15 分钟。客户端中途断开（比如用户按了停止）时，游戏里的动作也停下，不在没人看的情况下继续跑。
        /// </summary>
        private async Task WriteStreamed(HttpListenerResponse resp, Task<JsonNode> work)
        {
            resp.StatusCode = 200;
            resp.ContentType = "text/event-stream; charset=utf-8";
            resp.Headers["Cache-Control"] = "no-cache";
            resp.SendChunked = true;
            var output = resp.OutputStream;
            async Task Send(string text)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                await output.WriteAsync(bytes, 0, bytes.Length);
                await output.FlushAsync();
            }

            try
            {
                await Send(": start\n\n");
                while (await Task.WhenAny(work, Task.Delay(KeepAliveMs)) != work)
                {
                    await Send(": keep-alive\n\n");
                    Interlocked.Exchange(ref lastActivityTicks, DateTime.UtcNow.Ticks);
                }
                var result = await work;
                await Send("event: message\ndata: " + (result ?? Error(null, -32603, "no result")).ToJsonString() + "\n\n");
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException)
            {
                if (!work.IsCompleted)
                {
                    mod.MainThread?.CancelCurrent("MCP 客户端断开了连接，动作已中止");
                    mod.Monitor?.Log("MCP 客户端在动作执行中断开了连接，已中止当前动作。", LogLevel.Info);
                }
            }
            finally
            {
                try
                {
                    resp.Close();
                }
                catch
                {
                    // 连接已经断了
                }
            }
        }

        private static async Task Write(HttpListenerResponse resp, int status, JsonNode body)
        {
            resp.StatusCode = status;
            if (body != null)
            {
                var bytes = Encoding.UTF8.GetBytes(body.ToJsonString());
                resp.ContentType = "application/json; charset=utf-8";
                resp.ContentLength64 = bytes.Length;
                await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            }
            resp.Close();
        }
    }

    internal static class JsonNodeExtensions
    {
        /// <summary>复制 id（数字或字符串），net6 没有 DeepClone。</summary>
        public static JsonNode DeepCloneValue(this JsonNode node) => node == null ? null : JsonNode.Parse(node.ToJsonString());
    }
}
