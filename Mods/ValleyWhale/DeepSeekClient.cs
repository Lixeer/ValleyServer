using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace BlindWhaleFarmer
{
    internal sealed class ToolCall
    {
        public string Id;
        public string Name;
        public string Arguments;
    }

    internal sealed class ChatReply
    {
        public string Content = "";
        public List<ToolCall> ToolCalls = new();
        public int PromptTokens;
        public int CompletionTokens;
    }

    /// <summary>OpenAI 兼容的 Chat Completions 客户端（DeepSeek 官方接口即此格式）。</summary>
    internal sealed class DeepSeekClient : IDisposable
    {
        private readonly HttpClient http;
        private readonly ModConfig config;

        public DeepSeekClient(ModConfig config)
        {
            this.config = config;
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(10, config.RequestTimeoutSeconds)) };
        }

        public static string ResolveApiKey(ModConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.ApiKey))
                return config.ApiKey.Trim();
            return Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")?.Trim() ?? "";
        }

        public async Task<ChatReply> Chat(JsonArray messages, JsonArray tools, CancellationToken ct)
        {
            // JsonNode 只能有一个父节点：tools 是每次请求共用的同一个对象，必须复制一份再挂上去，
            // 否则第二次请求会抛 “The node already has a parent”
            var body = new JsonObject
            {
                ["model"] = config.Model,
                ["messages"] = JsonNode.Parse(messages.ToJsonString()),
                ["tools"] = JsonNode.Parse(tools.ToJsonString()),
                ["tool_choice"] = "auto",
                ["temperature"] = config.Temperature,
                ["max_tokens"] = config.MaxTokens,
                ["stream"] = false
            };
            string json = body.ToJsonString();
            string url = config.BaseUrl.TrimEnd('/') + "/chat/completions";

            Exception last = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, url);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ResolveApiKey(config));
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
                    string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (resp.StatusCode == (HttpStatusCode)429 || (int)resp.StatusCode >= 500)
                    {
                        last = new Exception($"HTTP {(int)resp.StatusCode}: {Truncate(text, 300)}");
                        await Task.Delay(TimeSpan.FromSeconds(3 * (attempt + 1)), ct).ConfigureAwait(false);
                        continue;
                    }
                    if (!resp.IsSuccessStatusCode)
                        throw new Exception($"HTTP {(int)resp.StatusCode}: {Truncate(text, 500)}");
                    return Parse(text);
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                    last = new Exception("请求超时");
                }
                catch (HttpRequestException ex)
                {
                    last = ex;
                    await Task.Delay(TimeSpan.FromSeconds(3 * (attempt + 1)), ct).ConfigureAwait(false);
                }
            }
            throw last ?? new Exception("请求失败");
        }

        private static ChatReply Parse(string text)
        {
            var root = JsonNode.Parse(text);
            var msg = root?["choices"]?[0]?["message"];
            var reply = new ChatReply
            {
                Content = msg?["content"]?.GetValue<string>() ?? "",
                PromptTokens = root?["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0,
                CompletionTokens = root?["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0
            };
            if (msg?["tool_calls"] is JsonArray calls)
            {
                foreach (var c in calls)
                {
                    reply.ToolCalls.Add(new ToolCall
                    {
                        Id = c?["id"]?.GetValue<string>(),
                        Name = c?["function"]?["name"]?.GetValue<string>(),
                        Arguments = c?["function"]?["arguments"]?.GetValue<string>() ?? "{}"
                    });
                }
            }
            return reply;
        }

        private static string Truncate(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n) + "…";

        public void Dispose() => http.Dispose();
    }
}
