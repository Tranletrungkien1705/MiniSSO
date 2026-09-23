#!/bin/bash
# probe pristine files: print first lines of each given hash
for f in "$@"; do
  p="${SVN_BASE:-D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine}/${f:0:2}/$f.svn-base"
  echo "=== $f ==="
  head -8 "$p"
  echo
done
