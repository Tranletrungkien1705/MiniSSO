#!/bin/bash
SVN=D:/idocNet/_labs/MiniSSO/_svndump/bin/Release/net8.0/_svndump.exe
for d in Dev Dev20 Docs; do
  echo "== $d =="
  "$SVN" "D:/idocNet/2017.A.iNOS.InBrand/$d/.svn/wc.db" list 2>/dev/null | wc -l
done