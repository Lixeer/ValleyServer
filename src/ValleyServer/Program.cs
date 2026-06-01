using System;
using ValleyServer.Core;
using ValleyServer.Adapters;
using ValleyServer.Services;

namespace HeadlessServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Starting Headless Stardew Valley Server (New Farmhand Customization Stage)...");

            // Define steam directory and game assembly path
            string steamDirPath = @"E:\steam\steamapps\common\Stardew Valley";
            string gameDllPath = @"D:\agent-workspace\vibe_server\Stardew Valley.dll";

            // Wire up dependencies (Composition Root)
            ISessionManager sessionManager = new SessionManager();
            IGameEngineAdapter gameEngine = new GameEngineAdapter(sessionManager);
            PlayerManager playerManager = new PlayerManager(sessionManager, gameEngine);
            ServerController serverController = new ServerController(sessionManager, gameEngine, playerManager);

            // Initialize game environment & mocks via adapter
            gameEngine.Initialize(steamDirPath, gameDllPath);

            // Run the Lidgren server loop
            int port = 24642;
            serverController.Start(port);
        }
    }
}
