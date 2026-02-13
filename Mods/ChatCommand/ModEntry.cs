using System.Reflection;
using Force.DeepCloner;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace ChatCommand;

internal class CommandInvoker
{

    private object commandManager;


    private MethodInfo getCommandMethod;


    private PropertyInfo callbackPropInfo;


    private MethodInfo cmdInvokeMethod;

    private IMonitor monitor;

    public CommandInvoker(IModHelper helper, IMonitor monitor)
    {
        this.monitor = monitor;
        this.ObtainGetCommandMethod(helper);
    }

    private void ObtainGetCommandMethod(IModHelper helper)
    {
        FieldInfo commandManagerFieldInfo = helper.ConsoleCommands.GetType().GetField("CommandManager", BindingFlags.Instance | BindingFlags.NonPublic);
        if (commandManagerFieldInfo == null)
        {
            this.monitor.Log("Could not obtain CommandManager field info", LogLevel.Error);
            return;
        }
        this.commandManager = commandManagerFieldInfo.GetValue(helper.ConsoleCommands);
        if (this.commandManager == null)
        {
            this.monitor.Log("Could not obtain ConsoleCommands object", LogLevel.Error);
            return;
        }
        this.getCommandMethod = this.commandManager.GetType().GetMethod("Get");
        if (this.getCommandMethod == null)
        {
            this.monitor.Log("Could not obtain ConsoleCommands.Get method", LogLevel.Error);
            return;
        }
        Type commandType = this.getCommandMethod.ReturnType;
        PropertyInfo[] propertyInfos = commandType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo[] array = propertyInfos;
        foreach (PropertyInfo propertyInfo in array)
        {
            if (propertyInfo.Name == "Callback")
            {
                this.callbackPropInfo = propertyInfo;
            }
        }
        if (this.callbackPropInfo == null)
        {
            this.monitor.Log("Could not obtain Command.Callback PropertyInfo", LogLevel.Error);
            return;
        }
        this.cmdInvokeMethod = this.callbackPropInfo.PropertyType.GetMethod("Invoke");
        if (this.cmdInvokeMethod == null)
        {
            this.monitor.Log("Could not obtain Action.Invoke MethodInfo", LogLevel.Error);
        }
    }

    public bool InvokeCommand(string name, string[] args)
    {
        object[] getArguments = new object[1] { name };
        object command = this.getCommandMethod?.Invoke(this.commandManager, getArguments);
        if (command == null)
        {
            this.monitor.Log("Could not obtain `" + name + "` command", LogLevel.Error);
            return false;
        }
        object callback = this.callbackPropInfo?.GetValue(command);
        if (callback == null)
        {
            this.monitor.Log("Could not obtain Callback value", LogLevel.Error);
            return false;
        }
        if (this.cmdInvokeMethod == null)
        {
            return false;
        }
        object[] callbackArgs = new object[2] { name, args };
        this.cmdInvokeMethod.Invoke(callback, callbackArgs);
        return true;
    }
}

public class ModEntry : Mod
{

    private int CurrentMessageCount = 0;

    private readonly static string CommandPrefix = "!cmd";
    private readonly static string WarnPrefix = ">";
    private CommandInvoker cmdinvoker;
    public override void Entry(IModHelper helper)
    {
        helper.ConsoleCommands.Add("test", "A test command", this.TestCommand);
        //
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        cmdinvoker = new CommandInvoker(helper, this.Monitor);

    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.Helper.Events.GameLoop.OneSecondUpdateTicked += this.OnOneSecondUpdateTicked;
    }

    private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
    {   
        
        if (this.CurrentMessageCount < Game1.chatBox.messages.Count)
        {
            this.CurrentMessageCount = Game1.chatBox.messages.Count.DeepClone();
            List<ChatMessage> messages = base.Helper.Reflection.GetField<List<ChatMessage>>(Game1.chatBox, "messages").GetValue();
            List<ChatSnippet> messagetoconvert = messages[^1].message;
            string actualmessage = ChatMessage.makeMessagePlaintext(messagetoconvert, false);
            Monitor.Log(actualmessage, LogLevel.Info);

            LocalizedContentManager.CurrentLanguageCode.ToString();
            string fontFragment;
            string cmdText;
            if (!actualmessage.Contains(WarnPrefix) || !actualmessage.Contains(CommandPrefix))
            {   
                Monitor.Log("Not a command message", LogLevel.Debug);
                return;
            }
            Monitor.Log(actualmessage, LogLevel.Info);
            switch (LocalizedContentManager.CurrentLanguageCode)
            {
                case LocalizedContentManager.LanguageCode.zh:
                    fontFragment = actualmessage.Split("：")[0];
                    Monitor.Log(fontFragment, LogLevel.Info);
                    cmdText = actualmessage.Replace($"{fontFragment}：{CommandPrefix}{WarnPrefix}", "");
                    Monitor.Log($"cmdText={cmdText}", LogLevel.Info);
                    break;

                case LocalizedContentManager.LanguageCode.en:
                    fontFragment = actualmessage.Split(": ")[0];
                    cmdText = actualmessage.Replace($"{fontFragment}: {CommandPrefix}{WarnPrefix}", "");
                    break;
                //默认英文               
                default:
                    fontFragment = actualmessage.Split(": ")[0];
                    cmdText = actualmessage.Replace($"{fontFragment}: {CommandPrefix}{WarnPrefix}", "");
                    break;

            }
            cmdText = cmdText.Replace(WarnPrefix, "").Replace(CommandPrefix, "");
            Monitor.Log(cmdText, LogLevel.Info);
            this.InvokeCommand(cmdText);

            return;
        }
    }



    private void TestCommand(string arg1, string[] arg2)
    {
        //throw new NotImplementedException();
        //Game1.Multiplayer.receiveChatMessage(Game1.player, 0, LocalizedContentManager.CurrentLanguageCode, "hello");
        cmdinvoker.InvokeCommand("alos.server", arg2);
        //Monitor.Log("Test", LogLevel.Info);
    }

    public void InvokeCommand(string command)
    {
        Monitor.Log($"InvokeCommand {command}", LogLevel.Debug);
        cmdinvoker.InvokeCommand(command, new string[] {});
    }
}
