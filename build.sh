#!/bin/bash
dotnet tool restore
dotnet paket restore

dotnet paket install

#export NPM_FILE_PATH=$(which npm)
# build using FAKE with optional parameters in $@
dotnet run