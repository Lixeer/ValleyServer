namespace AutoPause
{
    public class ModConfig
    {
        // 默认配置，第一次运行后可在生成的 config.json 中修改
        public string ServerIP { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 29103; 
        // public string AccessToken { get; set; } = "在这里填入你的Token";
        public string Command { get; set; } = "alos.pause";
    }
}
