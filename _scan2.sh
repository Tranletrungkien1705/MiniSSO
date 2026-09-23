#!/bin/bash
P="D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine"
for f in "$@"; do
  echo "=== $f ==="
  grep -nE "class |namespace " "$P/$f.svn-base" | head -8
  echo
done
