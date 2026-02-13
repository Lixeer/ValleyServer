using System;
using System.Text;
using System.Threading;
using StardewModdingAPI;




namespace CommandWebUI
{





    public class ModEntry : Mod
    {   

        private ModConfig Config;
        private Server? server;

        public override void Entry(IModHelper helper)
        
        {
            this.Config = base.Helper.ReadConfig<ModConfig>();

            this.Monitor.Log("Starting embedded HTTP+WS server...", LogLevel.Info);


            var reader = new WebSocketReader();
            Console.SetIn(reader);
            server = new Server(this.Monitor, port: Config.Port ,reader: reader, acesstoken: Config.AccessToken, config: Config);
            
            var thread = new Thread(server.Start);
            thread.IsBackground = true;
            thread.Start();
            
            Console.SetOut(new WebSocketWriter(server));
            

            
        }


    }
}
