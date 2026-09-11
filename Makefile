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
