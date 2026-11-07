using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using StardewModdingAPI;

namespace BlindWhaleFarmer
{
    /// <summary>动作协程的最终结果。协程 yield 它就表示结束。</summary>
    internal sealed class ActionResult
    {
        public bool Ok { get; }
        public string Message { get; }

        private ActionResult(bool ok, string message)
        {
            Ok = ok;
            Message = message;
        }

        public static ActionResult Success(string msg) => new ActionResult(true, "成功：" + msg);
        public static ActionResult Fail(string msg) => new ActionResult(false, "失败：" + msg);
    }

    /// <summary>子协程用来回传结果的容器（子协程只 yield null 等待）。</summary>
    internal sealed class Outcome
    {
        public bool Ok { get; private set; } = true;
        public string Message { get; private set; } = "";

        public void Fail(string msg)
        {
            Ok = false;
            Message = msg;
        }

        public void Succeed(string msg = "")
        {
            Ok = true;
            Message = msg;
        }
    }

    /// <summary>
    /// 把后台线程（调用大模型）里的请求切换到游戏主线程执行。
    /// 游戏状态只能在主线程读写，所以观察和动作都通过这里排队。
    /// </summary>
    internal sealed class MainThread
    {
        private readonly IMonitor monitor;
        private readonly ConcurrentQueue<Action> queue = new();

        private IEnumerator<object> current;
        private TaskCompletionSource<string> currentTcs;
        private int currentTicks;
        private int currentMaxTicks;
        /// <summary>正在跑的动作叫什么、什么时候开始的（“上一个动作还在执行”时告诉 AI）。</summary>
        private string currentName;
        private DateTime currentStartedUtc;
        /// <summary>动作开始时的生命值；-1 表示这个动作不因掉血中断。</summary>
        private int currentStartHealth = -1;

        /// <summary>读游戏状态；离线测试里没有游戏实例会抛异常，这时返回 fallback。</summary>
        private static T Safe<T>(Func<T> read, T fallback = default)
        {
            try
            {
                return read();
            }
            catch
            {
                return fallback;
            }
        }

        public MainThread(IMonitor monitor)
        {
            this.monitor = monitor;
        }

        public bool IsBusy => current != null;

        public Task<T> Invoke<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(() =>
            {
                try
                {
                    tcs.SetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }

        private bool currentInterruptOnChat;

        /// <summary>
        /// 在主线程上逐帧运行一个动作协程，返回其结果文字。
        /// preemptWhenStuck：处理界面的动作（dialogue、skip_event 等）在剧情/界面打开时可以打断正在跑的动作——
        /// 那个动作被剧情挡住本来也走不动，不打断的话两边互相等，会卡死。
        /// </summary>
        public Task<string> RunAction(Func<IEnumerable<object>> factory, int maxSeconds, bool preemptWhenStuck = false, bool interruptOnDamage = false, string name = null, bool interruptOnChat = false)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(() =>
            {
                if (current != null && preemptWhenStuck && Safe(() => StardewValley.Game1.eventUp || StardewValley.Game1.activeClickableMenu != null))
                    Finish("失败：被剧情/界面打断，已中止（先处理剧情或界面，再重新执行）");
                if (current != null)
                {
                    int sec = (int)(DateTime.UtcNow - currentStartedUtc).TotalSeconds;
                    string what = currentName != null ? $"（{currentName}，已经执行 {sec / 60} 分 {sec % 60} 秒，最长 {currentMaxTicks / 3600} 分钟）" : "";
                    tcs.SetResult($"失败：上一个动作{what}还在执行，它结束前其他动作都会被拒绝。可以用 get_state 看看情况，过一会儿再试");
                    return;
                }
                try
                {
                    current = factory().GetEnumerator();
                    currentTcs = tcs;
                    currentName = name;
                    currentStartedUtc = DateTime.UtcNow;
                    currentTicks = 0;
                    currentMaxTicks = Math.Max(1, maxSeconds) * 60;
                    currentInterruptOnChat = interruptOnChat;
                    currentStartHealth = interruptOnDamage ? Safe(() => StardewValley.Game1.player?.health ?? -1, -1) : -1;
                }
                catch (Exception ex)
                {
                    current = null;
                    tcs.SetResult("错误：" + ex.Message);
                }
            });
            return tcs.Task;
        }

        /// <summary>每帧由 ModEntry 调用。</summary>
        public void Tick(bool worldReady)
        {
            while (queue.TryDequeue(out var action))
                action();

            if (current == null)
                return;

            if (!worldReady)
            {
                Finish("失败：游戏世界未就绪（可能回到了标题画面）");
                return;
            }

            currentTicks++;
            if (currentTicks > currentMaxTicks)
            {
                Finish("失败：动作超时，已中止");
                return;
            }
            // 联机时有人说话：先停下听（结果后面会附上说了什么）
            if (currentInterruptOnChat && Safe(() => GameActions.ChatWantsAttention(), false))
            {
                Finish("失败：有人说话了，已停下（动作没做完，听完可以接着做）");
                return;
            }
            // 非打怪动作途中掉血（被怪打了）：停下，让 AI 先清怪（结果后面会附上附近的怪）
            int health = currentStartHealth >= 0 ? Safe(() => StardewValley.Game1.player.health, currentStartHealth) : -1;
            if (currentStartHealth >= 0 && health < currentStartHealth)
            {
                Finish($"失败：干活时被攻击了（生命 {currentStartHealth} → {health}），已停下。先用 fight 清掉附近的怪再继续");
                return;
            }

            try
            {
                if (!current.MoveNext())
                {
                    Finish("成功：完成");
                    return;
                }
                if (current.Current is ActionResult result)
                    Finish(result.Message);
            }
            catch (Exception ex)
            {
                monitor.Log($"动作执行出错：{ex}", LogLevel.Warn);
                Finish("错误：" + ex.Message);
            }
        }

        public void Cancel(string reason)
        {
            if (current != null)
                Finish("失败：" + reason);
        }

        /// <summary>从任意线程中止正在跑的动作（在主线程下一帧执行）；那时已经换成别的动作就不动它。</summary>
        public void CancelCurrent(string reason)
        {
            var tcs = currentTcs;
            if (tcs == null)
                return;
            queue.Enqueue(() =>
            {
                if (currentTcs == tcs)
                    Cancel(reason);
            });
        }

        private void Finish(string message)
        {
            var tcs = currentTcs;
            (current as IDisposable)?.Dispose();
            current = null;
            currentTcs = null;
            tcs?.TrySetResult(message);
        }
    }
}
