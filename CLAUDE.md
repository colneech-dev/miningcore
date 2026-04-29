# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

**Build (Windows):**
```bash
build-windows.bat
# Runs: dotnet publish -c Release --framework net8.0 -o ../../build
```

**Build (Linux):**
```bash
./build-debian.sh   # or build-ubuntu.sh / build-ubuntu2204.sh
```

**Docker:**
```bash
docker build -t miningcore .
docker-compose up
```

**Run tests:**
```bash
dotnet test src/Miningcore.Tests/Miningcore.Tests.csproj --logger:"console;verbosity=detailed"
```

**Run a single test:**
```bash
dotnet test src/Miningcore.Tests/Miningcore.Tests.csproj --filter "FullyQualifiedName~ClassName.MethodName"
```

**Restore dependencies:**
```bash
dotnet restore src/Miningcore.sln
```

## Linux Server Connection

```bash
ssh colin@192.168.1.100
```

## Running

```bash
cd build
Miningcore -c config.json
```

Production runs on **Linux only**. Windows is supported for development only.

**Docker run:**
```bash
docker run -d \
    -p 4000:4000 \
    -p 4066:4066 \
    -p 4067:4067 \
    --name mc \
    -v $(pwd)/config_prod.json:/app/config.json \
    --restart=unless-stopped \
    <your_dockerhubid>/miningcore:latest
```

## Architecture Overview

Miningcore is a multi-coin mining pool server targeting .NET 8. The solution lives under `src/` with two projects: `Miningcore` (main) and `Miningcore.Tests`.

### Share Processing Pipeline

1. **Stratum layer** — `Stratum/StratumServer.cs` accepts miner TCP connections. Each connection is an async pipeline using `System.IO.Pipelines`.
2. **Pool layer** — `Mining/PoolBase.cs` is the abstract base for every coin pool. Concrete implementations live in `Blockchain/<Family>/` (e.g. `Blockchain/Bitcoin/BitcoinPool.cs`). The pool validates submitted shares against the current job.
3. **Share relay** — `Mining/ShareReceiver.cs` ingests shares from remote relay nodes via ZeroMQ pub/sub.
4. **Share persistence** — `Mining/ShareRecorder.cs` writes shares to PostgreSQL asynchronously with a Polly circuit breaker and in-memory queue to absorb spikes.
5. **Stats** — `Mining/StatsRecorder.cs` aggregates per-pool and per-worker statistics on a background timer.
6. **Payments** — `Payments/PayoutManager.cs` drives coin-agnostic payout logic. Coin-specific handlers implement `IPayoutHandler` and `IPayoutScheme`.

### Coin Family Structure

Each supported coin family lives under `Blockchain/<Family>/` and provides:
- `<Family>Pool.cs` — pool lifecycle and share validation
- `<Family>Job.cs` / `<Family>JobManager.cs` — block template management and job creation
- `<Family>PayoutHandler.cs` — RPC-based payout execution
- `<Family>Constants.cs` / `<Family>CoinTemplate.cs` — coin metadata

Supported families: Alephium, Beam, Bitcoin, Conceal, Cryptonote, Equihash, Ergo, Ethereum, Handshake, Kaspa, Nexa, Progpow, Warthog, Xelis, Zano.

### Key Cross-Cutting Concerns

| Concern | Implementation |
|---|---|
| Dependency Injection | Autofac (`Autofac.Module` subclasses per area) |
| Async event bus | `System.Reactive` (Rx) — `MessageBus` for pool status, block found, share events |
| Config validation | `FluentValidation` — validators in each `Blockchain/<Family>/` folder |
| ORM / DB | Dapper with repository pattern under `Persistence/Postgres/Repositories/` |
| Logging | NLog — per-pool scoped loggers, configurable to per-pool log files |
| Metrics | Prometheus on `/metrics` endpoint |
| Native hashing | `libmultihash` and `libcryptonight` compiled as native libraries, P/Invoked from `Crypto/` |

### API

REST API served by ASP.NET Core on port 4000 (default):
- `GET /api/pools` — all pool stats (includes custom `connectedWorkers` field)
- `GET /api/pools/{id}` — single pool
- `GET /api/pools/{id}/blocks` — blocks (includes custom `worker` field)
- `GET /metrics` — Prometheus metrics
- `ws://{host}:{port}/notifications` — WebSocket event stream

### Database

PostgreSQL 10+ required. Schema: `src/Miningcore/Persistence/Postgres/Scripts/createdb.sql`.

This fork adds two columns requiring migrations:
- `connectedWorkers` on the pool stats table
- `worker` on the blocks table

### VarDiff

`VarDiff/VarDiffManager.cs` implements adaptive difficulty using a circular buffer of the last 10 share timestamps per connection.

### Configuration

JSON config file passed at startup (`-c config.json`). Example configs in `examples/`. Key top-level sections: `logging`, `banning`, `notifications`, `persistence`, `paymentProcessing`, `api`, `pools[]`.

Per-pool Bitcoin-family options of note: `enableVersionRolling`, `versionRollingMask`, `versionBlockedBits`, `enableAsicBoost`, `extraPoolConfig`.
