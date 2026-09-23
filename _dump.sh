#!/bin/bash
# dump pristine file content to stdout: arg1 = hash, optional arg2 = base dir
f="$1"
base="${2:-D:/idocNet/2017.A.iNOS.InBrand/Dev20/.svn/pristine}"
p="$base/${f:0:2}/$f.svn-base"
cat "$p"
