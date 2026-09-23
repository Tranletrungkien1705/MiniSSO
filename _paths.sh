#!/bin/bash
DB=D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/wc.db
grep -aboE '(\$sha1\$[0-9a-f]{40}|[A-Za-z0-9_./-]+\.(cs|cshtml|sql))' "$DB" 2>/dev/null > /tmp/tokens.txt
echo "token count: $(wc -l < /tmp/tokens.txt)"
echo "=== sample around SysUser.cs ==="
grep -n 'SysUser.cs' /tmp/tokens.txt | head
