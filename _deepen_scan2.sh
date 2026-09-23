#!/bin/bash
bash D:/idocNet/_labs/MiniSSO/_deepen_run.sh list 2>/dev/null | sed 's/^[^\t]*\t//' > /tmp/ino_paths.txt
echo "total: $(wc -l < /tmp/ino_paths.txt)"
echo "=== managers/providers/services ==="
grep -iE '\.cs$' /tmp/ino_paths.txt | grep -iE 'manager|provider|service' | head -100
