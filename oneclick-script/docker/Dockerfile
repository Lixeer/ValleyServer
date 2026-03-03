# Pull the baseimage.
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive
ENV TZ=Asia/Shanghai

RUN apt update
RUN apt install -y tar unzip xvfb x11vnc openbox xterm novnc



COPY content-game /content
COPY startapp.sh /startapp.sh

#COPY ["Mods", "/content/Stardew Valley/Mods"]
#COPY ["Saves","root/.config/StardewValley/Saves"]

RUN chmod +x startapp.sh
RUN chmod +x /content/Stardew\ Valley/StardewModdingAPI



RUN chmod -R 777 /content/Stardew\ Valley 
RUN chmod -R 777 /root 
RUN chown -R 1000:1000 /content/Stardew\ Valley



ENTRYPOINT  ["./startapp.sh"]
