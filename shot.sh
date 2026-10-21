#!/bin/bash
# использование: shot.sh "ix iy wait" ... name   (координаты по уменьшенному до 1024 снимку)
export DISPLAY=:5; W=$(xdotool search --name "Судно" | head -1)
name=${@: -1}
for a in "${@:1:$#-1}"; do set -- $a; x=$(python3 -c "print(int(-1+$1*1352/1024))"); y=$(python3 -c "print(int(31+$2*1352/1024))"); xdotool mousemove $x $y click 1; sleep ${3:-3}; done
import -window $W /workspace/ship-designer/screens/$name.png
