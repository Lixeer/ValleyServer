# ValleyServer 3.x —— 反编译 / Mock 协议服务器（实验性）

> ⚠️ **实验性项目**。目前仍在探索阶段，暂不建议生产使用。
> 本目录对应 ValleyServer 的 **3.x 主线**，与 2.x（`Mods/` + Docker 的 MOD 方案）原理完全不同。对比详见根目录 [版本指南](../../docs/version-guide.md)。

## 设计目标

提供一个**不依赖真实游戏客户端 / GUI** 的原生无头多人服务器。做法是加载反编译的 `Stardew Valley` 程序集，用反射与 mock 绕过图形上下文与构造器，直接基于 `Lidgren.Network` 实现星露谷的通信协议。

## 代码结构

| 文件 / 目录 | 说明 |
| :--- | :--- |
| `Program.cs` | 主程序：初始化 mock 的游戏状态、运行 `Lidgren` 服务器与消息循环 |
| `ValleyServer.csproj` | 项目配置（`net8.0`，引用 `deps/` 下的反编译程序集） |
| `deps/` | 引用的程序集：`Stardew Valley.dll`、`StardewValley.GameData.dll`、`MonoGame.Framework.dll`、`xTile.dll`、`Lidgren.Network.dll`、`liblwjgl_lz4.dll` |
| `Content/` | 运行所需的游戏资源，**不随仓库提交**，构建时从 `Lixeer/ValleyContent` 下载解压 |

## 实现要点

- **图形资源 mock**：`HeadlessContentManager` 对 `Texture2D` / `SpriteFont` 直接返回未初始化对象，避免加载纹理解析 XNB。
- **绕过构造器**：`FormatterServices.GetUninitializedObject(typeof(Game1))` 跳过 XNA / 图形上下文检查。
- **反射注入**：给 `Game1` 静态字段、`Game1.multiplayer`、`Program._sdk`（改为 `NullSDKHelper`）等赋值，并加载物品 / 数据字典。
- **协议**：用 `MockLidgrenMessageUtils` 反射调用 `LidgrenMessageUtils` 的非公开方法，实现客户端发现、握手、Farmhand 列表、玩家互相介绍、传送（warp）、消息广播等。
- **存档**：农夫的 `saved_farmhands/*.xml`（见根目录 `.gitignore`）。

## 构建

资源下载与发布流程见根目录工作流：

- `.github/workflows/build-server.yml` —— 下载 `Content` 并发布 Windows / Linux x64
- `.github/workflows/release.yml` —— 发版时重复下载 `Content` 并打包发布
  - 资源源：`https://github.com/Lixeer/ValleyContent/releases/download/G1.6.15/Content.zip`

## 已知局限

- 仍**依赖下载的 `Content` 资源**，并非完全独立的协议端。
- 缺少完整游戏逻辑（农业、季节推进、NPC、任务、节日等）。
- 反射 / mock 紧密耦合游戏内部实现，**游戏版本升级后极易失效**。
- 存在硬编码的本机路径（`Program.cs` 中 `E:\steam\...`）等。

## 长期目标

真正协议端：**编写一个 `agent` 分析星露谷源码并总结协议文档**，摆脱对反编译程序集的依赖。欢迎在 QQ 群或 `issue` 中加入我们。

## 许可

请参阅根目录 `LICENSE`。注意：`deps/` 下的程序集属于对应版权方，再分发前请确认授权。
