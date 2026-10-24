#!/bin/bash

# install install-dependencies
sudo apt-get update; \
  sudo apt-get -y install wget

# install dev-dependencies
sudo apt-get update; \
  sudo apt-get -y install git cmake clang ninja-build build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5 libgmp-dev libc++-dev zlib1g-dev

# The .NET 10 SDK: Ubuntu 22.04 ships .NET 8 and Microsoft's 22.04 package feed stops at .NET 9, so there is no
# dotnet-sdk-10.0 apt package here (Ubuntu 24.04 and Debian 12 have one: use build-ubuntu-24.04.sh or
# build-debian-12.sh there). Use Microsoft's installer script instead. NOTE: this path has not been run on a 22.04 machine.
if ! command -v dotnet >/dev/null || ! dotnet --list-sdks | grep -q '^10\.'; then
  wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
  bash dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
  rm dotnet-install.sh
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
fi

(cd src/Miningcore && \
BUILDIR=${1:-../../build} && \
echo "Building into $BUILDIR" && \
dotnet publish -c Release --framework net10.0 -o $BUILDIR)
