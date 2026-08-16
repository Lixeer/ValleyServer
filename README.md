![ValleyServer](https://socialify.git.ci/Lixeer/ValleyServer/image?description=1&font=KoHo&forks=1&issues=1&logo=https%3A%2F%2Fgithub.com%2FLixeer%2FValleyServer%2Fblob%2Fmain%2Ficon.jpg%3Fraw%3Dtrue&name=1&owner=1&pattern=Brick+Wall&pulls=1&stargazers=1&theme=Light)
<div align="center">


[简体中文](README.md) | [English](README_en.md)  
  

</div>



---

## 📋 项目概述
- **项目原理**：  
  通过自定义 `mod` 实现农场主人的自动运行，例如自动睡觉、自动跳过剧情、自动关闭弹窗等功能，使游戏能够在无人值守的情况下持续进行。并且提供稳定的容器化部署方案。



---

## ✨ 功能特性
- **支持 SMAPI**：  
  完全兼容 `SMAPI`，可自由添加 `MOD` 使用。部分 `MOD` 可能存在兼容性问题（例如无法正常跳过剧情等），请以实际测试为准。
  
- **活跃的社区开发**：  
  社区持续维护与更新，欢迎提交 `issue` 与 `PR`！  
  相较于现有的无人值守类 `MOD`，本项目支持范围更广、更新更及时。


- **集成控制面板**：  
  在最新版的docker部署方案中，我们提供了一个简单美观的WebUI和WebVNC，更好的控制和管理服务器

---

## 🌻 快速开始
  - [Docker for ValleyServer](oneclick-script/cookbook.md)
  
  

---
##  🧸 本项目维护中的MOD
| MOD 名称 | 功能描述 |文档链接|
|:-:|:-|:-|
| `ALOS (Always On Server)` | 无人值守运行游戏（自动睡觉、跳过剧情、自动操作） | [➡️](Mods/ALOS/README.md)
| `ServerCmd` | 在无头服务器环境下执行控制指令 | -
| `ChatCommand` | 允许在游戏聊天框中执行控制台指令 | [➡️](Mods/ChatCommand/README.md)
| `CommandWebUI` | 在web浏览器中使用smapi控制台 | [➡️](Mods/CommandWebUI/README.md)|

>在`realase`页中,会打包其他作者的Mod(与本项目搭配使用更佳的Mods)，可根据`manifest.json`中的信息找到对应的仓库/作者并且为他们提供支持


## 😘 社区支持
### 🐧 QQ交流群

| QQ 群组 | [![QQ Group#3](https://img.shields.io/badge/QQ群%234-加入-blue)](https://qm.qq.com/q/XUzyb67T6C)|[![QQ Group#3](https://img.shields.io/badge/QQ群(已满)%233-加入-blue)](https://qm.qq.com/q/vfn1YWMCRM) | [![QQ Group#2](https://img.shields.io/badge/QQ群(已满)%232-加入-blue)](https://qm.qq.com/q/KhXvEqsw8g) | [![QQ Group#1](https://img.shields.io/badge/QQ群(已满)%231-加入-blue)](https://qm.qq.com/q/Q8QaovnQWG) |
|:-:|:-:|:-:|:-:|-:|

| QQ 频道（版本发布） | [![QQ Channel](https://img.shields.io/badge/QQ频道-加入-blue)](https://pd.qq.com/s/7gut1do04?b=5) |
|:-:|:-:|
---


## 🧰 致谢
- [**SMAPI**](https://github.com/Pathoschild/StardewModdingAPI)：提供了游戏注入与扩展机制
   

## 🤝 友情链接  
- [**Stardew Valley**](https://www.stardewvalley.net)：星露谷物语游戏官网
- [**Stardew-Valley-Mutiplayer-docker**](https://github.com/printfuck/stardew-multiplayer-docker)：星露谷物语多人游戏服务器docker部署

## 🎯 TODO
- 编写真正的协议端而非依赖无头服务器 (**我们正在编写一个`agent`用来分析星露谷源码，总结协议文档，你可以在qq群或者`issue`中加入我们**)
- 暂时不再考虑扩展的已有mod，但是希望有其他的联机玩法，例如类似`HayDay`的联网但不联机功能


## 🧮 Star History

[![Star History Chart](https://star-history.dera.page/svg?repos=Lixeer/ValleyServer&type=Date)](https://star-history.dera.page/#Lixeer/ValleyServer&Date)

## 🥰贡献者们

<a href="https://github.com/Lixeer/ValleyServer/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=Lixeer/ValleyServer"> 
</a>

</div>

---

## 💰 捐助支持

如果你喜欢这个项目，欢迎通过以下方式支持我们的开发：

<img src="docs/img/vx_pay.jpg" width="25%" height="25%">
