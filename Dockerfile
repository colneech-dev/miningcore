# -------------------------
# Build stage
# -------------------------
FROM ubuntu:20.04 AS build

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y wget apt-transport-https && \
    wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    rm packages-microsoft-prod.deb

RUN apt-get update && apt-get install -y \
    dotnet-sdk-6.0 \
    cmake clang build-essential ninja-build git \
    libssl-dev pkg-config autoconf automake libtool \
    nasm yasm llvm llvm-dev gcc-multilib g++-multilib \
    && apt-get clean

WORKDIR /app
COPY . .

WORKDIR /app/src/Miningcore
RUN dotnet publish -c Release --framework net6.0


# -------------------------
# Runtime stage
# -------------------------
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS runtime

WORKDIR /app
COPY --from=build /app/src/Miningcore/bin/Release/net6.0 .

ENTRYPOINT ["./Miningcore", "-c", "/config/config.json"]
