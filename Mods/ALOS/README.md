## ALOS (Always On Server) 食用文档

### 😀支持与预支持的功能
| 功能描述                           | 支持情况 |
| :--------------------------------- | :------- |
| 自动正确处理并跳过大部分节日和事件 | ✅        |
| 无人的情况下自动暂停               | ✅        |
| 游戏原生暂停bug的补丁              | ✅        |
| 支持其他Mod的事件                  | ❌        |
| 有些必须要农场主人接取并完成的任务 | ❌        |


### 🥳游戏原生节日事件支持相关
在源码中，可以看到，目前支持的节日如下
```csharp
        SDate eggFestival = new SDate(13, "spring");
        SDate dayAfterEggFestival = new SDate(14, "spring");
        SDate flowerDance = new SDate(24, "spring");
        SDate luau = new SDate(11, "summer");
        SDate danceOfJellies = new SDate(28, "summer");
        SDate stardewValleyFair = new SDate(16, "fall");
        SDate spiritsEve = new SDate(27, "fall");
        SDate festivalOfIce = new SDate(8, "winter");
        SDate feastOfWinterStar = new SDate(25, "winter");
        SDate granpasGhost = new SDate(1, "spring", 3);
```
| 节日                               | 支持情况 |
| :--------------------------------- | :------- |
| `eggFestival(复活节)` | ✅        |
|`flowerDance(花舞节)`|✅|
|`luau(夏威夷宴会)`|✅|
|`stardewValleyFair(星露谷展览会)`|✅|
|`spiritsEve(万灵节)`|✅|
|`festivalOfIce(冰雪节)`|✅|
|`feastOfWinterStar(冬日星盛宴)`|✅|
|`grampasGhost(第三年爷爷幽灵回来)`|✅|
|`玩家结婚事件`|❌|
>请查看wiki获取这些节日相关信息，包括但是不限于开始事件结束时间，不要问出`为何bot不参与节日`这种囫囵的问题
### ⚙️配置文件参数说明
```json

{
  "serverHotKey": "F9",
  "profitmargin": 100,
  "upgradeHouse": 0,
  "petname": "QianWen",
  "farmcavechoicemushrooms": true,
  "communitycenterrun": true,
  "timeOfDayToSleep": 2200,
  "allowSleepBeforeTimeOfDayToSleep": false,
  "lockPlayerChests": true,
  "clientsCanPause": false,
  "copyInviteCodeToClipboard": true,
  "festivalsOn": true,
  "eggHuntCountDownConfig": 60,
  "flowerDanceCountDownConfig": 60,
  "luauSoupCountDownConfig": 60,
  "jellyDanceCountDownConfig": 60,
  "grangeDisplayCountDownConfig": 60,
  "iceFishingCountDownConfig": 60,
  "endofdayTimeOut": 300,
  "fairTimeOut": 1200,
  "spiritsEveTimeOut": 900,
  "winterStarTimeOut": 900,
  "eggFestivalTimeOut": 120,
  "flowerDanceTimeOut": 120,
  "luauTimeOut": 120,
  "danceOfJelliesTimeOut": 120,
  "festivalOfIceTimeOut": 120,
  "warpCoordForFarm": {
    "X": 64,
    "Y": 15
  },
  "warpCoordForBed": {
    "X": 0,
    "Y": 0
  }
}
```

| 配置项名称 | 配置项作用说明 | 默认值 |
|-----------|---------------|--------|
| serverHotKey | Mod启/停热键设置 | F9 |
| profitmargin | 利润率设置 | 100 |
| upgradeHouse | 房屋升级等级 | 0 |
| petname | 宠物名称 | QianWen |
| farmcavechoicemushrooms | 农场洞穴选择蘑菇（true为蘑菇，false为水果） | true |
| communitycenterrun | 是否启用社区中心运行模式 | true |
| timeOfDayToSleep | 每日睡觉时间（24小时制，`2200`表示`22:00`），如果希望早上九点睡觉请填写`0900`，或者使用`alos.go_to_sleep` | 2200 |
| allowSleepBeforeTimeOfDayToSleep | 是否允许在设定睡觉时间之前睡觉 | false |
| lockPlayerChests | 是否锁定玩家箱子 | true |
| clientsCanPause | 客户端是否可以暂停游戏 | false |
| copyInviteCodeToClipboard | 是否自动复制邀请码到剪贴板 | true |
| festivalsOn | 是否启用节日活动 | true |
| eggHuntCountDownConfig | 复活节寻蛋活动倒计时配置（秒） | 60 |
| flowerDanceCountDownConfig | 花舞节倒计时配置（秒） | 60 |
| luauSoupCountDownConfig | 夏威夷宴会汤品倒计时配置（秒） | 60 |
| jellyDanceCountDownConfig | 水母舞倒计时配置（秒） | 60 |
| grangeDisplayCountDownConfig | 农产品展示倒计时配置（秒） | 60 |
| iceFishingCountDownConfig | 冰钓倒计时配置（秒） | 60 |
| endofdayTimeOut | 每日结束超时时间（秒） | 300 |
| fairTimeOut | 集市超时时间（秒） | 1200 |
| spiritsEveTimeOut | 幽灵节超时时间（秒） | 900 |
| winterStarTimeOut | 冬星节超时时间（秒） | 900 |
| eggFestivalTimeOut | 复活节活动超时时间（秒） | 120 |
| flowerDanceTimeOut | 花舞节活动超时时间（秒） | 120 |
| luauTimeOut | 夏威夷宴会超时时间（秒） | 120 |
| danceOfJelliesTimeOut | 水母舞活动超时时间（秒） | 120 |
| festivalOfIceTimeOut | 冰雪节活动超时时间（秒） | 120 |
| warpCoordForFarm | 农场传送坐标（X, Y） | X: 64, Y: 15 |
| warpCoordForBed | 床铺传送坐标（X, Y） | X: 0, Y: 0 |


### ⚠️
- 在历史版本中,由于某些**使用姿势**、**偶然因素**、**API更新**~~(其实是代码写的不完整--)~~。出现了Mod无法使用，或者崩溃等等问题，请提交详细的issues帮助我们修复这些bug。
-  如果遇到不会使用，或者某些细节问题，请您耐心查看文档，亦可以再社群中向其他玩家和开发者寻求帮助，前提是给予他们足够的尊敬以及详尽的细节，他们是很乐意为您提供免费且力所能及的服务。不过在此之前，阅读[HTAQ](https://github.com/ryanhanwu/How-To-Ask-Questions-The-Smart-Way/blob/main/README-zh_CN.md)并不是什么坏处
- 本Mod基于[perkmi/Always-On-Server-for-Multiplayer](https://github.com/perkmi/Always-On-Server-for-Multiplayer/tree/master)进行二次开发，采用MIT+附加协议的方式进行分发，使用该mod进行商业用途时请署名原仓库地址