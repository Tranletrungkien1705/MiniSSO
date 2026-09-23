#!/bin/bash
# Extract clean source paths (strip CR, take 2nd tab field) from iNOS.InBrand SVN wc
bash D:/idocNet/_labs/MiniSSO/_deepen_run.sh list 2>/dev/null | tr -d '\r' | cut -f2 > /tmp/ino_paths.txt
echo "total: $(wc -l < /tmp/ino_paths.txt)"
head -3 /tmp/ino_paths.txt
