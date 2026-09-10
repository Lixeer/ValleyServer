# ValleyServer 3.x —— 反编译 / Mock 协议服务器（实验性）

> ⚠️ **实验性项目**。目前仍在探索阶段，暂不建议生产使用。
> 本目录对应 ValleyServer 的 **3.x 主线**，与 2.x（`Mods/` + Docker 的 MOD 方案）原理完全不同。对比详见根目录 [版本指南](../../docs/version-guide.md)。

## 设计目标

提供一个**不依赖真实游戏客户端 / GUI** 的原生无头多人服务器。做法是加载反编译的 `Stardew Valley` 程序集，用反射与 mock 绕过图形上下文与构造器，直接基于 `Lidgren.Network` 实现星露谷的通信协议。

## 代码结构

| 文件 / 目录 | 说明 |
| :--- | :--- |
| `Program.cs` | 主程序：初始化 mock 的游戏状态、运行 `Lidgren` 服务器与消息循环 |
| `Program.Helpers.cs` | `Program` 的 partial：世界推进、过夜流程、存档读写等辅助逻辑 |
| `ServerConfig.cs` | 分层配置模型（`Network` / `World` / `Simulation` / `Paths`），每项默认值即历史硬编码值 |
| `ConfigLoader.cs` | 读取 / 生成 / 校验 `config.json`，并合并环境变量与命令行覆盖 |
| `Protocol.cs` | 协议消息 ID 与消息描述辅助 |
| `HeadlessGameServer.cs` | `IGameServer` 适配器，把主机出站消息映射到 Lidgren |
| `HeadlessContentManager.cs` | 无头资源管理器，mock 纹理 / 字体 |
| `HeadlessDisplayDevice.cs` | xTile 空实现后端 |
| `MockLidgrenMessageUtils.cs` | 反射调用 `LidgrenMessageUtils` 的非公开方法 |
| `ValleyServer.csproj` | 项目配置（`net8.0`，引用 `deps/` 下的反编译程序集） |
| `deps/` | 引用的程序集：`Stardew Valley.dll`、`StardewValley.GameData.dll`、`MonoGame.Framework.dll`、`xTile.dll`、`Lidgren.Network.dll`、`liblwjgl_lz4.dll` |
| `Content/` | 运行所需的游戏资源，**不随仓库提交**，构建时从 `Lixeer/ValleyContent` 下载解压 |

## 配置

配置文件为可执行文件同目录下的 `config.json`，**首次运行时自动生成**（内容即下表默认值）。修改后重启生效。

覆盖优先级（从高到低）：

1. 命令行参数（`--port`）
2. 环境变量（`VALLEY_CONTENT_PATH`）
3. `config.json`
4. 代码内置默认值

文件缺失、格式错误或只写了一部分都不会导致启动失败：出错会打印提示并回退到默认值。越界取值会被报告并替换为默认值。

```json
{
  "Network": {
    "Port": 24642,
    "MaxConnections": 16,
    "ConnectionTimeoutSeconds": 30,
    "PingIntervalSeconds": 5,
    "MaximumTransmissionUnit": 1200
  },
  "World": {
    "FarmType": 0,
    "FarmName": "HeadlessFarm",
    "HostName": "Host",
    "Seed": 0,
    "StartingCabins": 4,
    "CabinsSeparate": false,
    "MaxFarmhands": 4,
    "StarterParsnipSeeds": 15
  },
  "Simulation": {
    "MillisecondsPerTenMinutes": 1000
  },
  "Paths": {
    "ContentPath": "",
    "SaveDirectory": "saved_farmhands"
  }
}
```

| 配置键 | 类型 | 默认值 | 说明 |
| :--- | :--- | :--- | :--- |
| `Network.Port` | int | `24642` | 监听端口，范围 `1024`–`65535`；可被 `--port` 覆盖 |
| `Network.MaxConnections` | int | `16` | Lidgren 最大连接数，建议不小于 `World.MaxFarmhands` |
| `Network.ConnectionTimeoutSeconds` | float | `30` | 无流量多久后断开对端 |
| `Network.PingIntervalSeconds` | float | `5` | 保活 Ping 间隔 |
| `Network.MaximumTransmissionUnit` | int | `1200` | 最大传输单元（字节） |
| `World.FarmType` | int | `0` | 农场类型 `0`–`6`（标准 / 河流 / 森林 / 山顶 / 荒野 / 四角 / 海滩） |
| `World.FarmName` | string | `"HeadlessFarm"` | 农场名 |
| `World.HostName` | string | `"Host"` | 内置主机农夫名 |
| `World.Seed` | ulong | `0` | 世界唯一 ID / 种子；`0` 表示按当前时间生成，其他值使世界可复现 |
| `World.StartingCabins` | int | `4` | 初始木屋数量 |
| `World.CabinsSeparate` | bool | `false` | 木屋是否分离布局 |
| `World.MaxFarmhands` | int | `4` | 可选农夫数量上限 |
| `World.StarterParsnipSeeds` | int | `15` | 新农夫初始芜菁种子数；`0` 表示不发放 |
| `Simulation.MillisecondsPerTenMinutes` | int | `1000` | 真实毫秒数推进游戏 10 分钟；越小游戏内一天越快 |
| `Paths.ContentPath` | string | `""` | 游戏资源目录；留空自动探测，可被 `VALLEY_CONTENT_PATH` 覆盖 |
| `Paths.SaveDirectory` | string | `"saved_farmhands"` | 农夫存档目录；相对路径相对于可执行文件目录 |

启动时会打印生效配置与解析出的世界身份（`[Config]`、`[World]` 前缀），便于确认配置确实被应用。

**说明**：游戏初始季节 / 日期 / 时刻、新农夫起始 ID、主循环 tick 间隔、游戏安装路径（`VALLEY_GAME_PATH`）、诊断自检开关目前**仍为硬编码**，尚未纳入配置。

## 实现要点

- **图形资源 mock**：`HeadlessContentManager` 对 `Texture2D` / `SpriteFont` 直接返回未初始化对象，避免加载纹理解析 XNB。
- **绕过构造器**：`FormatterServices.GetUninitializedObject(typeof(Game1))` 跳过 XNA / 图形上下文检查。
- **反射注入**：给 `Game1` 静态字段、`Game1.multiplayer`、`Program._sdk`（改为 `NullSDKHelper`）等赋值，并加载物品 / 数据字典。
- **协议**：用 `MockLidgrenMessageUtils` 反射调用 `LidgrenMessageUtils` 的非公开方法，实现客户端发现、握手、Farmhand 列表、玩家互相介绍、传送（warp）、消息广播等。
- **配置**：`config.json` 分层配置（见上方「配置」章节），首次运行自动生成。
- **存档**：农夫的 `<Paths.SaveDirectory>/*.xml`（默认 `saved_farmhands/`，见根目录 `.gitignore`）。

## 构建

资源下载与发布流程见根目录工作流：

- `.github/workflows/build-server.yml` —— 下载 `Content` 并发布 Windows / Linux x64
- `.github/workflows/release.yml` —— 发版时重复下载 `Content` 并打包发布
  - 资源源：`https://github.com/Lixeer/ValleyContent/releases/download/G1.6.15/Content.zip`

## 已知局限

- 仍**依赖下载的 `Content` 资源**，并非完全独立的协议端。
- 缺少完整游戏逻辑（农业、季节推进、NPC、任务、节日等）。
- 反射 / mock 紧密耦合游戏内部实现，**游戏版本升级后极易失效**。
- `Program.cs` 中仍有一处硬编码的本机路径（`D:\app\steam\...`，作为 `VALLEY_GAME_PATH` 未设置时的回退），尚未纳入配置。

## 长期目标

真正协议端：**编写一个 `agent` 分析星露谷源码并总结协议文档**，摆脱对反编译程序集的依赖。欢迎在 QQ 群或 `issue` 中加入我们。

## 许可

请参阅根目录 `LICENSE`。注意：`deps/` 下的程序集属于对应版权方，再分发前请确认授权。
