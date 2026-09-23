#!/bin/bash
DB="D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/wc.db"
grep -aboE '(\$sha1\$[0-9a-f]{40}|[A-Za-z0-9_./-]+\.(cs|cshtml|sql|aspx|config))' "$DB" 2>/dev/null > /tmp/tokens.txt
echo "token count: $(wc -l < /tmp/tokens.txt)"
echo "=== .cs files sample ==="
grep -oE '[A-Za-z0-9_./-]+\.cs' /tmp/tokens.txt | sort -u | head -40
echo "=== BRAIN matches ==="
grep -iE 'brain' /tmp/tokens.txt | head -20