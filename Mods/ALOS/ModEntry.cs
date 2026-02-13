
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Always_On_Server.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace Always_On_Server
{
    public class ModEntry : Mod
    {
        /// <summary>The mod configuration from the player.</summary>
        private ModConfig Config;

        private int gameTicks; //stores 1s game ticks for pause code
        private int skipTicks; //stores 1s game ticks for skip code
        private int gameClockTicks; //stores in game clock change
        private int numPlayers; //stores number of players

        /// <summary>Whether the main player is currently being automated.</summary>
        private bool IsAutomating;
        public int bedX;
        public int bedY;
        public bool clientPaused;
        private string lastInviteCode;

        //debug tools
        private bool debug;
        private bool shippingMenuActive;

        private readonly Dictionary<string, int> PreviousFriendships = new Dictionary<string, int>();  //stores friendship values

        public int connectionsCount = 1;

        private bool eventCommandUsed;

        private bool eggHuntAvailable; //is egg festival ready start timer for triggering eggHunt Event
        private int eggHuntCountDown; //to trigger egg hunt after set time

        private bool flowerDanceAvailable;
        private int flowerDanceCountDown;

        private bool luauSoupAvailable;
        private int luauSoupCountDown;

        private bool jellyDanceAvailable;
        private int jellyDanceCountDown;

        private bool grangeDisplayAvailable;
        private int grangeDisplayCountDown;

        private bool goldenPumpkinAvailable;
        private int goldenPumpkinCountDown;

        private bool iceFishingAvailable;
        private int iceFishingCountDown;

        private bool winterFeastAvailable;
        private int winterFeastCountDown;
        //variables for current time and date
        int currentTime = Game1.timeOfDay;
        SDate currentDate = SDate.Now();
        SDate eggFestival = new SDate(13, "spring");
        SDate dayAfterEggFestival = new SDate(14, "spring");
        SDate flowerDance = new SDate(24, "spring");
        SDate luau = new SDate(11, "summer");
        SDate danceOfJellies = new SDate(28, "summer");
        SDate stardewValleyFair = new SDate(16, "fall");
        SDate spiritsEve = new SDate(27, "fall");
        SDate festivalOfIce = new SDate(8, "winter");
        SDate feastOfWinterStar = new SDate(25, "winter");
        SDate grampasGhost = new SDate(1, "spring", 3);
        ///////////////////////////////////////////////////////

        //variables for timeout reset code

        private int timeOutTicksForReset;
        private int festivalTicksForReset;
        private int shippingMenuTimeoutTicks;
        readonly SDate currentDateForReset = SDate.Now();
        readonly SDate danceOfJelliesForReset = new SDate(28, "summer");
        readonly SDate spiritsEveForReset = new SDate(27, "fall");
        //////////////////////////

        public override void Entry(IModHelper helper)
        {

            this.Config = this.Helper.ReadConfig<ModConfig>();

            helper.ConsoleCommands.Add("alos.server", "Toggles headless 'auto mode' on/off", this.ToggleAutoMode);
            helper.ConsoleCommands.Add("alos.debug_server", "Turns debug mode on/off, lets server run when no players are connected", this.DebugToggle);
            helper.ConsoleCommands.Add("alos.go_to_sleep", "Toggles festivals on/off", this.HandleGoToBed);
		    helper.ConsoleCommands.Add("alos.pause", "Toggles festivals on/off", this.HandlePauseSingle);


            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.Saving += this.OnSaving; // Shipping Menu handler
            helper.Events.Display.MenuChanged += this.OnMenuChanged; // Menu handler
            helper.Events.GameLoop.OneSecondUpdateTicked += this.OnOneSecondUpdateTicked; //game tick event handler
            helper.Events.GameLoop.TimeChanged += this.OnTimeChanged; // Time of day change handler
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked; //handles various events that should occur as soon as they are available
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Display.Rendered += this.OnRendered;
            helper.Events.Specialized.UnvalidatedUpdateTicked += OnUnvalidatedUpdateTick; //used bc only thing that gets throug save window
        }

        private void HandlePauseSingle(string arg1, string[] arg2)
        {
            this.clientPaused = !this.clientPaused;
		    Game1.netWorldState.Value.IsPaused = !this.clientPaused;
        }

        private void HandleGoToBed(string arg1, string[] arg2)
        {
            if (Context.IsWorldReady)
            {
                this.GoToBed();
            }
            throw new NotImplementedException();
        }



        /// <summary>Raised after the player loads a save slot and the world is initialised.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            // turns on server after the game loads
            if (Game1.IsServer)
            {
                //store levels, set in game levels to max
                var data = this.Helper.Data.ReadJsonFile<ModData>($"data/{Constants.SaveFolderName}.json") ?? new ModData();
                data.FarmingLevel = Game1.player.FarmingLevel;
                data.MiningLevel = Game1.player.MiningLevel;
                data.ForagingLevel = Game1.player.ForagingLevel;
                data.FishingLevel = Game1.player.FishingLevel;
                data.CombatLevel = Game1.player.CombatLevel;
                this.Helper.Data.WriteJsonFile($"data/{Constants.SaveFolderName}.json", data);
                Game1.player.farmingLevel.Value = 10;
                Game1.player.miningLevel.Value = 10;
                Game1.player.foragingLevel.Value = 10;
                Game1.player.fishingLevel.Value = 10;
                Game1.player.combatLevel.Value = 10;
                ////////////////////////////////////////
                IsAutomating = true;
                Game1.chatBox.addInfoMessage("The host is in automatic mode!");
                this.Monitor.Log("Auto Mode On!", LogLevel.Info);
            }

        }

        //debug for running with no one online
        private void DebugToggle(string command, string[] args)
        {
            if (Context.IsWorldReady)
            {
                this.debug = !debug;
                this.Monitor.Log($"Server Debug {(debug ? "On" : "Off")}", LogLevel.Info);
            }
        }

        //draw textbox rules
        public static void DrawTextBox(int x, int y, SpriteFont font, string message, int align = 0, float colorIntensity = 1f)
        {
            SpriteBatch spriteBatch = Game1.spriteBatch;
            int width = (int)font.MeasureString(message).X + 32;
            int num = (int)font.MeasureString(message).Y + 21;
            switch (align)
            {
                case 0:
                    IClickableMenu.drawTextureBox(spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x, y, width, num + 4, Color.White * colorIntensity);
                    Utility.drawTextWithShadow(spriteBatch, message, font, new Vector2(x + 16, y + 16), Game1.textColor);
                    break;
                case 1:
                    IClickableMenu.drawTextureBox(spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x - width / 2, y, width, num + 4, Color.White * colorIntensity);
                    Utility.drawTextWithShadow(spriteBatch, message, font, new Vector2(x + 16 - width / 2, y + 16), Game1.textColor);
                    break;
                case 2:
                    IClickableMenu.drawTextureBox(spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x - width, y, width, num + 4, Color.White * colorIntensity);
                    Utility.drawTextWithShadow(spriteBatch, message, font, new Vector2(x + 16 - width, y + 16), Game1.textColor);
                    break;
            }
        }

        /// <summary>Raised after the game draws to the sprite patch in a draw tick, just before the final sprite batch is rendered to the screen.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnRendered(object sender, RenderedEventArgs e)
        {
            //draw a textbox in the top left corner saying Server On
            if (Game1.options.enableServer && IsAutomating)
            {
                int connections = Game1.server.connectionsCount;
                DrawTextBox(5, 100, Game1.dialogueFont, "Auto Mode On");
                DrawTextBox(5, 180, Game1.dialogueFont, $"Press {this.Config.serverHotKey} On/Off");
                float profitMargin = this.Config.profitmargin;
                DrawTextBox(5, 260, Game1.dialogueFont, $"Profit Margin: {profitMargin}%");
                DrawTextBox(5, 340, Game1.dialogueFont, $"{connections} Players Online");
                if (this.lastInviteCode != null)
                    DrawTextBox(5, 420, Game1.dialogueFont, $"Invite Code: {this.lastInviteCode}");
            }
        }

        // When the shipping menu is shown, let's skip it
        //TODO: This code never gets hit
        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            if (IsAutomating)
            {
                if (e.NewMenu is ShippingMenu)
                {
                    shippingMenuActive = true;
                    this.Monitor.Log("Skipping shipping menu");
                    this.Helper.Reflection.GetMethod(Game1.activeClickableMenu, "okClicked").Invoke();
                }
            }
        }

        /// <summary>Raised before the game begins writes data to the save file (except the initial save creation).</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnSaving(object sender, SavingEventArgs e)
        {
            // shipping menu "OK" click through code
            if (IsAutomating)
            {
                this.Monitor.Log("This is the Shipping Menu");
                shippingMenuActive = true;
                if (Game1.activeClickableMenu is ShippingMenu)
                    this.Helper.Reflection.GetMethod(Game1.activeClickableMenu, "okClicked").Invoke();
            }
        }

        // toggles auto mode on/off with console command "server"
        private void ToggleAutoMode(string command, string[] args)
        {
            if (Context.IsWorldReady)
            {
                if (!IsAutomating)
                {
                    Helper.ReadConfig<ModConfig>();
                    IsAutomating = true;


                    this.Monitor.Log("Auto mode on!", LogLevel.Info);
                    Game1.chatBox.addInfoMessage("The host is in automatic mode!");

                    Game1.displayHUD = true;
                    Game1.addHUDMessage(new HUDMessage("Auto Mode On!"));

                    Game1.options.pauseWhenOutOfFocus = false;

                    // store levels, set in game levels to max
                    var data = this.Helper.Data.ReadJsonFile<ModData>($"data/{Constants.SaveFolderName}.json") ?? new ModData();
                    data.FarmingLevel = Game1.player.FarmingLevel;
                    data.MiningLevel = Game1.player.MiningLevel;
                    data.ForagingLevel = Game1.player.ForagingLevel;
                    data.FishingLevel = Game1.player.FishingLevel;
                    data.CombatLevel = Game1.player.CombatLevel;
                    this.Helper.Data.WriteJsonFile($"data/{Constants.SaveFolderName}.json", data);
                    Game1.player.farmingLevel.Value = 10;
                    Game1.player.miningLevel.Value = 10;
                    Game1.player.foragingLevel.Value = 10;
                    Game1.player.fishingLevel.Value = 10;
                    Game1.player.combatLevel.Value = 10;
                    ///////////////////////////////////////////

                }
                else
                {
                    IsAutomating = false;
                    this.Monitor.Log("Auto mode off!", LogLevel.Info);

                    Game1.chatBox.addInfoMessage("The host has returned!");

                    Game1.displayHUD = true;
                    Game1.addHUDMessage(new HUDMessage("Auto Mode Off!"));

                    //set player levels to stored levels
                    var data = this.Helper.Data.ReadJsonFile<ModData>($"data/{Constants.SaveFolderName}.json") ?? new ModData();
                    Game1.player.farmingLevel.Value = data.FarmingLevel;
                    Game1.player.miningLevel.Value = data.MiningLevel;
                    Game1.player.foragingLevel.Value = data.ForagingLevel;
                    Game1.player.fishingLevel.Value = data.FishingLevel;
                    Game1.player.combatLevel.Value = data.CombatLevel;
                    //////////////////////////////////////
                }
            }
        }

        /// <summary>Raised after the player presses a button on the keyboard, controller, or mouse.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            //toggles server on/off with configurable hotkey
            if (Context.IsWorldReady)
            {
                if (e.Button == this.Config.serverHotKey)
                {
                    if (!IsAutomating)
                    {
                        Helper.ReadConfig<ModConfig>();
                        IsAutomating = true;
                        this.Monitor.Log("Auto mode on!", LogLevel.Info);
                        Game1.chatBox.addInfoMessage("The host is in automatic mode!");

                        Game1.displayHUD = true;
                        Game1.addHUDMessage(new HUDMessage("Auto Mode On!"));

                        Game1.options.pauseWhenOutOfFocus = false;
                        // store levels, set in game levels to max
                        var data = this.Helper.Data.ReadJsonFile<ModData>($"data/{Constants.SaveFolderName}.json") ?? new ModData();
                        data.FarmingLevel = Game1.player.FarmingLevel;
                        data.MiningLevel = Game1.player.MiningLevel;
                        data.ForagingLevel = Game1.player.ForagingLevel;
                        data.FishingLevel = Game1.player.FishingLevel;
                        data.CombatLevel = Game1.player.CombatLevel;
                        this.Helper.Data.WriteJsonFile($"data/{Constants.SaveFolderName}.json", data);
                        Game1.player.farmingLevel.Value = 10;
                        Game1.player.miningLevel.Value = 10;
                        Game1.player.foragingLevel.Value = 10;
                        Game1.player.fishingLevel.Value = 10;
                        Game1.player.combatLevel.Value = 10;
                        ///////////////////////////////////////////
                    }
                    else
                    {
                        IsAutomating = false;
                        this.Monitor.Log("Auto mode off!", LogLevel.Info);

                        Game1.chatBox.addInfoMessage("The host has returned!");

                        Game1.displayHUD = true;
                        Game1.addHUDMessage(new HUDMessage("Auto Mode Off!"));
                        //set player levels to stored levels
                        var data = this.Helper.Data.ReadJsonFile<ModData>($"data/{Constants.SaveFolderName}.json") ?? new ModData();
                        Game1.player.farmingLevel.Value = data.FarmingLevel;
                        Game1.player.miningLevel.Value = data.MiningLevel;
                        Game1.player.foragingLevel.Value = data.ForagingLevel;
                        Game1.player.fishingLevel.Value = data.FishingLevel;
                        Game1.player.combatLevel.Value = data.CombatLevel;
                        //////////////////////////////////////

                    }
                    //warp farmer on button press
                    if (Game1.player.currentLocation is FarmHouse)
                    {
                        Game1.warpFarmer("Farm", this.Config.warpCoordForFarm.X, this.Config.warpCoordForFarm.Y, false);
                    }
                    else
                    {
                        GetBedCoordinates();
                        Game1.warpFarmer("Farmhouse", bedX, bedY, false);
                    }
                }
            }
        }


        private void FestivalsToggle()
        {
            if (!this.Config.festivalsOn)
                return;
        }


        /// <summary>Raised once per second after the game state is updated.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnOneSecondUpdateTicked(object sender, OneSecondUpdateTickedEventArgs e)
        {
            // pause if no players present
            if (this.IsAutomating)
            {
                this.PauseIfNobodyPresent();
            }
            else
            {
                Game1.netWorldState.Value.IsPaused = false;
            }

            // toggle pause if requested
            if (this.IsAutomating && this.Config.clientsCanPause)
            {
                List<ChatMessage> messages = this.Helper.Reflection.GetField<List<ChatMessage>>(Game1.chatBox, "messages").GetValue();
                if (messages.Count > 0)
                {
                    var messagetoconvert = messages[messages.Count - 1].message;
                    string actualmessage = ChatMessage.makeMessagePlaintext(messagetoconvert, true);
                    string lastFragment = actualmessage.Split(' ')[1];

                    if (lastFragment != null && lastFragment == "!pause")
                    {
                        Game1.netWorldState.Value.IsPaused = true;
                        clientPaused = true;
                        this.SendChatMessage("Game Paused");
                    }
                    if (lastFragment != null && lastFragment == "!unpause")
                    {
                        Game1.netWorldState.Value.IsPaused = false;
                        clientPaused = false;
                        this.SendChatMessage("Game UnPaused");
                    }
                }
            }

            // update invite code
            if (Game1.options.enableServer && Game1.server != null)
            {
                string inviteCode = Game1.server.getInviteCode();
                if (inviteCode != this.lastInviteCode)
                {
                    // copy to clipboard
                    if (this.Config.copyInviteCodeToClipboard)
                        DesktopClipboard.SetText($"Invite Code: {Game1.server.getInviteCode()}");

                    // write to file
                    try
                    {
                        File.WriteAllText(Path.Combine(this.Helper.DirectoryPath, "InviteCode.txt"), inviteCode);
                    }
                    catch (Exception b)
                    {
                        Console.WriteLine("Exception: " + b.Message);
                    }

                    // remember code
                    this.lastInviteCode = inviteCode;
                }
            }

            // write number of players online to .txt
            if (Game1.options.enableServer && Game1.server != null)
            {
                if (connectionsCount != Game1.server.connectionsCount)
                {
                    connectionsCount = Game1.server.connectionsCount;

                    try
                    {
                        File.WriteAllText(Path.Combine(this.Helper.DirectoryPath, "ConnectionsCount.txt"), connectionsCount.ToString());
                    }
                    catch (Exception b)
                    {
                        Console.WriteLine("Exception: " + b.Message);
                    }
                }
            }

            //left click menu spammer and event skipper to get through random events happening
            //also moves player around, this seems to free host from random bugs sometimes
            if (IsAutomating)
            {
                if (Game1.activeClickableMenu != null && Game1.activeClickableMenu is DialogueBox)
                {
                    Game1.activeClickableMenu.receiveLeftClick(10, 10);
                    //Console.WriteLine("Clicked");
                }
                if (Game1.CurrentEvent != null && Game1.CurrentEvent.skippable)
                {
                    skipTicks += 1; // If we spam skipEvent() it gets stuck.

                    if (skipTicks >= 3)
                    {
                        Game1.CurrentEvent.skipEvent();
                        //Console.WriteLine("Skipped");
                        skipTicks = 0;
                    }
                }
            }

            // disable friendship decay
            if (IsAutomating)
            {
                if (this.PreviousFriendships.Any())
                {
                    foreach (string key in Game1.player.friendshipData.Keys)
                    {
                        Friendship friendship = Game1.player.friendshipData[key];
                        if (this.PreviousFriendships.TryGetValue(key, out int oldPoints) && oldPoints > friendship.Points)
                            friendship.Points = oldPoints;
                    }
                }

                this.PreviousFriendships.Clear();
                foreach (var pair in Game1.player.friendshipData.FieldDict)
                    this.PreviousFriendships[pair.Key] = pair.Value.Value.Points;
            }

            // automate events
            if (this.IsAutomating)
            {
                // egg-hunt event
                if (eggHuntAvailable && Game1.CurrentEvent != null && Game1.CurrentEvent.isFestival)
                {
                    if (eventCommandUsed)
                    {
                        eggHuntCountDown = this.Config.eggHuntCountDownConfig;
                        eventCommandUsed = false;
                    }
                    eggHuntCountDown += 1;

                    float chatEgg = this.Config.eggHuntCountDownConfig / 60f;
                    if (eggHuntCountDown == 1)
                    {
                        this.SendChatMessage($"The Egg Hunt will begin in {chatEgg:0.#} minutes.");
                    }

                    if (eggHuntCountDown == this.Config.eggHuntCountDownConfig + 1)
                    {
                        this.Helper.Reflection.GetMethod(Game1.CurrentEvent, "answerDialogueQuestion").Invoke(Game1.getCharacterFromName("Lewis"), "yes");
                    }
                    if (eggHuntCountDown >= this.Config.eggHuntCountDownConfig + 5)
                    {
                        if (Game1.activeClickableMenu != null)
                        {
                            //this.Helper.Reflection.GetMethod(Game1.activeClickableMenu, "receiveLeftClick").Invoke(10, 10, true);
                        }
                        //festival timeout
                        festivalTicksForReset += 1;
                        if (festivalTicksForReset >= this.Config.eggFestivalTimeOut + 180)
                        {
                            Game1.options.setServerMode("offline");
                        }
                        ///////////////////////////////////////////////
                    }
                }

                // flower dance event
                if (flowerDanceAvailable && Game1.CurrentEvent != null && Game1.CurrentEvent.isFestival)
                {
                    if (eventCommandUsed)
                    {
                        flowerDanceCountDown = this.Config.flowerDanceCountDownConfig;
                        eventCommandUsed = false;
                    }

                    flowerDanceCountDown += 1;

                    float chatFlower = this.Config.flowerDanceCountDownConfig / 60f;
                    if (flowerDanceCountDown == 1)
                    {
                        this.SendChatMessage($"The Flower Dance will begin in {chatFlower:0.#} minutes.");
                    }

                    if (flowerDanceCountDown == this.Config.flowerDanceCountDownConfig + 1)
                    {
                        this.Helper.Reflection.GetMethod(Game1.CurrentEvent, "answerDialogueQuestion").Invoke(Game1.getCharacterFromName("Lewis"), "yes");
                    }
                    if (flowerDanceCountDown >= this.Config.flowerDanceCountDownConfig + 5)
                    {
                        if (Game1.activeClickableMenu != null)
                        {
                            // this.Helper.Reflection.GetMethod(Game1.activeClickableMenu, "receiveLeftClick").Invoke(10, 10, true);
                        }

                        //festival timeout
                        festivalTicksForReset += 1;
                        if (festivalTicksForReset >= this.Config.flowerDanceTimeOut + 90)
                        {
                            Game1.options.setServerMode("offline");
                        }
                        ///////////////////////////////////////////////
                    }
                }

                //luauSoup event
                if (luauSoupAvailable && Game1.CurrentEvent != null && Game1.CurrentEvent.isFestival)
                {
                    if (eventCommandUsed)
                    {
                        luauSoupCountDown = this.Config.luauSoupCountDownConfig;
                        //add iridium starfruit to soup
                        var item = new SObject("268", 1, false, -1, 3);
                        this.Helper.Reflection.GetMethod(new Event(), "addItemToLuauSoup").Invoke(item, Game1.player);
                        eventCommandUsed = false;
                    }

                    luauSoupCountDown += 1;

                    float chatSoup = this.Config.luauSoupCountDownConfig / 60f;
                    if (luauSoupCountDown == 1)
                    {
                        this.SendChatMessage($"The Soup Tasting will begin in {chatSoup:0.#} minutes.");

                        //add iridium starfruit to soup
                        var item = new SObject("268", 1, false, -1, 3);
                        this.Helper.Reflection.GetMethod(new Event(), "addItemToLuauSoup").Invoke(item, Game1.player);
                    }

                    if (luauSoupCountDown == this.Config.luauSoupCountDownConfig + 1)
                    {
                        this.Helper.Reflection.GetMethod(Game1.CurrentEvent, "answerDialogueQuestion").Invoke(Game1.getCharacterFromName("Lewis"), "yes");
                    }
                    if (luauSoupCountDown >= this.Config.luauSoupCountDownConfig + 5)
                    {
                        if (Game1.activeClickableMenu != null)
                        {
                            //this.Helper.Reflection.GetMethod(Game1.activeClickableMenu, "receiveLeftClick").Invoke(10, 10, true);
                        }
                        //festival timeout
                        festivalTicksForReset += 1;
                        if (festivalTicksForReset >= this.Config.luauTimeOut + 80)
                        {
                            Game1.options.setServerMode("offline");
                        }
                        ///////////////////////////////////////////////
                    }
                }

                //Dance of the Moonlight Jellies event
                if (jellyDanceAvailable && Game1.CurrentEvent != null && Game1.CurrentEvent.isFestival)
                {
                    if (eventCommandUsed)
                    {
                        jellyDanceCountDown = this.Config.jellyDanceCountDownConfig;
                        eventCommandUsed = false;
                    }

                    jellyDanceCountDown += 1;

                    float chatJelly = this.Config.jellyDanceCountDownConfig / 60f;
                    if (jellyDanceCountDown == 1)
                    {
                        this.SendChatMessage($"The Dance of the Moonlight Jellies will begin in {chatJelly:0.#} minutes.");
                    }

                    if (jellyDanceCountDown == this.Config.jellyDanceCountDownConfig + 1)
                    {
                        this.Helper.Reflection.GetMethod(Game1.CurrentEvent, "answerDialogueQuestion").Invoke(Game1.getCharacterFromName("Lewis"), "yes");
                    }
                    if (jellyDanceCountDown >= this.Config.jellyDanceCountDownConfig + 5)
                    {
                        if (Game1.activeClickableMenu != null)
                        {
                            // this.Helper.Reflection.GetMethod(Game1.activeClickableMenu, "receiveLeftClick").Invoke(10, 10, true);
                        }
                        //festival timeout
                        festivalTicksForReset += 1;
                        if (festivalTicksForReset >= this.Config.danceOfJelliesTimeOut + 180)
                        {
                            Game1.options.setServerMode("offline");
                        }
                        ///////////////////////////////////////////////
                    }
                }

                //Grange Display event
                if (grangeDisplayAvailable && Game1.CurrentEvent != null && Game1.CurrentEvent.isFestival)
                {
                    if (eventCommandUsed)
                    {
                        grangeDisplayCountDown = this.Config.grangeDisplayCountDownConfig;
                        eventCommandUsed = false;
                    }

                    grangeDisplayCountDown += 1;
                    festivalTicksForReset += 1;
                    //festival timeout code
                    if (festivalTicksForReset == this.Config.fairTimeOut - 120)
                    {
                        this.SendChatMessage("2 minutes to the exit or");
                        this.SendChatMessage("everyone will be kicked.");
                    }
                    if (festivalTicksForReset >= this.Config.fairTimeOut)
                    {
                        Game1.options.setServerMode("offline");
                    }
                    ///////////////////////////////////////////////
                    float chatGrange = this.Config.grangeDisplayCountDownConfig / 60f;
                    if (grangeDisplayCountDown == 1)
                    {
                        this.SendChatMessage($"The Grange Judging will begin in {chatGrange:0.#} minutes.");
                    }

                    if (grangeDisplayCountDown == this.Config.grangeDisplayCountDownConfig + 1)
                    {
                        this.Helper.Reflection.GetMethod(Game1.CurrentEvent, "answerDialogueQuestion").Invoke(Game1.getCharacterFromName("Lewis"), "yes");
                    }
                    if (grangeDisplayCountDown == this.Config.grangeDisplayCountDownConfig + 5)
                        this.LeaveFestival();
                }

                //golden pumpkin maze event
                if (goldenPumpkinAvailable && Game1.CurrentEvent != null && Game1.CurrentEvent.isFestival)
                {
                    goldenPumpkinCountDown += 1;
                    festivalTicksForReset += 1;
                    //festival timeout code
                    if (festivalTicksForReset == this.Config.spiritsEveTimeOut - 120)
                    {
                        this.SendChatMessage("2 minutes to the exit or");
                        this.SendChatMessage("everyone will be kicked.");
                    }
                    if (festivalTicksForReset >= this.Config.spiritsEveTimeOut)
                    {
                        Game1.options.setServerMode("offline");
                    }
                    ///////////////////////////////////////////////
                    if (goldenPumpkinCountDown == 10)
                        this.LeaveFestival();
                }

                //ice fishing event
                if (iceFishingAvailable && Game1.CurrentEvent != null && Game1.CurrentEvent.isFestival)
                {
                    if (eventCommandUsed)
                    {
                        iceFishingCountDown = this.Config.iceFishingCountDownConfig;
                        eventCommandUsed = false;
                    }
                    iceFishingCountDown += 1;

                    float chatIceFish = this.Config.iceFishingCountDownConfig / 60f;
                    if (iceFishingCountDown == 1)
                    {
                        this.SendChatMessage($"The Ice Fishing Contest will begin in {chatIceFish:0.#} minutes.");
                    }

                    if (iceFishingCountDown == this.Config.iceFishingCountDownConfig + 1)
                    {
                        this.Helper.Reflection.GetMethod(Game1.CurrentEvent, "answerDialogueQuestion").Invoke(Game1.getCharacterFromName("Lewis"), "yes");
                    }
                    if (iceFishingCountDown >= this.Config.iceFishingCountDownConfig + 5)
                    {
                        if (Game1.activeClickableMenu != null)
                        {
                            //this.Helper.Reflection.GetMethod(Game1.activeClickableMenu, "receiveLeftClick").Invoke(10, 10, true);
                        }
                        //festival timeout
                        festivalTicksForReset += 1;
                        if (festivalTicksForReset >= this.Config.festivalOfIceTimeOut + 180)
                        {
                            Game1.options.setServerMode("offline");
                        }
                        ///////////////////////////////////////////////
                    }
                }

                //Feast of the Winter event
                if (winterFeastAvailable && Game1.CurrentEvent != null && Game1.CurrentEvent.isFestival)
                {
                    winterFeastCountDown += 1;
                    festivalTicksForReset += 1;
                    //festival timeout code
                    if (festivalTicksForReset == this.Config.winterStarTimeOut - 120)
                    {
                        this.SendChatMessage("2 minutes to the exit or");
                        this.SendChatMessage("everyone will be kicked.");
                    }
                    if (festivalTicksForReset >= this.Config.winterStarTimeOut)
                    {
                        Game1.options.setServerMode("offline");
                    }
                    ///////////////////////////////////////////////
                    if (winterFeastCountDown == 10)
                        this.LeaveFestival();
                }
            }

            // Skip level up menu
            if (IsAutomating && Game1.activeClickableMenu is LevelUpMenu)
            {
                // Taken from LevelUpMenu.cs:504
                this.Monitor.Log("Skipping level up menu");
                ((LevelUpMenu)Game1.activeClickableMenu).isActive = false;
                ((LevelUpMenu)Game1.activeClickableMenu).informationUp = false;
                ((LevelUpMenu)Game1.activeClickableMenu).isProfessionChooser = false;
                ((LevelUpMenu)Game1.activeClickableMenu).RemoveLevelFromLevelList();
            }
        }

        //Pause game if no clients Code
        private void PauseIfNobodyPresent()
        {
            gameTicks += 1;

            if (gameTicks >= 3)
            {
                this.numPlayers = Game1.otherFarmers.Count;

                if (numPlayers >= 1 || debug)
                {
                    if (clientPaused)
                    {
                        Game1.netWorldState.Value.IsPaused = true;
                    }
                    else
                    {
                        Game1.netWorldState.Value.IsPaused = false;
                    }

                }
                else if (numPlayers <= 0 && Game1.timeOfDay >= 610 && Game1.timeOfDay <= 2500 && currentDate != eggFestival && currentDate != flowerDance && currentDate != luau && currentDate != danceOfJellies && currentDate != stardewValleyFair && currentDate != spiritsEve && currentDate != festivalOfIce && currentDate != feastOfWinterStar)
                {
                    Game1.netWorldState.Value.IsPaused = true;
                }

                gameTicks = 0;
            }


            //handles client commands for sleep, go to festival, start festival event.
            if (Context.IsWorldReady && IsAutomating)
            {
                List<ChatMessage> messages = this.Helper.Reflection.GetField<List<ChatMessage>>(Game1.chatBox, "messages").GetValue();
                if (messages.Count > 0)
                {
                    var messagetoconvert = messages[messages.Count - 1].message;
                    string actualmessage = ChatMessage.makeMessagePlaintext(messagetoconvert, true);
                    string lastFragment = actualmessage.Split(' ')[1];

                    if (lastFragment != null)
                    {
                        if (lastFragment == "!sleep")
                        {
                            if (currentTime >= this.Config.timeOfDayToSleep || this.Config.allowSleepBeforeTimeOfDayToSleep)
                            {
                                GoToBed();
                                this.SendChatMessage("Trying to go to bed.");
                            }
                            else
                            {
                                this.SendChatMessage("It's too early.");
                                this.SendChatMessage($"Try after {this.Config.timeOfDayToSleep}.");
                            }
                        }
                        if (lastFragment == "!festival")
                        {
                            this.SendChatMessage("Trying to go to Festival.");

                            if (currentDate == eggFestival)
                            {
                                EggFestival();

                            }
                            else if (currentDate == flowerDance)
                            {
                                FlowerDance();
                            }
                            else if (currentDate == luau)
                            {
                                Luau();
                            }
                            else if (currentDate == danceOfJellies)
                            {
                                DanceOfTheMoonlightJellies();
                            }
                            else if (currentDate == stardewValleyFair)
                            {
                                StardewValleyFair();
                            }
                            else if (currentDate == spiritsEve)
                            {
                                SpiritsEve();
                            }
                            else if (currentDate == festivalOfIce)
                            {
                                FestivalOfIce();
                            }
                            else if (currentDate == feastOfWinterStar)
                            {
                                FeastOfWinterStar();
                            }
                            else
                            {
                                this.SendChatMessage("Festival Not Ready.");
                            }
                        }
                        if (lastFragment == "!event")
                        {
                            if (Game1.CurrentEvent != null && Game1.CurrentEvent.isFestival)
                            {
                                if (currentDate == eggFestival)
                                {
                                    eventCommandUsed = true;
                                    eggHuntAvailable = true;
                                }
                                else if (currentDate == flowerDance)
                                {
                                    eventCommandUsed = true;
                                    flowerDanceAvailable = true;
                                }
                                else if (currentDate == luau)
                                {
                                    eventCommandUsed = true;
                                    luauSoupAvailable = true;
                                }
                                else if (currentDate == danceOfJellies)
                                {
                                    eventCommandUsed = true;
                                    jellyDanceAvailable = true;
                                }
                                else if (currentDate == stardewValleyFair)
                                {
                                    eventCommandUsed = true;
                                    grangeDisplayAvailable = true;
                                }
                                else if (currentDate == spiritsEve)
                                {
                                    eventCommandUsed = true;
                                    goldenPumpkinAvailable = true;
                                }
                                else if (currentDate == festivalOfIce)
                                {
                                    eventCommandUsed = true;
                                    iceFishingAvailable = true;
                                }
                                else if (currentDate == feastOfWinterStar)
                                {
                                    eventCommandUsed = true;
                                    winterFeastAvailable = true;
                                }
                            }
                            else
                            {
                                this.SendChatMessage("I'm not at a Festival.");
                            }
                        }
                        if (lastFragment == "!leave")
                        {
                            if (Game1.CurrentEvent != null && Game1.CurrentEvent.isFestival)
                            {
                                this.SendChatMessage("Trying to leave Festival");
                                this.LeaveFestival();
                            }
                            else
                            {
                                this.SendChatMessage("I'm not at a Festival.");
                            }
                        }
                        if (lastFragment == "!unstick")
                        {
                            if (Game1.player.currentLocation is FarmHouse)
                            {
                                this.SendChatMessage("Warping to Farm.");
                                Game1.warpFarmer("Farm", this.Config.warpCoordForFarm.X, this.Config.warpCoordForFarm.Y, false);
                            }
                            else
                            {
                                this.SendChatMessage("Warping inside house.");
                                GetBedCoordinates();
                                Game1.warpFarmer("Farmhouse", bedX, bedY, false);
                            }
                        }

                    }
                }
            }
        }

        /// <summary>Raised after the game state is updated (≈60 times per second).</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            // lock player chests
            if (this.Config.lockPlayerChests)
            {
                foreach (Farmer farmer in Game1.getOnlineFarmers())
                {
                    if (farmer.IsMainPlayer)
                        continue;

                    if (farmer.currentLocation is FarmHouse house && farmer != house.owner)
                    {
                        // lock offline player inventory
                        if (house is Cabin cabin)
                        {
                            NetMutex inventoryMutex = this.Helper.Reflection.GetField<NetMutex>(cabin, "inventoryMutex").GetValue();
                            inventoryMutex.RequestLock();
                        }

                        // lock fridge & chests
                        house.fridge.Value.mutex.RequestLock();
                        foreach (Chest chest in house.objects.Values.OfType<Chest>())
                            chest.mutex.RequestLock();
                    }
                }
            }

            // automate choices
            if (this.IsAutomating)
            {
                //petchoice
                if (!Game1.player.hasPet())
                {
                    this.Helper.Reflection.GetMethod(new Event(), "namePet").Invoke(this.Config.petname.Substring(0));
                }
                if (Game1.player.hasPet() && Game1.getCharacterFromName(Game1.player.getPetName()) is Pet pet)
                {
                    pet.Name = this.Config.petname.Substring(0);
                    pet.displayName = this.Config.petname.Substring(0);
                }

                //cave choice unlock
                if (!Game1.player.eventsSeen.Contains("65"))
                {
                    Game1.player.eventsSeen.Add("65");


                    if (this.Config.farmcavechoicemushrooms)
                    {
                        Game1.MasterPlayer.caveChoice.Value = 2;
                        (Game1.getLocationFromName("FarmCave") as FarmCave).setUpMushroomHouse();
                    }
                    else
                    {
                        Game1.MasterPlayer.caveChoice.Value = 1;
                    }
                }

                // community center unlock
                if (!Game1.player.eventsSeen.Contains("611439"))
                {

                    Game1.player.eventsSeen.Add("611439");
                    Game1.MasterPlayer.mailReceived.Add("ccDoorUnlock");
                }

                if (this.Config.upgradeHouse != 0 && Game1.player.HouseUpgradeLevel != this.Config.upgradeHouse)
                {
                    Game1.player.HouseUpgradeLevel = this.Config.upgradeHouse;
                }

                // just turns off auto mod if the game gets exited back to title screen
                IsAutomating &= !(Game1.activeClickableMenu is TitleMenu);
            }
        }


        /// <summary>Raised after the in-game clock time changes.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        public void OnTimeChanged(object sender, TimeChangedEventArgs e)
        {
            // auto-sleep and Holiday code
            currentTime = Game1.timeOfDay;
            currentDate = SDate.Now();
            eggFestival = new SDate(13, "spring");
            dayAfterEggFestival = new SDate(14, "spring");
            flowerDance = new SDate(24, "spring");
            luau = new SDate(11, "summer");
            danceOfJellies = new SDate(28, "summer");
            stardewValleyFair = new SDate(16, "fall");
            spiritsEve = new SDate(27, "fall");
            festivalOfIce = new SDate(8, "winter");
            feastOfWinterStar = new SDate(25, "winter");
            grampasGhost = new SDate(1, "spring", 3);
            if (IsAutomating)
            {
                gameClockTicks += 1;

                if (gameClockTicks >= 3)
                {
                    if (currentDate == eggFestival && (numPlayers >= 1 || debug))
                    {
                        FestivalsToggle();

                        if (currentTime >= 600 && currentTime <= 630)
                        {
                            this.SendChatMessage("Egg Festival Today!");
                            this.SendChatMessage("I will not be in bed until after 2:00 P.M.");

                        }
                        EggFestival();
                    }


                    //flower dance message changed to disabled bc it causes crashes
                    else if (currentDate == flowerDance && (numPlayers >= 1 || debug))
                    {
                        FestivalsToggle();

                        if (currentTime >= 600 && currentTime <= 630)
                        {
                            this.SendChatMessage("Flower Dance Today.");
                            this.SendChatMessage("I will not be in bed until after 2:00 P.M.");

                        }
                        FlowerDance();
                    }

                    else if (currentDate == luau && (numPlayers >= 1 || debug))
                    {
                        FestivalsToggle();

                        if (currentTime >= 600 && currentTime <= 630)
                        {
                            this.SendChatMessage("Luau Today!");
                            this.SendChatMessage("I will not be in bed until after 2:00 P.M.");
                        }
                        Luau();
                    }

                    else if (currentDate == danceOfJellies && (numPlayers >= 1 || debug))
                    {
                        FestivalsToggle();

                        if (currentTime >= 600 && currentTime <= 630)
                        {
                            this.SendChatMessage("Dance of the Moonlight Jellies Tonight!");
                            this.SendChatMessage("I will not be in bed until after 12:00 A.M.");
                        }
                        DanceOfTheMoonlightJellies();
                    }

                    else if (currentDate == stardewValleyFair && (numPlayers >= 1 || debug))
                    {
                        FestivalsToggle();

                        if (currentTime >= 600 && currentTime <= 630)
                        {
                            this.SendChatMessage("Stardew Valley Fair Today!");
                            this.SendChatMessage("I will not be in bed until after 3:00 P.M.");
                        }
                        StardewValleyFair();
                    }

                    else if (currentDate == spiritsEve && (numPlayers >= 1 || debug))
                    {
                        FestivalsToggle();

                        if (currentTime >= 600 && currentTime <= 630)
                        {
                            this.SendChatMessage("Spirit's Eve Tonight!");
                            this.SendChatMessage("I will not be in bed until after 12:00 A.M.");
                        }
                        SpiritsEve();
                    }

                    else if (currentDate == festivalOfIce && (numPlayers >= 1 || debug))
                    {
                        FestivalsToggle();

                        if (currentTime >= 600 && currentTime <= 630)
                        {
                            this.SendChatMessage("Festival of Ice Today!");
                            this.SendChatMessage("I will not be in bed until after 2:00 P.M.");
                        }
                        FestivalOfIce();
                    }

                    else if (currentDate == feastOfWinterStar && (numPlayers >= 1 || debug))
                    {
                        FestivalsToggle();

                        if (currentTime >= 600 && currentTime <= 630)
                        {
                            this.SendChatMessage("Feast of the Winter Star Today!");
                            this.SendChatMessage("I will not be in bed until after 2:00 P.M.");
                        }
                        FeastOfWinterStar();
                    }

                    else if (currentTime >= this.Config.timeOfDayToSleep && (numPlayers >= 1 || debug))
                    {
                        GoToBed();
                    }

                    gameClockTicks = 0;
                }
            }

            //handles various events that the host normally has to click through
            if (IsAutomating)
            {
                if (currentDate != grampasGhost && currentDate != eggFestival && currentDate != flowerDance && currentDate != luau && currentDate != danceOfJellies && currentDate != stardewValleyFair && currentDate != spiritsEve && currentDate != festivalOfIce && currentDate != feastOfWinterStar)
                {
                    if (currentTime == 620)
                    {
                        //check mail 10 a day
                        for (int i = 0; i < 10; i++)
                        {
                            this.Helper.Reflection.GetMethod(Game1.currentLocation, "mailbox").Invoke();
                        }
                    }
                    if (currentTime == 630)
                    {
                        //rustkey-sewers unlock
                        if (!Game1.player.hasRustyKey)
                        {
                            int items = Game1.netWorldState.Value.MuseumPieces.Length;
                            //Console.WriteLine($"Pieces {items}");
                            if (items >= 60)
                            {
                                Game1.player.eventsSeen.Add("295672");
                                Game1.player.eventsSeen.Add("66");
                                Game1.player.hasRustyKey = true;
                            }
                        }

                        //community center complete
                        if (this.Config.communitycenterrun)
                        {
                            if (!Game1.player.eventsSeen.Contains("191393") && Game1.player.mailReceived.Contains("ccCraftsRoom") && Game1.player.mailReceived.Contains("ccVault") && Game1.player.mailReceived.Contains("ccFishTank") && Game1.player.mailReceived.Contains("ccBoilerRoom") && Game1.player.mailReceived.Contains("ccPantry") && Game1.player.mailReceived.Contains("ccBulletin"))
                            {
                                CommunityCenter locationFromName = Game1.getLocationFromName("CommunityCenter") as CommunityCenter;
                                for (int index = 0; index < locationFromName.areasComplete.Count; ++index)
                                    locationFromName.areasComplete[index] = true;
                                Game1.player.eventsSeen.Add("191393");

                            }
                        }

                        //Joja run
                        if (!this.Config.communitycenterrun)
                        {
                            if (Game1.player.Money >= 10000 && !Game1.player.mailReceived.Contains("JojaMember"))
                            {
                                Game1.player.Money -= 5000;
                                Game1.player.mailReceived.Add("JojaMember");
                                this.SendChatMessage("Buying Joja Membership");

                            }

                            if (Game1.player.Money >= 30000 && !Game1.player.mailReceived.Contains("jojaBoilerRoom"))
                            {
                                Game1.player.Money -= 15000;
                                Game1.player.mailReceived.Add("ccBoilerRoom");
                                Game1.player.mailReceived.Add("jojaBoilerRoom");
                                this.SendChatMessage("Buying Joja Minecarts");

                            }

                            if (Game1.player.Money >= 40000 && !Game1.player.mailReceived.Contains("jojaFishTank"))
                            {
                                Game1.player.Money -= 20000;
                                Game1.player.mailReceived.Add("ccFishTank");
                                Game1.player.mailReceived.Add("jojaFishTank");
                                this.SendChatMessage("Buying Joja Panning");

                            }

                            if (Game1.player.Money >= 50000 && !Game1.player.mailReceived.Contains("jojaCraftsRoom"))
                            {
                                Game1.player.Money -= 25000;
                                Game1.player.mailReceived.Add("ccCraftsRoom");
                                Game1.player.mailReceived.Add("jojaCraftsRoom");
                                this.SendChatMessage("Buying Joja Bridge");

                            }

                            if (Game1.player.Money >= 70000 && !Game1.player.mailReceived.Contains("jojaPantry"))
                            {
                                Game1.player.Money -= 35000;
                                Game1.player.mailReceived.Add("ccPantry");
                                Game1.player.mailReceived.Add("jojaPantry");
                                this.SendChatMessage("Buying Joja Greenhouse");

                            }

                            if (Game1.player.Money >= 80000 && !Game1.player.mailReceived.Contains("jojaVault"))
                            {
                                Game1.player.Money -= 40000;
                                Game1.player.mailReceived.Add("ccVault");
                                Game1.player.mailReceived.Add("jojaVault");
                                this.SendChatMessage("Buying Joja Bus");
                                Game1.player.eventsSeen.Add("502261");
                            }
                        }

                    }

                    //go outside
                    if (currentTime == 640)
                    {
                        Game1.warpFarmer("Farm", this.Config.warpCoordForFarm.X, this.Config.warpCoordForFarm.Y, false);
                    }

                    //get fishing rod (standard spam clicker will get through cutscene)
                    if (currentTime == 900 && !Game1.player.eventsSeen.Contains("739330"))
                    {
                        Game1.player.increaseBackpackSize(1);
                        Game1.warpFarmer("Beach", 1, 20, 1);
                    }
                }
            }
        }

        public void EggFestival()
        {
            if (currentTime >= 900 && currentTime <= 1400)
            {
                //teleports to egg festival
                Game1.netReady.SetLocalReady("festivalStart", true);
                Game1.activeClickableMenu = new ReadyCheckDialog("festivalStart", true, who =>
                {
                    Game1.exitActiveMenu();
                    Game1.warpFarmer("Town", 1, 20, 1);

                });

                eggHuntAvailable = true;

            }
            else if (currentTime >= 1410)
            {

                eggHuntAvailable = false;
                Game1.options.setServerMode("online");
                eggHuntCountDown = 0;
                festivalTicksForReset = 0;
                GoToBed();
            }
        }

        public void FlowerDance()
        {
            if (currentTime >= 900 && currentTime <= 1400)
            {

                Game1.netReady.SetLocalReady("festivalStart", true);
                Game1.activeClickableMenu = new ReadyCheckDialog("festivalStart", true, who =>
                {
                    Game1.exitActiveMenu();
                    Game1.warpFarmer("Forest", 1, 20, 1);
                });

                flowerDanceAvailable = true;

            }
            else if (currentTime >= 1410 && currentTime >= this.Config.timeOfDayToSleep)
            {

                flowerDanceAvailable = false;
                Game1.options.setServerMode("online");
                flowerDanceCountDown = 0;
                festivalTicksForReset = 0;
                GoToBed();
            }
        }

        public void Luau()
        {

            if (currentTime >= 900 && currentTime <= 1400)
            {

                Game1.netReady.SetLocalReady("festivalStart", true);
                Game1.activeClickableMenu = new ReadyCheckDialog("festivalStart", true, who =>
                {
                    Game1.exitActiveMenu();
                    Game1.warpFarmer("Beach", 1, 20, 1);

                });

                luauSoupAvailable = true;

            }
            else if (currentTime >= 1410)
            {

                luauSoupAvailable = false;
                Game1.options.setServerMode("online");
                luauSoupCountDown = 0;
                festivalTicksForReset = 0;
                GoToBed();
            }
        }

        public void DanceOfTheMoonlightJellies()
        {


            if (currentTime >= 2200 && currentTime <= 2400)
            {

                Game1.netReady.SetLocalReady("festivalStart", true);
                Game1.activeClickableMenu = new ReadyCheckDialog("festivalStart", true, who =>
                {
                    Game1.exitActiveMenu();
                    Game1.warpFarmer("Beach", 1, 20, 1);

                });

                jellyDanceAvailable = true;

            }
            else if (currentTime >= 2410)
            {

                jellyDanceAvailable = false;
                Game1.options.setServerMode("online");
                jellyDanceCountDown = 0;
                festivalTicksForReset = 0;
                GoToBed();
            }
        }

        public void StardewValleyFair()
        {
            if (currentTime >= 900 && currentTime <= 1500)
            {



                Game1.netReady.SetLocalReady("festivalStart", true);
                Game1.activeClickableMenu = new ReadyCheckDialog("festivalStart", true, who =>
                {
                    Game1.exitActiveMenu();
                    Game1.warpFarmer("Town", 1, 20, 1);

                });

                grangeDisplayAvailable = true;

            }
            else if (currentTime >= 1510)
            {

                Game1.displayHUD = true;
                grangeDisplayAvailable = false;
                Game1.options.setServerMode("online");
                grangeDisplayCountDown = 0;
                festivalTicksForReset = 0;

                GoToBed();
            }
        }

        public void SpiritsEve()
        {

            if (currentTime >= 2200 && currentTime <= 2350)
            {
                Game1.netReady.SetLocalReady("festivalStart", true);
                Game1.activeClickableMenu = new ReadyCheckDialog("festivalStart", true, who =>
                {
                    Game1.exitActiveMenu();
                    Game1.warpFarmer("Town", 1, 20, 1);

                });

                goldenPumpkinAvailable = true;

            }
            else if (currentTime >= 2400)
            {

                Game1.displayHUD = true;
                goldenPumpkinAvailable = false;
                Game1.options.setServerMode("online");
                goldenPumpkinCountDown = 0;
                festivalTicksForReset = 0;
                GoToBed();
            }
        }

        public void FestivalOfIce()
        {
            if (currentTime >= 900 && currentTime <= 1400)
            {

                Game1.netReady.SetLocalReady("festivalStart", true);
                Game1.activeClickableMenu = new ReadyCheckDialog("festivalStart", true, who =>
                {
                    Game1.exitActiveMenu();
                    Game1.warpFarmer("Forest", 1, 20, 1);

                });

                iceFishingAvailable = true;

            }
            else if (currentTime >= 1410)
            {

                iceFishingAvailable = false;
                Game1.options.setServerMode("online");
                iceFishingCountDown = 0;
                festivalTicksForReset = 0;
                GoToBed();
            }
        }

        public void FeastOfWinterStar()
        {
            if (currentTime >= 900 && currentTime <= 1400)
            {

                Game1.netReady.SetLocalReady("festivalStart", true);
                Game1.activeClickableMenu = new ReadyCheckDialog("festivalStart", true, who =>
                {
                    Game1.exitActiveMenu();
                    Game1.warpFarmer("Town", 1, 20, 1);

                });

                winterFeastAvailable = true;

            }
            else if (currentTime >= 1410)
            {

                winterFeastAvailable = false;
                Game1.options.setServerMode("online");
                winterFeastCountDown = 0;
                festivalTicksForReset = 0;
                GoToBed();
            }
        }

        private void GetBedCoordinates()
        {
            // Check if the user overrides the bed coordinates via the config
            if (this.Config.warpCoordForBed.X != 0 && this.Config.warpCoordForBed.Y != 0)
            {
                bedX = this.Config.warpCoordForBed.X;
                bedY = this.Config.warpCoordForBed.Y;
                return;
            }

            int houseUpgradeLevel = Game1.player.HouseUpgradeLevel;
            if (houseUpgradeLevel == 0)
            {
                bedX = 9;
                bedY = 9;
            }
            else if (houseUpgradeLevel == 1)
            {
                bedX = 21;
                bedY = 4;
            }
            else
            {
                bedX = 27;
                bedY = 13;
            }
        }

        private void GoToBed()
        {
            GetBedCoordinates();

            Game1.warpFarmer("Farmhouse", bedX, bedY, false);

            this.Helper.Reflection.GetMethod(Game1.currentLocation, "startSleep").Invoke();
            Game1.displayHUD = true;
        }

        /// <summary>Raised after the game state is updated (≈60 times per second), regardless of normal SMAPI validation. This event is not thread-safe and may be invoked while game logic is running asynchronously. Changes to game state in this method may crash the game or corrupt an in-progress save. Do not use this event unless you're fully aware of the context in which your code will be run. Mods using this event will trigger a stability warning in the SMAPI console.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnUnvalidatedUpdateTick(object sender, UnvalidatedUpdateTickedEventArgs e)
        {
            //resets server connection after certain amount of time end of day
            if (Game1.timeOfDay >= this.Config.timeOfDayToSleep || Game1.timeOfDay == 600 && currentDateForReset != danceOfJelliesForReset && currentDateForReset != spiritsEveForReset && this.Config.endofdayTimeOut != 0)
            {
                timeOutTicksForReset += 1;
                var countdowntoreset = (2600 - this.Config.timeOfDayToSleep) * .01 * 6 * 7 * 60;
                if (timeOutTicksForReset >= (countdowntoreset + (this.Config.endofdayTimeOut * 60)))
                {
                    Game1.options.setServerMode("offline");
                }
            }
            if (currentDateForReset == danceOfJelliesForReset || currentDateForReset == spiritsEveForReset && this.Config.endofdayTimeOut != 0)
            {
                if (Game1.timeOfDay >= 2400 || Game1.timeOfDay == 600)
                {
                    timeOutTicksForReset += 1;
                    if (timeOutTicksForReset >= (5040 + (this.Config.endofdayTimeOut * 60)))
                    {
                        Game1.options.setServerMode("offline");
                    }
                }
            }

            if (shippingMenuActive && this.Config.endofdayTimeOut != 0)
            {
                shippingMenuTimeoutTicks += 1;
                if (shippingMenuTimeoutTicks >= this.Config.endofdayTimeOut * 60)
                {
                    Game1.options.setServerMode("offline");
                }
            }

            if (Game1.timeOfDay == 610)
            {
                shippingMenuActive = false;
                Game1.player.difficultyModifier = this.Config.profitmargin * .01f;

                Game1.options.setServerMode("online");
                timeOutTicksForReset = 0;
                shippingMenuTimeoutTicks = 0;
            }

            if (Game1.timeOfDay == 2600)
            {
                Game1.paused = false;
            }
        }

        /// <summary>Send a chat message.</summary>
        /// <param name="message">The message text.</param>
        private void SendChatMessage(string message)
        {
            Game1.chatBox.activate();
            Game1.chatBox.setText(message);
            Game1.chatBox.chatBox.RecieveCommandInput('\r');
        }

        /// <summary>Leave the current festival, if any.</summary>
        private void LeaveFestival()
        {
            Game1.netReady.SetLocalReady("festivalEnd", true);
            Game1.activeClickableMenu = new ReadyCheckDialog("festivalEnd", true, who =>
            {
                GetBedCoordinates();
                Game1.exitActiveMenu();
                Game1.warpFarmer("Farmhouse", bedX, bedY, false);
                Game1.timeOfDay = currentDate == spiritsEve ? 2400 : 2200;
                Game1.shouldTimePass();
            });
        }
    }
}
