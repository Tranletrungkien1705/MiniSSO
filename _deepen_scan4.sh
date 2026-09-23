#!/bin/bash
grep -iE 'UserTeam' /tmp/ino_paths.txt
echo "=== controllers with Team ==="
grep -iE 'Team' /tmp/ino_paths.txt | grep -iE 'Controller'
