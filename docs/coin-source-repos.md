# Coin source repositories

Reference list of the upstream git repository, deployed version and build method for every coin this pool runs or has
prepared, both primary pool coins and merge-mined (AuxPoW) chains. Compiled 2026-10-23 from the deployment recipes on the
servers and the project notes.

**How to read it.** "Release" means the project's own prebuilt Linux binary, pinned by checksum in the Docker build.
"Source" means we compile it ourselves from the repo and ref shown. Versions are what is deployed today, not necessarily the
latest upstream. This file deliberately holds no addresses, hostnames, ports or credentials, because the repository is public.

Contents: [Our own repositories](#our-own-repositories) · [Primary pool coins](#primary-pool-coins) ·
[Merge-mined chains in the pools](#merge-mined-chains-in-the-pools) · [Installed, syncing, not yet wired](#installed-syncing-not-yet-wired) ·
[Prepared but parked or rejected](#prepared-but-parked-or-rejected) · [Decommissioned](#decommissioned) · [Notes](#notes)

## Our own repositories

| Purpose | Repository | Notes |
|---|---|---|
| Pool software (this repo) | `github.com/colneech-dev/miningcore` | Fork of Miningcore. Deployed from the `fix/aux-getauxblock-address` branch at the time of writing. |
| Web UI | `github.com/colneech-dev/Miningcore-Web-UI` | Node/Express + React, built with esbuild. |
| Infrastructure and wallet backups | `github.com/colneech-dev/linux3040-infrastructure` | Private. Daily config and wallet backups. |
| NerdAxe monitoring | `github.com/colneech-dev/Influx-Grafana-Nerd-Axe` | Separate project (InfluxDB + Grafana). |
| Bitcoin Silver (our fork) | `github.com/colneech-dev/BitCoinSilvercore` | Primary pool coin, built from source. |
| Litecoin Cash (our fork) | `github.com/colneech-dev/litecoincash` | Primary pool coin, built from source. |
| Huntercoin (our fork) | `github.com/colneech-dev/huntercore` | Referenced in notes; not an active pool. |

## Primary pool coins

These have their own stratum pools.

| Pool coin | Algorithm | Upstream repository | Deployed version | Method |
|---|---|---|---|---|
| DigiByte (SHA-256, Scrypt, OdoCrypt pools) | sha256d / scrypt / odocrypt | `github.com/DigiByte-Core/digibyte` | v9.26.7 | Release |
| FxTC | sha256d | `github.com/fxtc/fxtc` | branch `0.18` | Source |
| Mydogecoin | scrypt | `github.com/mydogecoin-project/mydogecoin` | commit `b9748c42a77d91f01a038d3b2a19b898aadd2c69` | Source |
| OrdexCoin | sha256d | `github.com/OrdexCoin/Ordexcoin-Core` | branch `main` | Source |
| Palladium | sha256d | `github.com/palladium-coin/palladiumcore` | v2.0.3 | Source |
| Bitcoin Silver | sha256d | `github.com/colneech-dev/BitCoinSilvercore` | branch `main` | Source (our fork) |
| Litecoin Cash | sha256d | `github.com/colneech-dev/litecoincash` | branch `master` | Source (our fork) |
| WojakCoin | sha256d | `github.com/WojakCoinProj/wojakcore` | branch `rebase-0.12.1` | Source |
| Litecoin II | scrypt | `github.com/litecoinII-project/litecoinII` | v0.21.6.0 | Release |
| Bitcoin II | sha256d | `github.com/Bitcoin-II/BitcoinII-Core` | v29.1.0 | Release |
| GateviaCoin | sha256d | `github.com/gateviaofficial/gateviacoin` | v1.0.0 | Release (zip) |
| BTGS | sha256d | `github.com/BTGSCOINDEV/BTGS` | v30.2.3 | Release |
| Cheetahcoin | sha256d | `github.com/ShorelineCrypto/cheetahcoin` | v2.5.0 | Release |
| CorgiCoin (also merge-mined) | scrypt | `github.com/corgicoin/corgicoin` | v4.5.0.0 | Release |
| Fractal Bitcoin (also merge-mined) | sha256d | `github.com/fractal-bitcoin/fractald-release` | v0.4.0 | Release |
| Scala | Panthera (CryptoNote) | `github.com/scala-network/Scala` | v8.0.3 | Release |

Native hashing for Scala uses `github.com/scala-network/Panthera` at commit `12ce56720e11667e80267edbc7a035a666afa7c2`
(built by `src/Miningcore/build-libs-linux.sh`).

## Merge-mined chains in the pools

Chains that the pools merge-mine today. The aux chain is attached to a parent pool of the matching algorithm.

### SHA-256 parents

| Chain | Upstream repository | Deployed version | Method |
|---|---|---|---|
| Namecoin | `github.com/namecoin/namecoin-core` | built locally, binaries checksum-pinned | Source |
| Myriad (SHA-256 lane) | `github.com/myriadteam/myriadcoin` | v0.18.1.0 | Release |
| Emercoin | `github.com/emercoin/emercoin` | 0.8.5 | Release |
| Terracoin | `github.com/terracoin/terracoin` | default branch, shallow clone | Source |
| SpaceXpanse | `github.com/SpaceXpanse/rod-core-wallet` | v0.6.8.9 | Release |
| Doichain | image `doichain/node-only` (upstream repo not verified) | image `latest` | Docker image |
| Ixcoin | image `blocknetdx/ixcoin` (upstream repo not verified) | v0.14.1 | Docker image |
| Devcoin | image `emfox/devcoin` (upstream is likely `github.com/devcoin/core`, not verified) | image `latest` | Docker image |
| ElevenSeventyFive (ESF) | `github.com/1175Dev/1175` | v29.1.0 | Release |
| Bitcoin Vault (BTCV) | `github.com/bitcoinvault/bitcoinvault` | commit `bfef181` (2.6.0) | Source (compiled locally) |
| Perbug | `github.com/Blocklechain/perbug` | v30.4.2 | Release |
| Electric Cash | `github.com/electric-cash/electric-cash` | v0.27.7 | Release |
| Soqucoin | `github.com/soqucoin/soqucoin` | v2.5.2 | Release |
| SHA256Coin (S256) | `github.com/sha256coin/s256-core` | v3.0.1 | Release |
| Qbit | `github.com/Qbit-Org/qbit` | v1.0.0 | Release |
| VargaMesh (VMESH) | `github.com/ati1993de/vargamesh-core` | v0.2.0 | Release |
| Fractal Bitcoin | `github.com/fractal-bitcoin/fractald-release` | v0.4.0 | Release |
| Hathor | `github.com/HathorNetwork/hathor-core` (image `hathornetwork/hathor-core`) | v0.71.0 | Docker image |
| Rootstock (RSK) | `github.com/rsksmart/rskj` | 9.0.4 | Release |

Hathor also runs the headless wallet image `hathornetwork/hathor-wallet-headless`.

### Scrypt parents

| Chain | Upstream repository | Deployed version | Method |
|---|---|---|---|
| Dogecoin | `github.com/dogecoin/dogecoin` | v1.14.9 | Release |
| Dingocoin | `github.com/dingocoin/dingocoin` | v1.18.1.1 (commit `ce1acdabe819bd2e4194ab5a0f2897f26283ccfc`) | Release |
| Bells | `github.com/Nintondo/bellscoin` | 2.0.0 | Release |
| Pepecoin | `github.com/pepecoinppc/pepecoin` | v1.1.0 | Release |
| Dogmcoin | `github.com/dogmcoin/dogmcoin` | v1.14.7.1 | Release |
| Flopcoin | `github.com/Flopcoin/Flopcoin` | v2.0.0.0 | Release |
| Junkcoin | `github.com/Junkcoin-Foundation/junkcoin-core` | v3.1.1 | Release |
| Luckycoin | `github.com/LuckycoinFoundation/luckycoin` | v5.0.1 | Release |
| Shibacoin | `github.com/shibacoinppc/shibacoin` | v1.2.1 | Release |
| Marscoin | `github.com/marscoin/marscoin` | v28.1.4 | Release |
| Earthcoin | `github.com/Sandokaaan/Earthcoin` | default branch, shallow clone | Source |
| TrumPOW | `github.com/trumpowppc/trumpow` | default branch, shallow clone | Source |
| Dogecoin2 | `github.com/LivingLitecoin/Doge2` | Dogecoin2 v0.0.7 | Release |
| CoiledCoin II | `github.com/coiledcoin2/coiledcoin2` | v31.1-clc2 | Release |
| Unobtanium | `github.com/unobtanium-official/Unobtanium` | v0.11.5 | Release |
| Viacoin | `github.com/viacoin/viacoin` | v0.16.3 plus our local `getauxblock` payout patch | Source (patched) |
| Myriad (Scrypt lane) | `github.com/myriadteam/myriadcoin` | v0.18.1.0 | Release |
| BoskaCoin | `github.com/boskacoin/boskacoin` | commit `6bdd81bae8f90630e0be57a0eecfd87fb7e738c3` (= release v1.0-2) | Source (compiled locally; upstream ships Windows only) |
| CorgiCoin | `github.com/corgicoin/corgicoin` | v4.5.0.0 | Release |
| Mydogecoin | `github.com/mydogecoin-project/mydogecoin` | commit `b9748c42` | Source |
| Bit (B1T) | `github.com/OnlyPW/Bit-core` | v4.0.1.0 | Release |

Viacoin note: stock Viacoin pays all merge-mined rewards to a burn address. Our build patches `getauxblock` to use a wallet
address. See the project notes before upgrading it.

## Installed, syncing, not yet wired

Nodes are running. Sexcoin and NewYorkCoin are already synced and only need wiring; Cyberyen, Argentum, BBQCoin and Meowcoin are still downloading, and Worldcoin has no reachable peer. They are attached to the pools once synced. BoskaCoin, built from source, is already merge-mined.

| Chain | Algorithm | Upstream repository | Deployed version | Method |
|---|---|---|---|---|
| Cyberyen (CY) | scrypt | `github.com/cyberyen/cyberyen` | v0.21.7 | Release |
| Argentum (ARG, SHA-256 lane) | sha256 | `github.com/argentumproject/argentum` | 4.14.5 | Release (no published checksums) |
| BBQCoin (BQC) | scrypt | `github.com/BBQCoinCommunity/BBQCoinV3` | v3.0.1 | Release (no published checksums) |
| Meowcoin (MEWC, Scrypt lane) | scrypt | `github.com/Meowcoin-Foundation/Meowcoin` | v30.2.8 | Release |
| Worldcoin (WDC) | scrypt | `github.com/OfficialWorldcoinGlobal/Worldcoin` | 0.17.1 | Release (no published checksums) |
| Sexcoin (SXC) | scrypt | `github.com/sexcoin-project/sexcoin` | commit `39f60929150154994e4a597f3f06eba254291366` (v0.15.2.2) | Source. The node that matters runs synced beside the pool; a second copy built locally is redundant. |
| NewYorkCoin (NYC) | scrypt | `github.com/NewYorkCoinNYC/NewYorkCoin` | release zip | Release. Runs synced beside the pool; only needs wiring. |
| Bonkcoin (BONC) | scrypt | `github.com/Bonkcoin/Bonkcoin-core` | v1.4.0.0 | Release. Running and syncing, but difficulty is far too high to wire. |
| Lebowskiscoin (LBW) | scrypt | `github.com/blackluv/LebowskiscoinCore` | commit `7dcc0c09f4e375d37e054e898803192cddd1ca6e` (v2.0.0.0) | Source (compiled locally; no release). The repo ships stale prebuilt object files, so the build deletes every `.o`, `.lo`, `.a` and `.la` first. |
| 4547 | scrypt | `github.com/corlovich-boop/4547` | v1.0 (`861063e66`) | Release |

## Prepared but parked or rejected

| Chain | Repository | Why |
|---|---|---|
| NewYorkCoin v2 | `github.com/jamesburrell2/newyorkcoin_v2` (v2.1.1) | A community rewrite whose README warns of consensus incompatibilities. It rejected the live chain's first block, so the original client above is used instead. |
| LinkCoin | `github.com/linkcoinproject/linkcoin` (v2.2.2) | Its seed closes every handshake. |
| Bitmark (BTM) | `github.com/project-bitmark/bitmark` (built from master, commit `ffd840ec`) | Deployed 2026-10-24 and syncing; not yet wired. Multi-algorithm, Scrypt lane via `getauxblock`. No recent release, so it is compiled locally, with one patch for Boost 1.71 (`std::placeholders` to `boost::placeholders` in `src/main.cpp`). |
| Unitus (UIS) | `github.com/unitusdev/unitus` | Rejected. Its algorithms (Lyra2REv2, Skein, Argon2d, X11, Yescrypt) need a parent chain on the same algorithm, which this pool does not have. |

The wider screening of roughly 355 merge-mining repositories, with reasons for using or skipping each, is in
[aux-coin-candidates.md](aux-coin-candidates.md).

## Decommissioned

| Chain | Repository | Notes |
|---|---|---|
| Syscoin | `github.com/syscoin/syscoin` (v5.0.5) | Removed 2026-07. |
| Elastos | `github.com/elastos/Elastos.ELA` (v0.9.9.5 release) | Removed 2026-07. It never produced a block. |
| Xaya | `github.com/xaya/xaya` (v1.7.0.1) | Recipe kept; not running. Difficulty is out of reach. |

## Notes

- **Verifying a release.** The Docker build for each release coin downloads the project's tarball and checks it against a
  pinned SHA-256. Where the project publishes no checksum file (marked above), the hash was computed locally on first
  download and is trusted from then on.
- **Compiled locally.** BoskaCoin, Sexcoin and Bitcoin Vault have no usable Linux release. They were built from the commits
  above in an isolated Ubuntu 20.04 build environment, and the resulting binaries are hash-pinned in the image.
- **Forks we maintain** (Bitcoin Silver, Litecoin Cash) are the only coins built from our own repositories. The rest track
  upstream.
- **Keeping this current.** The authoritative source of truth is the `Dockerfile` for each coin on the servers. To refresh
  this table, read the `BINARY_URL`, `git clone` and `VERSION` lines from each coin's deployment folder.

## Newer upstream releases than what is deployed

Found 2026-10-23 while reviewing which of the screened repositories are still maintained. Not upgraded; each needs a rollback tag
and a check that the chain has not forked.

| Chain | Deployed | Latest upstream release |
|---|---|---|
| CorgiCoin | v4.5.0.0 | v4.6.0.0 |
| Soqucoin | v2.5.2 | v2.5.3 |
| Junkcoin | v3.1.1 | v4.0.3.1 |
| VargaMesh | v0.2.0 | v0.3.0 |
| Xaya (not running) | v1.7.0.1 | v1.11.1 |

## Worldcoin (WDC) repositories

The chain this pool targets (about 11.9 million blocks, 15-second blocks, AuxPoW from block 6,570,530) is the one served by
the first repository below. Checked 2026-10-23.

| Repository | Last push | What it is |
|---|---|---|
| `github.com/OfficialWorldcoinGlobal/Worldcoin` | 2024-01-02 (`develop`) | The official client for the live chain. Release 0.17.1 (2021, Linux x86_64 binary) is what we run. |
| `github.com/Worldcoin-Network/worldcoin` | 2022-02-24 | Same code base; its v0.10.4.x releases only add seed nodes. |
| `github.com/WorldcoinGlobal/WorldcoinLegacy` | 2015-07-10 | Classic Qt 1.1.x wallet. Old chain format, not usable. |
| `github.com/worldcoinproject/worldcoin-v0.8` | 2016-01-30 | Old 0.8 tree with the `worldcoincore.com` seed names, which no longer resolve. |
| `github.com/aburan28/Worldcoin` | 2026-10-19 | A stale mirror of the old code; recent pushes are not new development. |

**Peers.** None of these repositories gives a working peer today; four live ones came from CryptoID's node list instead (`https://chainz.cryptoid.info/wdc/api.dws?q=nodes`). The official client's seed entries are three single IPs and
five `worldcoin.global` names; the names resolve to web servers that do not accept P2P connections, and the IPs refuse
P2P connections. The older trees' `worldcoincore.com` names do not resolve and their fixed IPs are dead. Two addresses in
the repo's node list (`contrib/seeds/nodes_main.txt`) accepted a TCP connection once but time out now and never completed a
protocol handshake.
