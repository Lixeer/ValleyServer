using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using StardewModdingAPI;

namespace BlindWhaleFarmer
{
    /// <summary>
    /// 联机时打中文用的小输入框：游戏聊天框里看不到输入法的候选词，所以按一个键弹出普通的 Windows 窗口，
    /// 回车后把文字交回游戏。窗口放在单独的进程里（PowerShell + WinForms），不用给模组加界面库，也卡不到游戏。
    /// </summary>
    internal sealed class ChatInput
    {
        private const string Script = @"
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Name Native -Namespace BlindWhaleFarmer -MemberDefinition '[DllImport(""user32.dll"")] public static extern bool SetForegroundWindow(IntPtr h);'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$script:text = ''
$form = New-Object Windows.Forms.Form
$form.Text = '说话（回车发送，Esc 取消）'
$form.TopMost = $true
$form.FormBorderStyle = 'FixedToolWindow'
$form.StartPosition = 'CenterScreen'
$form.ClientSize = New-Object Drawing.Size(560, 38)
$form.KeyPreview = $true
$box = New-Object Windows.Forms.TextBox
$box.Dock = 'Fill'
$box.MaxLength = 300
$box.Font = New-Object Drawing.Font('Microsoft YaHei UI', 14)
$form.Controls.Add($box)
$form.Add_KeyDown({
    if ($_.KeyCode -eq 'Return') { $script:text = $box.Text; $_.SuppressKeyPress = $true; $form.Close() }
    elseif ($_.KeyCode -eq 'Escape') { $_.SuppressKeyPress = $true; $form.Close() }
})
# 打开就是中文：先切到中文输入法（装了的话），再打开它的中文状态
$box.ImeMode = 'On'
$form.Add_Shown({
    $form.Activate()
    $chinese = [Windows.Forms.InputLanguage]::InstalledInputLanguages | Where-Object { $_.Culture.Name -like 'zh*' } | Select-Object -First 1
    if ($chinese) { [Windows.Forms.InputLanguage]::CurrentInputLanguage = $chinese }
    $box.Focus()
})
[void]$form.ShowDialog()
$game = Get-Process -Id __PID__ -ErrorAction SilentlyContinue
if ($game) { [void][BlindWhaleFarmer.Native]::SetForegroundWindow($game.MainWindowHandle) }
if ($script:text.Trim()) { [Console]::Out.WriteLine($script:text) }
";

        private readonly IMonitor monitor;
        private readonly ConcurrentQueue<string> lines = new();
        private Process box;

        public ChatInput(IMonitor monitor) => this.monitor = monitor;

        /// <summary>打开输入框；已经开着就不再开。</summary>
        public void Open()
        {
            if (box is { HasExited: false })
                return;
            try
            {
                string script = Script.Replace("__PID__", Environment.ProcessId.ToString());
                box = Process.Start(new ProcessStartInfo("powershell.exe")
                {
                    Arguments = "-NoProfile -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                });
                box.OutputDataReceived += (_, e) =>
                {
                    string line = e.Data?.Trim('﻿', ' ');
                    if (!string.IsNullOrEmpty(line))
                        lines.Enqueue(line);
                };
                box.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        monitor.Log("输入框：" + e.Data, LogLevel.Trace);
                };
                box.BeginOutputReadLine();
                box.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                monitor.Log("打不开聊天输入框（只支持 Windows）：" + ex.Message, LogLevel.Warn);
            }
        }

        /// <summary>取出输入框发回来的一句话（没有返回 null）。主线程每帧调用。</summary>
        public string TakeLine() => lines.TryDequeue(out string line) ? line : null;
    }
}
