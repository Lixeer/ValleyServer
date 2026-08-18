## ServerCMD

在无头（无图形界面）服务器环境下执行控制指令的模组。

### 指令列表

| 指令 | 说明 |
| :--- | :--- |
| `ServerCMD.load_save <save_name>` | 加载存档（参数为存档名称） |
| `ServerCMD.set_multiplayermode <mode_id>` | 设置联机模式（0: 关闭，1: 本地联机，2: 网络联机） |
| `ServerCMD.get_save_list` | 获取存档列表 |
| `ServerCMD.get_save_path` | 获取存档路径 |
| `ServerCMD.new_save` | 创建新存档 |
| `ServerCMD.tp` | 传送主机（未完成） |

### 使用方式

在 SMAPI 控制台中输入上述指令即可；也可以配合 [ChatCommand](../ChatCommand/README.md) 在游戏内聊天框中以 `!cmd>` 前缀执行。
