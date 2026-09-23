#!/bin/bash
# Extract a source file from the SVN pristine store by relative path substring.
# Usage: bash _scan.sh <relpath-substring>
DB="D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/wc.db"
P="D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/pristine"
target="$1"
grep -aoE ".{80}${target}.{80}" "$DB" | head -3
