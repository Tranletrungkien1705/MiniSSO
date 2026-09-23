#!/bin/bash
# helper: run _svndump against the iNOS.InBrand SVN working copy
DOTNET="/c/Program Files/dotnet/dotnet.exe"
DLL="D:/idocNet/_labs/MiniSSO/_svndump/bin/Release/net8.0/_svndump.dll"
DB="D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/wc.db"
"$DOTNET" "$DLL" "$DB" "$@"
