#!/bin/bash
echo "=== Sys* files ==="
grep -iE 'Sys[A-Za-z]*\.cs$' /tmp/ino_paths.txt | grep -viE 'templates' | head -80
echo ""
echo "=== Org / Dealer / User / Group / Role / Permission ==="
grep -iE '\.cs$' /tmp/ino_paths.txt | grep -iE 'org|dealer|user|group|role|permission|access|login|password|session' | grep -viE 'templates|Inventory|Part|Inv[A-Z]' | head -100
