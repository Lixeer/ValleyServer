## CommandWebUI

### 功能介绍
将控制台输出重定向至`websocketIO`，因此可以在浏览器中使用`SMAPI 控制台`,也可以编写`websocket`反向代理软件接收信息流达到接入其他应用的目的

### 配置文件相关
```cs
namespace CommandWebUI
{
    public class ModConfig
    {
        public int Port = 29103;
        public string AccessToken = "set_your_own_token_please";
        public string IndexPage = "index-sv2.html";
        public string LoginPage = "login.html";
    }
}
```

| 配置项名称 | 配置项作用说明 | 默认值 |
|-----------|---------------|--------|
| Port | Web UI 服务器监听端口号 | 29103 |
| AccessToken | Web UI 访问令牌（用于身份验证） | set_your_own_token_please |
| IndexPage | Web UI 主页文件名 | index-sv2.html |
| LoginPage | Web UI 登录页面文件名 | login.html |

>`IndexPage`和`LoginPage`都可以通过自己编写达到自定义风格的目的。

>出于安全考虑，我们设置一个简单的登录认证通过`AccessToken`设置登录的密钥，但是这种加密方式对`websocketIO`无效，单纯是在浏览器中的页面验证，如果`Token`泄露或者获取到`WebsokcetIO`终结点，将会获取到控制台权限，由于本Mods使用了`System.Net.HttpLister`,在`window`系统下该mod必须以管理员身份运行才能正常使用，以及`smapi`有运行`c#`代码的能力，故本mod存在渗透风险，请谨慎使用