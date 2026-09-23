#!/bin/bash
P="D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine"
for f in "$@"; do
  echo "=== $f ==="
  head -5 "$P/$f.svn-base"
  echo
done
