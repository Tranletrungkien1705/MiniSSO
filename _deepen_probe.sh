#!/bin/bash
DB="D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/wc.db"
echo "== file size =="
ls -la "$DB"
echo "== strings: NODES / tables =="
strings "$DB" 2>/dev/null | grep -iE "CREATE TABLE|NODES|local_relpath|checksum" | head -20
echo "== count sha1 tokens =="
grep -aoE '\$sha1\$[0-9a-f]{40}' "$DB" 2>/dev/null | wc -l
echo "== sample relpaths =="
strings "$DB" 2>/dev/null | grep -iE '\.(cs|cshtml|sql)$' | head -20