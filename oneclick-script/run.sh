#!/bin/sh

echo "===== Docker 状态检查 ====="

# 1️⃣ 检查 docker 命令是否存在
if ! command -v docker >/dev/null 2>&1; then
    echo "[ERROR] 未安装 Docker"
    exit 1
fi

echo "[OK] 已安装 Docker"

# 2️⃣ 检查 Docker 服务是否运行
if docker info >/dev/null 2>&1; then
    echo "[OK] Docker 服务正在运行"
else
    echo "[WARNING] Docker 已安装，但服务未运行或当前用户无权限"
    
    # 尝试使用 systemctl 检查（适用于大多数 Linux 发行版）
    if command -v systemctl >/dev/null 2>&1; then
        systemctl is-active docker >/dev/null 2>&1
        if [ $? -eq 0 ]; then
            echo "[INFO] Docker 服务运行中，但可能权限不足"
        else
            echo "[INFO] Docker 服务未启动"
        fi
    fi

    exit 2
fi

# 3️⃣ 显示版本信息
echo "Docker 版本信息："
docker --version

echo "===== 检查完成 ====="

#5800 webvnc
#5900 VNC
#29103 commandWebUI
#24642 GameServer 

docker build -t valley-server ./docker
docker run --rm -p 5800:5800 -p 5900:5900 -p 29103:29103/tcp -p 24642:24642/udp valley-server --name seeya-valley-server