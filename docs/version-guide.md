# ValleyServer 版本指南（2.x vs 3.x）

ValleyServer 目前存在 **2.x** 与 **3.x** 两条主线，二者在**实现原理**与**代码位置**上完全不同。本页帮助你理解区别，并决定该用哪个。

---

## 🧩 2.x —— 基于 MOD 的无人值守 + Docker（推荐 / 稳定）

### 原理
在**真实的 `Stardew Valley` + `SMAPI` 客户端**之上，通过自定义 `MOD` 让“农场主人”自动运行：

- 自动睡觉、自动跳过剧情、自动关闭弹窗
- 无人时自动暂停，游戏原生暂停 bug 补丁
- 通过 `ChatCommand` / `ServerCMD` / `CommandWebUI` 在无头环境下执行控制指令
- 搭配 Docker 提供开箱即用的容器化部署（含 WebUI / WebVNC）

### 代码位置
- MOD 源码：`Mods/`（`ALOS`、`ServerCMD`、`ChatCommand`、`CommandWebUI`、`ChangeServerPort`）
- 部署脚本：`oneclick-script/`
- 版本：以 `v2.x` 形式发布

### 适用场景
追求**开箱即用、稳定可靠**的无人值守服务器。这是当前**推荐**的默认路线。

---

## 🧬 3.x —— 反编译 / Mock 协议服务器（实验性）

### 原理
不依赖真实游戏客户端，直接 **加载游戏程序集（decompile）并使用反射 / mock**，构建一个**无 GUI 的原生无头服务器**：

- 通过 `HeadlessContentManager` mock 掉 `Texture2D` / `SpriteFont` 等图形资源
- 用 `FormatterServices.GetUninitializedObject` 绕过构造函数与 XNA 图形上下文检查
- 通过反射注入 `Game1` 静态字段、`Multiplayer`、`NullSDKHelper` 等
- 直接基于 `Lidgren.Network` 实现星露谷的信息层协议（握手、传送、广播等）

### 代码位置
- 服务器源码：[`src/ValleyServer`](../src/ValleyServer)
  - `Program.cs` —— 主程序与消息循环
  - `ValleyServer.csproj` —— 项目配置
  - `deps/` —— 引用的反编译程序集（`Stardew Valley.dll`、`StardewValley.GameData.dll`、`MonoGame.Framework.dll`、`xTile.dll`、`Lidgren.Network.dll`、`liblwjgl_lz4.dll`）
  - `Content/` —— 运行时下载的游戏资源（见下方构建）
- 版本：以 `v3.x` 形式发布

### 现状与局限（实验性）
- ✅ 已实现：客户端发现 / 连接 / 握手、可用的 Farmhand 列表、玩家互相介绍、传送（warp）、部分消息广播、存档（saved_farmhands）
- ❌ 尚未完成 / 有局限：
  - 仍**依赖下载的 `Content` 资源**（见 `.github/workflows/build-server.yml` 与 `release.yml` 中的 `Lixeer/ValleyContent`）
  - 缺少完整的游戏逻辑支撑（农业、季节推进、NPC、任务、节日等）
  - 反射 / mock 依赖游戏内部实现，**版本升级时极易失效**
  - 硬编码了 Windows 本机路径（`Program.cs` 中的 `E:\steam\...`）等内容

### 适用场景
探索“真正的协议端”、研究星露谷多人协议，或作为技术验证。**暂不建议生产使用**。

---

## 🆚 对比一览

| 维度 | 2.x（MOD + Docker） | 3.x（反编译 / mock 协议端） |
| :--- | :--- | :--- |
| 原理 | 真实客户端 + SMAPI + 自动化 MOD | 加载反编译程序集 + 反射 / mock |
| 代码位置 | `Mods/`、`oneclick-script/` | `src/ValleyServer/` |
| 是否依赖游戏客户端 | 是 | 否（仅依赖反编译 DLL 与 Content） |
| 是否有 GUI | 是（可后台 + VNC） | 无 GUI，纯无头 |
| 稳定性 | 高（推荐） | 低（实验性） |
| 版本号 | `v2.x` | `v3.x` |

---

## 🚀 我该用哪个？

- **只是想稳定地开一个无人值守服务器** → 用 **2.x**，直接参考 [`oneclick-script/cookbook.md`](../oneclick-script/cookbook.md) 的 Docker 部署。
- **对协议 / 反编译感兴趣，或想参与 3.x 开发** → 看 `src/ValleyServer`，并加入我们的 QQ 群 / `issue`。
