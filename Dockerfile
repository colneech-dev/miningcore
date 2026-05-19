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
# Only needs the .NET SDK — no C/C++ toolchain required.
# ─────────────────────────────────────────────────────────────────────────────
FROM ubuntu:24.04 AS build
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y wget apt-transport-https && \
    wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    rm packages-microsoft-prod.deb
RUN apt-get update && apt-get install -y dotnet-sdk-8.0 && apt-get clean

WORKDIR /app
COPY . .
RUN dotnet publish src/Miningcore/Miningcore.csproj -c Release --framework net8.0 -p:SkipNativeLibBuild=true

# Inject pre-built native .so files alongside the binary in the build output.
COPY --from=native-builder /native-output/*.so ./src/Miningcore/bin/Release/net8.0/

# ─────────────────────────────────────────────────────────────────────────────
# Stage 3: Runtime
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble AS runtime
WORKDIR /app
COPY --from=build /app/src/Miningcore/bin/Release/net8.0 .
RUN apt-get update && apt-get install -y libsodium23 libzmq5 curl && \
    ln -s /usr/lib/x86_64-linux-gnu/libzmq.so.5 /usr/lib/x86_64-linux-gnu/libzmq.so && \
    rm -rf /var/lib/apt/lists/*
ENTRYPOINT ["./Miningcore", "-c", "/config/config.json"]
