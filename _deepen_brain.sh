#!/bin/bash
for d in Dev Dev20 Docs; do
  DB="D:/idocNet/2017.A.iNOS.InBrand/$d/.svn/wc.db"
  echo "===== $d ====="
  grep -aoE '[A-Za-z0-9_./-]+\.(cs|cshtml|sql|aspx)' "$DB" 2>/dev/null | sort -u > /tmp/paths_$d.txt
  echo "total paths: $(wc -l < /tmp/paths_$d.txt)"
  echo "-- BRAIN --"
  grep -iE 'brain' /tmp/paths_$d.txt | head -20
done