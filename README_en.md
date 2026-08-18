<p align="center">
  <img src="icon.jpg" width="200" style="border-radius: 50%;" alt="logo"/>
</p>

<div align="center">

# ValleyServer  
A Multiplayer Server Solution for Stardew Valley  
[简体中文](README.md) | [English](README_en.md)  

[![visitors](https://visitor-badge.laobi.icu/badge?page_id=Lixeer.ValleyServer_sync)](https://github.com/Lixeer/ValleyServer) [![license](https://img.shields.io/github/license/Lixeer/ValleyServer)](https://github.com/Lixeer/ValleyServer/blob/main/LICENSE) [![stars](https://img.shields.io/github/stars/Lixeer/ValleyServer)](https://github.com/Lixeer/ValleyServer/stargazers)
</div>

## 📋 Project Overview
ValleyServer keeps your `Stardew Valley` farm running unattended and provides a stable multiplayer server solution. It is split into **2.x** and **3.x** mainlines:

### 🧩 2.x — MOD-Based Unattended + Docker
- **Principle**: Custom `MODs` (e.g. `ALOS`) automate the farm owner — auto sleeping, auto skipping cutscenes, closing dialogs, etc., so the game keeps running without human interaction.
- **Form**: Runs MODs on top of a real `Stardew Valley` + `SMAPI` client, packaged with a stable Docker deployment for a ready-to-use server.
- **Recommended**: The most mature, out-of-the-box solution. Code lives under `Mods/`; deployment docs: [`oneclick-script`](oneclick-script/cookbook.md).
- **Versioning**: released as `v2.x`.

### 🧬 3.x — Reverse-Engineered / Mock Protocol Server (Experimental)
- **Principle**: A standalone headless server (code under [`src/ValleyServer`](src/ValleyServer) — [README](src/ValleyServer/README.md)) that loads the decompiled game assemblies and uses reflection/mocking to run without a full game client or GUI.
- **Status**: Experimental. It currently still depends on the downloaded `Content` assets and lacks complete game logic (farming, seasons, NPCs, etc.). See TODO below.
- **Versioning**: released as `v3.x`.

> 💡 For a full 2.x / 3.x comparison, see [`docs/version-guide.md`](docs/version-guide.md) (Chinese).

**Notes**:
  This repository collects various server hosting solutions, as well as related `MODs` for running and maintaining unattended servers.  
  Please **do not** open `issues` asking developers to adapt to specific management panels or containerization technologies — those needs are community-driven and out of scope for this repo.  
  We may support them in the future, but **not** in this repository.  
  If you already have a mature deployment solution, you can submit a `PR` to add your repo to this documentation.  
  The `issues` section here only accepts `MOD`-related `feature` requests.

---

## ✨ Features
- **SMAPI Supported**: Compatible with `SMAPI`, allowing you to add `MODs`. However, some `MODs` may have limited compatibility (e.g., may fail to skip cutscenes).
- **Active Development**: The community is constantly improving and developing new features.  
  Issues and PRs are welcome! Compared to other unattended `MOD` solutions, ours is **the most up-to-date and comprehensive**.
- **WebUI + WebVNC Control Panel**: The latest Docker deployment ships a simple, clean WebUI and WebVNC to control and manage the server.
- **3.x Protocol Server (Experimental)**: A standalone headless server in [`src/ValleyServer`](src/ValleyServer) that reverse-engineers and mocks the game protocol. See TODO below.

---

## 🌻 Quick Start
- **2.x (recommended)**: Use the Docker deployment — see [`oneclick-script` cookbook](oneclick-script/cookbook.md).
- **3.x**: The reverse-engineered/mock server is still experimental and not recommended for production.

### 📚 Community Tutorials
- [Hosting a Stardew Valley Multiplayer Server / Cross-Platform / Remote Play (Bilibili)](https://www.bilibili.com/video/BV13VPJe6EM1/?share_source=copy_web&vd_source=dddc5d0c3c33183e95f30f7d1ccdb295)
- [Running in Headless Mode on Linux (CSDN)](https://blog.csdn.net/2401_87565228/article/details/148801625?spm=1001.2014.3001.5501)

## 🧸 MOD in this repo
| MOD Name | Description |
|:-:|:-|
| [![ALOS](https://img.shields.io/badge/ALOS-Auto%20Life%20on%20Server-brightgreen?style=for-the-badge)](Mods/ALOS/README.md) | Run the game unattended — auto sleep, skip cutscenes, and perform automated actions. |
| [![ServerCMD](https://img.shields.io/badge/ServerCMD-Headless%20Server%20Control-brightgreen?style=for-the-badge)](Mods/ServerCMD/README.md) | Execute control commands in a headless (no-GUI) server environment. |
| [![ChatCommand](https://img.shields.io/badge/ChatCommand-In%20Game%20Console%20via%20Chat-brightgreen?style=for-the-badge)](Mods/ChatCommand/README.md) | Run console commands directly from the in-game chat box. |
| [![CommandWebUI](https://img.shields.io/badge/CommandWebUI-Web%20Console-brightgreen?style=for-the-badge)](Mods/CommandWebUI/README.md) | Use the SMAPI console from a web browser. |
| [![ChangeServerPort](https://img.shields.io/badge/ChangeServerPort-Change%20Port-brightgreen?style=for-the-badge)](Mods/ChangeServerPort/README.md) | Change the Stardew Valley server port. |

## 😘 Community
### 🐧 QQ Groups

| QQ Group | [![QQ Group#3](https://img.shields.io/badge/QQ%20Group%233-Join-blue)](https://qm.qq.com/q/vfn1YWMCRM) | [![QQ Group#2](https://img.shields.io/badge/QQ%20Group%232-Join-blue)](https://qm.qq.com/q/KhXvEqsw8g) | [![QQ Group#1](https://img.shields.io/badge/QQ%20Group%231-Join-blue)](https://qm.qq.com/q/Q8QaovnQWG) |
|:-:|:-:|:-:|:-:|

| QQ Channel (Version Release) | [![QQ Group#3](https://img.shields.io/badge/QQ%20Group-Join-blue)](https://pd.qq.com/s/7gut1do04?b=5) |
|:-:|:-:|

---

## 🧰 Acknowledgements
- [**SMAPI**](https://github.com/Pathoschild/StardewModdingAPI) — for providing modding and game injection capabilities  

## 🎯 TODO
- **3.x mainline**: Write a real protocol server instead of depending on a headless client (we are building an `agent` to analyze the Stardew Valley source and document the protocol — join us via QQ group or `issue`). The current [`src/ValleyServer`](src/ValleyServer) achieves a headless server via decompile + reflection/mocking, but still requires the downloaded `Content` assets and lacks full game logic (farming, seasons, NPCs, etc.).
- **2.x mainline**: No near-term expansion of existing MODs, but we hope to add other online "no-co-op" play styles (e.g. HayDay-like), while the MOD-based unattended solution stays as the long-term stable default.

 


## 🧮 Star History

[![Star History Chart](https://star-history.dera.page/svg?repos=Lixeer/ValleyServer&type=Date)](https://star-history.dera.page/#Lixeer/ValleyServer&Date)

## 🥰 Contributors

<a href="https://github.com/Lixeer/ValleyServer/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=Lixeer/ValleyServer"> 
</a>

</div>

---

## 💰 Donate

<img src="docs/img/vx_pay.jpg" width="25%" height="25%">
