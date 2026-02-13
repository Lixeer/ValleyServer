
﻿using Microsoft.Xna.Framework;
using StardewModdingAPI;

namespace Always_On_Server.Framework
{
    class ModConfig
    {
        public SButton serverHotKey { get; set; } = SButton.F9;

        public int profitmargin { get; set; } = 100;
        public int upgradeHouse { get; set; } = 0;
        public string petname { get; set; } = "QianWen";
        public bool farmcavechoicemushrooms { get; set; } = true;
        public bool communitycenterrun { get; set; } = true;
        public int timeOfDayToSleep { get; set; } = 2200;
        public bool allowSleepBeforeTimeOfDayToSleep { get; set; } = false;

        public bool lockPlayerChests { get; set; } = true;
        public bool clientsCanPause { get; set; } = false;
        public bool copyInviteCodeToClipboard { get; set; } = true;


        public bool festivalsOn { get; set; } = true;
        public int eggHuntCountDownConfig { get; set; } = 60;
        public int flowerDanceCountDownConfig { get; set; } = 60;
        public int luauSoupCountDownConfig { get; set; } = 60;
        public int jellyDanceCountDownConfig { get; set; } = 60;
        public int grangeDisplayCountDownConfig { get; set; } = 60;
        public int iceFishingCountDownConfig { get; set; } = 60;

        public int endofdayTimeOut { get; set; } = 300;
        public int fairTimeOut { get; set; } = 1200;
        public int spiritsEveTimeOut { get; set; } = 900;
        public int winterStarTimeOut { get; set; } = 900;

        public int eggFestivalTimeOut { get; set; } = 120;
        public int flowerDanceTimeOut { get; set; } = 120;
        public int luauTimeOut { get; set; } = 120;
        public int danceOfJelliesTimeOut { get; set; } = 120;
        public int festivalOfIceTimeOut { get; set; } = 120;

        /**
         * Warp coordinates for various events during the game.
         */
        public Point warpCoordForFarm { get; set; } = new Point(64, 15);
        /**
         * Normally coordinates for the bed are hardcoded based on Farmhouse type.
         * This allows you to override the coordinates for the bed.
         */
        public Point warpCoordForBed { get; set; } = new Point(0, 0);
    }
}
