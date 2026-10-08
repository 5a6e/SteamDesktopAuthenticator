CONFIG ?= Debug
SLN := SteamDesktopAuthenticator.sln
7Z ?= "C:/Program Files/7-Zip/7z.exe"
PUBLISH_DIR := Steam Desktop Authenticator/bin/Release/net8.0-windows/win-x64/publish
ARCHIVE := Steam Desktop Authenticator/bin/Release/net8.0-windows/win-x64/SteamDesktopAuthenticator-win-x64.7z

.PHONY: all build release clean publish

all: build

build:
	dotnet build "$(SLN)" -c $(CONFIG)

release:
	dotnet build "$(SLN)" -c Release

clean:
	dotnet clean "$(SLN)"

publish:
	dotnet publish "Steam Desktop Authenticator/Steam Desktop Authenticator.csproj" -c Release -r win-x64 --self-contained true
	-rm -f "$(ARCHIVE)"
	cd "$(PUBLISH_DIR)" && $(7Z) a -t7z -mx=9 -r "$(CURDIR)/$(ARCHIVE)" *
