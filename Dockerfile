# -------------------------
# Build stage
# -------------------------
FROM ubuntu:24.04 AS build
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y wget apt-transport-https && \
    wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    rm packages-microsoft-prod.deb
RUN apt-get update && apt-get install -y \
    dotnet-sdk-8.0 \
    cmake clang build-essential ninja-build git \
    libssl-dev libsodium-dev pkg-config autoconf automake libtool \
    nasm yasm llvm llvm-dev gcc-multilib g++-multilib \
    libboost-all-dev libgmp-dev \
    && apt-get clean
WORKDIR /app
COPY . .
# Build native libmultihash
RUN cd /app/src/Native/libmultihash && make -j$(nproc)
# Fix RandomARQ and Panthera to only build randomx target, skipping broken tests
RUN sed -i \
    's|cmake -DARCH=native -DCMAKE_C_FLAGS=-Wa,--noexecstack -DCMAKE_CXX_FLAGS=-Wa,--noexecstack .. && make) && (cd ../Native/librandomarq|cmake -DARCH=native -DCMAKE_C_FLAGS=-Wa,--noexecstack -DCMAKE_CXX_FLAGS=-Wa,--noexecstack .. \&\& make randomx) \&\& (cd ../Native/librandomarq|g' \
    /app/src/Miningcore/build-libs-linux.sh && \
    sed -i \
    's|cmake -DARCH=native .. && make) && (cd ../Native/libpanthera|cmake -DARCH=native .. \&\& make randomx) \&\& (cd ../Native/libpanthera|g' \
    /app/src/Miningcore/build-libs-linux.sh
WORKDIR /app/src/Miningcore
RUN dotnet publish -c Release --framework net8.0
# -------------------------
# Runtime stage
# -------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble AS runtime
WORKDIR /app
COPY --from=build /app/src/Miningcore/bin/Release/net8.0 .
# Install runtime dependencies
RUN apt-get update && apt-get install -y libsodium23 libzmq5 && \
    ln -s /usr/lib/x86_64-linux-gnu/libzmq.so.5 /usr/lib/x86_64-linux-gnu/libzmq.so && \
    rm -rf /var/lib/apt/lists/*
ENTRYPOINT ["./Miningcore", "-c", "/config/config.json"]
