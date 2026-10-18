# ─────────────────────────────────────────────────────────────────────────────
# Stage 1: Native library compilation
# Only copies src/Native + build script — Docker caches this layer unless
# native sources actually change (very rare), so most rebuilds skip it.
# ─────────────────────────────────────────────────────────────────────────────
FROM ubuntu:24.04 AS native-builder
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y \
    cmake clang build-essential ninja-build git \
    libssl-dev libsodium-dev pkg-config autoconf automake libtool \
    nasm yasm llvm llvm-dev gcc-multilib g++-multilib \
    libboost-all-dev libgmp-dev \
    && apt-get clean

WORKDIR /build
COPY src/Native ./src/Native
COPY src/Miningcore/build-libs-linux.sh ./src/Miningcore/

RUN mkdir -p /native-output
WORKDIR /build/src/Miningcore
RUN bash build-libs-linux.sh /native-output

# ─────────────────────────────────────────────────────────────────────────────
# Stage 2: .NET build
# ─────────────────────────────────────────────────────────────────────────────
FROM ubuntu:24.04 AS build
ENV DEBIAN_FRONTEND=noninteractive
# Ubuntu 24.04's own archive carries the .NET 10 SDK, so the Microsoft package feed
# (and the wget/dpkg dance to add it) is no longer needed.
RUN apt-get update && apt-get install -y dotnet-sdk-10.0 && apt-get clean

WORKDIR /app
COPY . .

# FIX: Publish explicitly to an isolated /out directory to clean up the pathing
RUN dotnet publish src/Miningcore/Miningcore.csproj -c Release --framework net10.0 -p:SkipNativeLibBuild=true -o /out

# FIX: Copy pre-built native .so files directly into that unified publication directory
COPY --from=native-builder /native-output/*.so /out/

# ─────────────────────────────────────────────────────────────────────────────
# Stage 3: Runtime
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app

# FIX: Copy everything cleanly out of the unified /out folder
COPY --from=build /out .

RUN apt-get update && apt-get install -y libsodium23 libzmq5 curl && \
    ln -s /usr/lib/x86_64-linux-gnu/libzmq.so.5 /usr/lib/x86_64-linux-gnu/libzmq.so && \
    rm -rf /var/lib/apt/lists/*
ENTRYPOINT ["./Miningcore", "-c", "/config/config.json"]
