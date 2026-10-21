#!/bin/bash
# Пересборка и запуск под Wine на дисплее :5
export PATH=$HOME/.dotnet:$PATH DISPLAY=:5 WINEDEBUG=-all WINEPREFIX=$HOME/.wine-sd
cd /workspace/ship-designer
pkill -f ShipDesigner.exe; sleep 2
dotnet publish src/ShipDesigner/ShipDesigner.csproj -c Release -o out/ShipDesigner -p:DebugType=none 2>&1 | grep -E " error" | head -3
cd out/ShipDesigner && (nohup wine ShipDesigner.exe > /tmp/app.log 2>&1 &)
for i in $(seq 60); do W=$(xdotool search --name "Судно" 2>/dev/null | head -1); [ -n "$W" ] && break; sleep 1; done
sleep 8; xdotool windowmove $W 0 0; sleep 1; echo $W
