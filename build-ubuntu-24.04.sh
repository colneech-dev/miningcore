#!/bin/bash

# Ubuntu 24.04 ships the .NET 10 SDK in its own archive; no Microsoft feed needed.

# install dev-dependencies
sudo apt-get update; \
  sudo apt-get -y install dotnet-sdk-10.0 git cmake clang ninja-build build-essential libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq5 libgmp-dev libc++-dev zlib1g-dev 

(cd src/Miningcore && \
BUILDIR=${1:-../../build} && \
echo "Building into $BUILDIR" && \
dotnet publish -c Release --framework net10.0 -o $BUILDIR)
