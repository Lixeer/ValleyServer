#!/bin/bash
#Xvfb :99 -screen 0 1280x720x24 -ac +extension GLX +render -noreset &
Xvfb :99 -screen 0 1280x720x24 -ac +extension GLX +extension RANDR +render -noreset -core &
export DISPLAY=:99
sleep 3
openbox-session &
cd content/Stardew\ Valley


x11vnc -display :99 -forever -shared -passwd "041041" -rfbport 5900 -noxdamage -bg 2>&1 | grep -v "^$"
websockify --web=/usr/share/novnc/ 5800 localhost:5900 &
sleep 3
export DISPLAY=:99
./StardewModdingAPI 