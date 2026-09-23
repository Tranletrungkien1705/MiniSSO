#!/bin/bash
cd 'D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine'
for c in SysUserInGroup SysModule SysAccess SysFunction SysFunctionInModule ViewGroupView ViewColumnInGroup; do
  echo "== $c =="
  grep -rl "class $c :" . 2>/dev/null | head -3
done
