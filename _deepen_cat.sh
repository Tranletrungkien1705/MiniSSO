#!/bin/bash
DLL="D:/idocNet/_labs/MiniSSO/_svndump/bin/Release/net8.0/_svndump.dll"
DB="D:/idocNet/2017.A.iNOS.InBrand/Dev/.svn/wc.db"
DOTNET="/c/Program Files/dotnet/dotnet.exe"
"$DOTNET" "$DLL" "$DB" cat "$1" 2>/dev/null
