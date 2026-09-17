CONFIG ?= Debug
SLN := SteamDesktopAuthenticator.sln

.PHONY: all build release clean

all: build

build:
	dotnet build "$(SLN)" -c $(CONFIG)

release:
	dotnet build "$(SLN)" -c Release

clean:
	dotnet clean "$(SLN)"

publish:
	dotnet publish "Steam Desktop Authenticator/Steam Desktop Authenticator.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
