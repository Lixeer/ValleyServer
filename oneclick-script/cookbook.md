# Cookbook for Docker
Docker for ValleyServer的使用手册

## 1.
安装`docker`和`docker compose`
>`docker` 是一种将应用程序及其运行所需依赖（包括系统库、配置文件、运行时等）封装为标准化单元（称为容器）的工具。这一过程可类比移动应用程序的打包机制：开发者在构建移动应用时，会将应用代码与所需的前置依赖一并纳入安装包中，最终用户无需自行安装依赖即可直接运行该应用。类似地，Docker 使得开发者能够将应用与其完整运行环境封装至一个可移植的容器镜像中，从而保证该容器在任意支持 Docker 的环境中运行时，其行为保持一致，消除因运行环境差异导致的应用行为不一致问题。而`docker compose` 是为了编排多个运行中的应用所使用的工具，在本项目中仅为了将某些配置标准化结构化

## 2.
下载本目录`oneclick-script`并放到你的服务器中

## 3.
使用命令一键拉取并启动服务器应用
```sh
cd oneclick-script
bash ./run.sh
```
> [!NOTE]  
> 需要注意的是，受网络影响首次拉取镜像需要较长的时间（与dockerhub网络连接质量相关，无需从steam apt等下载服务和依赖），可以自行找镜像站或者使用代理服务器加速下载，也可以联系我（qq群中），为了简便各位的操作，我统一购买了流量供给有需要的人使用，单日下载仅象征性收取一块钱（虽然我会赚点）

## 4.

测试是否启用成功
访问：`http://<ip>:5800`
如果成功启动就会显示一个web界面，你点击下面带有`vnc`字样的进去，然后输入连接密码（默认041041），可以看到游戏主界面，此时仅需主持游戏即可
> [!NOTE]  
> 此时mod目录是且应当是空的（排除mod的神秘小bug导致无法启动加载器），你后续需要将`ALOS`等放入


## 5.
编排文件解读  
[docker-compose](docker/docker-compose.yml)
```yml
version: "3.8"

services:
  stardew:
    image: lixeer/valley-server:v26.5
    container_name: stardew_server
    restart: unless-stopped

    volumes:
      - ./Mods:/content/Stardew Valley/Mods     #将容器的mod目录挂载到宿主机，容器崩溃，删除，这个，这个目录都不会受到影响，当然，你的mod也是放入这个目录使其生效
      - ./Saves:/root/.config/StardewValley/Saves #同上,此时是存档目录

    ports:
      - "24642:24642/udp"  #游戏端口
      - "5800:5800"        #webvnc端口
      - "5900:5900"        #vnc端口 可用tiggervnc等软件连接 比webvnc有更好的性能
      - "29103:29103"      # 预留端口
```


如遇其他问题可在qq群或者issues中提出