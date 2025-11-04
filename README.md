<p align="center">
  <img src="icon.jpg" width="200" style="border-radius: 50%;" alt="logo"/>
</p>

<div align="center">

# ValleyServer  
星露谷物语多人服务器解决方案  
[简体中文](README.md) | [English](README_en.md)  
  
[![visitors](https://visitor-badge.laobi.icu/badge?page_id=Lixeer.ValleyServer_sync)]() [![license](https://img.shields.io/github/license/Lixeer/ValleyServer)]() [![stars](https://img.shields.io/github/stars/Lixeer/ValleyServer)]()
</div>



---

## 📋 项目概述
- **项目原理**：  
  通过自定义 `MOD` 实现农场的自动运行，例如自动睡觉、自动跳过剧情、自动关闭弹窗等功能，使游戏能够在无人值守的情况下持续进行。

- **注意事项**：  
  本项目主要收录各类开服方案、服务器相关 `MOD`、以及用于维护无人值守服务器的 `MOD`。  
  请不要在 `issues` 中催促开发者适配各类管理面板或容器化方案，这些需求由社区自行扩展和维护。  
  若你拥有成熟的部署方案，欢迎通过 `PR` 提交以收录到本项目文档中。  
  本仓库的 `issue` 仅受理与 `MOD` 功能相关的 `feature` 请求。

---

## ✨ 功能特性
- **支持 SMAPI**：  
  完全兼容 `SMAPI`，可自由添加 `MOD` 使用。部分 `MOD` 可能存在兼容性问题（例如无法正常跳过剧情等），请以实际测试为准。
  
- **活跃的社区开发**：  
  社区持续维护与更新，欢迎提交 `issue` 与 `PR`！  
  相较于现有的无人值守类 `MOD`，本项目支持范围更广、更新更及时。

---

## 🌻 快速开始
1. 从 `Releases` 页面下载所需的 `MOD` 压缩包  
2. 安装 `Stardew Valley` 与 `SMAPI`  
3. 将下载的 `MOD` 放入游戏 `Mods` 文件夹  
4. 运行 `StardewModdingAPI` 启动游戏  
5. ~~根据需要配置网络（FRP / DDNS / 公网 IP 等），默认端口为 `24642`，协议为 `UDP`~~

> ⚙️ **特殊场景运行技巧**
- **无 GPU 环境运行**  
  - Windows 环境可使用 **Mesa3D**  
  - Linux 环境可使用 **Xvfb**  (极不稳定且)
  - 一键开服包可在 QQ 群内获取（见下方）

---

- 📚 其他教程
  - 🎥 [【星露谷物语开服 / 多人服务器 / 三端互通 / 远程联机教程】](https://www.bilibili.com/video/BV13VPJe6EM1/?share_source=copy_web&vd_source=dddc5d0c3c33183e95f30f7d1ccdb295)  
  - 🧠 [【Linux 无头模式运行教程】](https://blog.csdn.net/2401_87565228/article/details/148801625?spm=1001.2014.3001.5501)

---
##  🧸 本项目维护中的MOD
| MOD 名称 | 功能描述 |
|:-:|:-|
| [![ALOS](https://img.shields.io/badge/ALOS-Auto%20Life%20on%20Server-brightgreen?style=for-the-badge)](Mod/ALOS/README.md) | 无人值守运行游戏（自动睡觉、跳过剧情、自动操作） |
| [![ServerCMD](https://img.shields.io/badge/ServerCMD-Headless%20Server%20Control-brightgreen?style=for-the-badge)](Mod/ServerCMD/README.md) | 在无头服务器环境下执行控制指令 |
| [![ChatCommand](https://img.shields.io/badge/ChatCommand-In%20Game%20Console%20via%20Chat-brightgreen?style=for-the-badge)](Mod/ChatCommand/README.md) | 允许在游戏聊天框中执行控制台指令 |

## 😘 社区支持
### 🐧 QQ交流群

| QQ 群组 | [![QQ Group#3](https://img.shields.io/badge/QQ群%233-加入-blue)](https://qm.qq.com/q/vfn1YWMCRM) | [![QQ Group#2](https://img.shields.io/badge/QQ群%232-加入-blue)](https://qm.qq.com/q/KhXvEqsw8g) | [![QQ Group#1](https://img.shields.io/badge/QQ群%231-加入-blue)](https://qm.qq.com/q/Q8QaovnQWG) |
|:-:|:-:|:-:|:-:|

| QQ 频道（版本发布） | [![QQ Channel](https://img.shields.io/badge/QQ频道-加入-blue)](https://pd.qq.com/s/7gut1do04?b=5) |
|:-:|:-:|
---


## 🧰 致谢
- [**SMAPI**](https://github.com/Pathoschild/StardewModdingAPI)：提供了游戏注入与扩展机制
- [**圆心云计算**](https://tyteam.net)：为本项目提供了服务器托管服务   

## 🤝 友情链接  
- [**圆心云计算**](https://tyteam.net)：租赁服务器，成熟的，客制化运维方案，就选圆心云，一键开服
- [**Stardew Valley**](https://www.stardewvalley.net)：星露谷物语游戏官网
- [**Stardew-Valley-Mutiplayer-docker**](https://github.com/printfuck/stardew-multiplayer-docker)：星露谷物语多人游戏服务器docker部署
> 如果您有成熟的部署方案，可以通过`PR`提交到本栏目，感谢您对社区的支持


## 🧮 Star History

[![Star History Chart](https://api.star-history.com/svg?repos=Lixeer/ValleyServer&type=Date)](https://www.star-history.com/#Lixeer/ValleyServer&Date)

## 🥰贡献者们

<a href="https://github.com/Lixeer/ValleyServer/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=Lixeer/ValleyServer"> 
</a>

</div>

---

## 💰 捐助支持

如果你喜欢这个项目，欢迎通过以下方式支持我们的开发：

<img src="docs/img/vx_pay.jpg" width="25%" height="25%">
