# Aux (merged-mining) coin candidates

Research log of every AuxPoW / merge-mineable coin looked at for this pool, with the reason each was used or not.
State as of **2026-10-23** (updates in the section below the summary; the zpool section is from 2026-10-22). Public facts only: no server addresses, ports of our own infrastructure, wallet
addresses or credentials are recorded here.

## How the candidates were found

1. **GitHub code search** for AuxPoW identifiers (`nAuxpowChainId`, `nAuxpowStartHeight`, `createauxblock`,
   `getauxblock`, `CAuxPow`, `BLOCK_VERSION_AUXPOW`, `IsAuxpow`, `auxpow.cpp`, `fStrictChainId`, ...): about 346
   distinct repositories in two sweeps.
2. **Repository searches** by keyword and creation date (`auxpow`, `merged mining`, `namecoin fork`, ...).
3. **Forums and pool listings**: bitcointalk announcements, pool "supported merged-mining coins" lists
   (f2pool, ViaBTC, Antpool, Kryptex), and the Shears SHA-256 pool's coin list.
3b. **Other code hosts**: the public search APIs of GitLab, Codeberg, Gitea.com, SourceForge and Bitbucket were queried for
   the same terms and returned essentially no AuxPoW coin projects (Gitee needs a login token). GitHub is where
   these coins live.
3c. **A live pool listing**: the public JSON of a multi-coin pool (`zpool.ca/api/currencies`) flags which coins it
   merge-mines and reports their height, difficulty and recent blocks. This found the strongest candidates and
   corrected several earlier "dead" verdicts that rested on seed hostnames alone.
4. For each plausible chain: read the real `chainparams.cpp` (chain ID, ports, address prefixes, AuxPoW start
   height, seeds), the release page, and the README.
5. **Liveness check without running the coin's code**: resolve each DNS seed and test a TCP connection on the
   coin's own P2P port. A chain with no reachable seed is treated as dead until shown otherwise.
6. **By-hand RPC test** on a node we run (`createauxblock` / `getauxblock`), comparing `target` with `bits`
   before any pool is wired.

Selection rules used throughout:

- The chain must have a **unique AuxPoW chain ID** within each pool (two aux chains with the same ID hang the
  nonce search).
- The coinbase payout must be a **legacy P2PKH address** that belongs to the node's wallet (never a script address).
- The coin must be **alive** (reachable peers, recent blocks) and its algorithm must match a parent we mine
  (SHA-256d or Scrypt).
- Nothing in this list is assumed to have a market. Every coin below is treated as a free lottery ticket, not income.
- Binaries are only used when they can be pinned by SHA-256 (published by the project, or computed by us and
  enforced at image build time).

## Summary

| Coin | Algo | Chain ID | Status | Verdict |
|---|---|---|---|---|
| ElevenSeventyFive (ESF) | SHA-256 | 1175 | Wired | **Use** |
| Bitcoin Vault (BTCV) | SHA-256 | 1638 | Wired | **Use** |
| Perbug (PERBUG) | SHA-256 | 100 | Wired, AuxPoW starts at block 80,000 | **Use** (waiting for activation) |
| Soqucoin (SOQ) | Scrypt | 21329 | Wired | **Use** |
| Electric Cash (ELCASH) | SHA-256 | 8503 | Node synced and tested, not yet wired | **Use** (pending approval) |
| Bit (B1T) | Scrypt | 2840 | Node syncing | **Use** (after sync) |
| 4547 | Scrypt | 4547 | Node waiting for launch (27 Oct 12:00 UTC); AuxPoW from block 4,547 | **Use later** |
| LinkCoin (LNC) | Scrypt | 19534 | Node running, cannot reach any peer | **Parked** |
| Azure (AZURE) | SHA-256 lane | 1901 | Compiling from source | **Undecided, low value** |
| Bitmark (BTM) | Scrypt + SHA-256 lanes | 91 | Not started | **Maybe later** |
| Cyberyen, BoskaCoin, Sexcoin, BBQCoin, NewYorkCoin, Worldcoin, eCanada, Meowcoin, Craftcoin, Bonkcoin | Scrypt | various | Alive and merge-mined per a live pool listing; not yet deployed | **Candidates** (see the zpool section) |
| Argentum (SHA-256 lane) | SHA-256 | - | Alive and merge-mined; about 1.6 days per block | **Candidate** |
| Everything else | | | | **Rejected** (reasons below) |

## Update, 2026-10-23

What changed since the table above was written.

**Deployed.** Nodes for BoskaCoin, Cyberyen, Argentum (SHA-256 lane), BBQCoin, Meowcoin (Scrypt lane), Worldcoin, Bitmark, Lebowskiscoin and Sexcoin
now run. BoskaCoin is merge-mined in the Scrypt pools. Sexcoin and NewYorkCoin (original client) already ran fully synced
beside the pool, are on the live chain tip, and only need wiring into the Scrypt pools. Cyberyen, Argentum, BBQCoin and
Meowcoin are still downloading their chains.

**Corrections to earlier verdicts.**

- *Sexcoin and NewYorkCoin are not "no peers" or "stale".* Both have been synced for months on the pool host (Sexcoin: 4
  peers, NewYorkCoin: 10). The earlier verdicts came from testing seed DNS names and the repo's hard-coded addresses only.
- *The v2.1.1 NewYorkCoin rewrite is still unusable* (it rejects the live chain's first block), but the original
  NewYorkCoin client works, so the coin is fine.
- *Hashes per block are difficulty x 2^32 for Scrypt as well as SHA-256.* Earlier statements that a Scrypt block needs
  difficulty x 2^16 hashes, or "about a second" at our hashrate, were wrong. Realistic times at about 470 MH/s: BoskaCoin,
  Cyberyen and Sexcoin roughly a day; BBQCoin about 10 days; NewYorkCoin and Worldcoin about two weeks or more.

**Why the first screening was weaker than it looked.** The appendix verdicts rest on whether a repo's seed hostnames resolve
and whether its hard-coded peer IPs accept a connection. Of the 355 repositories, 185 were never assessed ("chain
parameters not parsed"), 30 had seed names that no longer exist and 29 had seeds that refuse connections. That test misses
coins whose seeds are dead but whose network is alive. Three better sources of live peers worked on 2026-10-23:

1. The explorer's own peer list. Iquidus-style explorers publish it at `/ext/connections` and `/api/getpeerinfo`
   (BBQCoin's explorer gave the only two live nodes).
2. A seed list that is no longer in the code but still resolves (Bitmark's `zmark.org` seeds).
3. A synced node's own saved peers (`peers.dat`), which only ever held the same few peers.

4. CryptoID's node list. For every coin it indexes, `https://chainz.cryptoid.info/<ticker>/api.dws?q=nodes` returns the node
   addresses it has seen, grouped by client version. This found four live Worldcoin peers (all on the same 0.17.1 client
   we run) after every seed and repository had failed. It also lists nodes for Bitmark (31), NewYorkCoin (16), Sexcoin (10),
   Unobtanium (20) and Canada-eCoin (15). The top-level coin list is `https://chainz.cryptoid.info/explorer/api.dws?q=summary`.

**Independent liveness check of the 355.** Matching the repositories against zpool's and CryptoID's live coin lists matched 56
repos. Almost all are coins already run here or already rejected. New names with signs of life: Bitmark (below) and Unitus.
Repos pushed since 2025 whose latest release is recent are mostly coins we already run; newer releases exist for several of
them (CorgiCoin v4.6.0.0, Soqucoin v2.5.3, Junkcoin v4.0.3.1, VargaMesh v0.3.0, Xaya v1.11.1).

**CryptoID cross-check (2026-10-24).** CryptoID indexes 91 coins. Checked against every coin on our lists (the 355 repos, zpool, and the
candidates above), with two snapshots five minutes apart to see which chains produce blocks:

- Only two are flagged AuxPoW: Canada-eCoin (difficulty 1.18 million, about 130 days per block for us, 15 nodes) and Doichain (already run).
- Coins of ours or evaluated that CryptoID indexes: Bitmark, NewYorkCoin, Sexcoin, Worldcoin, Unobtanium, Ixcoin, DigiByte, Litecoin
  Cash, Litecoin and Doichain. All are covered above. Bonkcoin, Argentum, BBQCoin, Meowcoin, Cyberyen and BoskaCoin are not indexed.
- Of the other 80, the Scrypt and SHA-256 family coins that produce blocks are almost all proof-of-stake (not merge-mineable).
  The pure proof-of-work ones with low difficulty (Smileycoin, e-Gulden, Mooncoin, GoldCoin, Auroracoin, Deutsche eMark, Zetacoin, UFO,
  Trollcoin, Artbyte, Oduwacoin, Vanillacash) have no AuxPoW code in any of their repositories, so none can be merge-mined here.
- Syscoin is merge-mined but its difficulty (14 trillion) is out of reach, which is why it was retired.
- Result: CryptoID gives no new coin to add.

**Automated review of the 113 repositories pushed since 2025 (2026-10-24).** For each: AuxPoW RPCs, port, chain ID, release, and a TCP
test of every seed and fixed peer address. 54 expose AuxPoW RPCs. Of those, the ones that are not already run here and have a
reachable peer are few:

- *Lebowskiscoin (LBW)* - alive. Scrypt, 1-minute blocks, chain ID 0x2040 (8256), port 8245, AuxPoW from block 24,000. A peer at
  its seed `lbw-seed.s3na.xyz` answers with client `/LBW:2.0.0/` and chain height 771,497. The repo `blackluv/LebowskiscoinCore`
  has no release, so it would be compiled (BoskaCoin is a fork of it). Deployed 2026-10-24 and syncing (one peer so far). Difficulty is unknown until the node has synced.
- *DogecoinEV* - dead. Scrypt, chain ID 80, but its DNS seeds do not resolve and none of the 28 fixed peers answers.
- *Coinye, Vien, Fartcoin, TEXITcoin and other Dogecoin forks on chain ID 98* - cannot share a pool with Dogecoin. The Vien
  and dogecoin-clone repositories only answer because they inherit Dogecoin's own seeds.
- *BlakeBitcoin, UniversalMol, HootChain* - Blake and X11 algorithms, which this pool cannot mine.
- *Everything else* (BeersCoin, Flincoin, Kitehcoin, Badcoin, Bit v2, Firebugcoin, Rafsen, Shibclub) - dead seeds, no AuxPoW, or no
  live peer.

**Bitmark (BTM), chain ID 91 (0x5B).** Alive but quiet: about two blocks in ninety minutes on 2026-10-23, which means
our Scrypt hashrate would dominate its blocks, as with CorgiCoin. Multi-algorithm; `getauxblock` mines the Scrypt lane by
default, with no address argument. The repository was pushed in June 2026 but its last release is from 2018, so it is built
from the repository at a pinned commit. Its seeds `ra.zmark.org` and `shiba.zmark.org` resolve to 17 addresses and 11 of
them accept connections on port 9265. Built from master at commit `ffd840ec` (v0.9.7.4) and deployed 2026-10-24 with 8 peers; syncing 2.48M blocks. The build needed one source patch for Boost 1.71 (`std::placeholders` used with `boost::bind` in `src/main.cpp`).

**Unitus (UIS), chain ID 155 (0x9B): rejected.** The chain is alive but slow (five blocks in ninety minutes) and every seed name
is dead. More importantly its algorithms are Lyra2REv2, Skein, Argon2d, X11 and Yescrypt. Merged mining needs a parent
chain on the same algorithm, and this pool has none of them, so Unitus cannot be merge-mined here.

**Worldcoin.** No reachable peer: its seed names point at web servers, not nodes, and the two addresses in its repo that
accept a connection never complete the handshake. The explorer domain it used now redirects to an unrelated site. Four live peers were
finally found through CryptoID (see method 4 above) and the node now syncs.

## Used or in progress

### ElevenSeventyFive (ESF), SHA-256, chain ID 1175
- **Why use:** the only fully live new SHA-256 chain we found. AuxPoW and ASERT difficulty are active (a
  merged ESF block #43,833 was accepted on a live pool in October 2026), 10-minute blocks, MIT licence, a
  prebuilt Linux release with a published `SHA256SUMS` (a checksum list, not a signature), and it works with the
  existing SHA-256 pools unchanged.
- **Odds:** roughly one block every 12 hours at about 21 TH/s of SHA-256.
- **Quirk:** `getauxblock` requires the payout address as its first argument and the chain has no
  `createauxblock`. Miningcore only sent the address for `createauxblock`, so we added an opt-in per-chain
  setting `passAddressToGetAuxBlock` (default off, so Namecoin-style chains are unchanged). Merged in branch
  `fix/aux-getauxblock-address`.
- **Risk:** no market; solo developer.

### Bitcoin Vault (BTCV), SHA-256, chain ID 1638 (0x0666)
- **Why use:** a 2019 chain with AuxPoW from block 58,420, a reachable seed, standard `createauxblock`, and a
  fully synced node (363,000+ blocks). 3.125 BTCV per block at difficulty about 2.6 billion.
- **Notes:** no `target` field in the work response, only `bits`; the pool falls back to expanding `bits`.
  No release binary exists, so we compiled it from source (gcc 13 needed `-include stdexcept -include cstdint`)
  and pinned the resulting binary hashes in the image.
- **Risk:** low maintenance (last commit February 2024); unaudited build.

### Perbug (PERBUG), SHA-256, chain ID 100 (0x64)
- **Why use:** a reachable, actively revived chain. The developer shipped per-block LWMA difficulty (hard fork at
  block 75,725) and then AuxPoW within a day of the Shears pool operator reporting that difficulty was stuck at 6.4M.
  Release v30.4.2 has a published `SHA256SUMS` that matches. Standard `createauxblock`.
- **Activation:** merge-mining only works from **block 80,000**. At the time of writing the tip is around 75,800 and
  only advances when someone mines it. Until then merged blocks we submit are rejected (harmless log noise).
- **Odds once active:** difficulty is about 61,000 now (it will adapt), so blocks would come very quickly.
- **Risks:** a stated **44% premine**; the chain was revived by a small team and could stall again. ASICs that roll
  the version bits overwrite the AuxPoW chain-ID bits, so only merge-mined blocks are valid after the fork.

### Soqucoin (SOQ), Scrypt, chain ID 21329 (0x5351)
- **Why use:** live mainnet (block 1 on 13 October 2026), three working seeds, AuxPoW from genesis, Linux release
  with published checksums, an external audit claimed in the README.
- **Quirks:** mainnet mining RPCs are off by default in the launch period and need `enablemining=1`. Long
  bech32-style payout addresses. Difficulty is volatile on a tiny network.
- **Risk:** brand-new, no market, post-quantum signature scheme not exercised by Miningcore beyond passing the
  address string to the daemon.

### Electric Cash (ELCASH), SHA-256, chain ID 8503 (0x2137)
- **Why use:** AuxPoW from block 1, standard `createauxblock`, fully synced node, ready Linux binaries (static, run
  on any current distro), reachable seed. Difficulty about 1.6 billion: roughly one block every 4 days.
- **Risks:** a disclosed **10% premine** (2.1M of 21M); a staking/governance pitch; no commits since January
  2025; binaries ship without checksums (we pin our own hashes).

### Bit (B1T), Scrypt, chain ID 2840 (0x0B18)
- **Why use:** the project moved to `OnlyPW/Bit-core` (the old repository is abandoned) and the live network is
  reachable through `followthebit.org` seeds. Live block versions already carry the `0x0b18` chain-ID bits, which
  confirms the chain ID. 1-minute blocks, four public explorers, AuxPoW from block 1,000.
- **Notes:** the old repository's chain ID (0x0B17) is stale. The release tarball contains Linux binaries built on
  Debian 12, so the image uses a Debian 12 base.
- **Status:** full sync is hours away (over 600,000 blocks).

### 4547, Scrypt, chain ID 4547 (0x11C3)
- **Why use later:** fair-launch Dogecoin 1.14.9 fork, 4,547-coin hard cap, release binaries with published
  checksums, live seeds and pool. AuxPoW switches on at block 4,547, about 32 days after the 27 October 12:00 UTC
  launch.
- **Why not yet:** merged mining cannot work before block 4,547; mining it natively would divert hashrate from
  Litecoin/Dogecoin. The node is deployed and waiting.
- **Risk:** anonymous author, tiny supply, no stated value.

## Parked

### LinkCoin (LNC), Scrypt, chain ID 19534 (0x4C4E)
- **Looked good on paper:** community revival of a 2014 coin, active development, AuxPoW from block 80,204,
  Linux release.
- **Why parked:** the only two hardcoded seeds are one dead address and one that accepts a TCP connection and then
  closes it before any P2P reply (tested with every plausible protocol version and user agent). The project
  hard-forked three times in September 2026 and an open issue reports the network split into two forks
  ("Luckydog" and "Aika") at different heights, with the website, explorer and Discord down. Revisit if the project
  publishes working peers and says which fork is canonical.

### Azure Coin (AZURE), SHA-256 lane, chain ID 1901
- **Findings:** multi-algo (SHA-256d, Neoscrypt, Yespower) Xaya/Namecoin-style chain with a `createauxblock` that
  reports `algo: sha256d`. One public seed is reachable. It comes from an auto-generated "fork lab" family with a
  **5,000,000 spendable premine** and regtest-level bootstrap difficulty.
- **Why low value:** template fork, single peer, no release on GitHub. Compiling it from source is in progress; use
  only if the build is clean and the chain turns out to be active.

### Bitmark (BTM), chain ID 91
- **Findings:** eight-algorithm chain. The Scrypt and SHA-256d lanes can be merge-mined (an earlier note that
  dismissed it as "wrong algorithm" only described its Equihash lane). Two peers are reachable.
- **Why parked:** Bitcoin 0.9-era code, last binary release in 2018, and the lane is chosen by a global setting, so
  it needs one daemon per lane. Heavy for an uncertain return.

## Found through a live pool listing (zpool.ca, 22 October 2026)

The public API of a multi-coin pool (`https://www.zpool.ca/api/currencies`) flags every coin it merge-mines (`is_aux`,
`merged_with`) and reports live height, difficulty, reward and blocks per 24 hours. That is stronger evidence than a seed
test, because it proves the chain is running and being mined. It lists 34 merge-mined coins: 23 Scrypt (parents
Litecoin and DigiByte), 7 SHA-256 (parent Bitcoin), and 4 of other algorithms (ignored). The ones this pool does not
mine yet:

| Coin | Algo | Height | Difficulty | Blocks/24h | Reward | Notes |
|---|---|---|---|---|---|---|
| **Cyberyen (CY)** | Scrypt | 1,755,466 | 7,158 | 252 | 10,000 | Source `cyberyen/cyberyen`. Reachable: about 18 hours per block on average at 470 MH/s. |
| **BoskaCoin (BOSKA)** | Scrypt | 245,101 | 6,787 | 261 | 20 | Deployed and merge-mined 2026-10-23. Chain ID 0xbf7c; the Windows-only release was compiled from the pinned commit. |
| **Sexcoin (SXC)** | Scrypt | 7,005,599 | 6,656 | 259 | 0.049 | Chain ID 105. A synced node already runs beside the pool (see the 2026-10-23 update); needs wiring. Tiny reward. |
| **BBQCoin (BQC)** | Scrypt | 1,428,622 | 90,392 | 11 | 42 | Chain ID 0x4B74. Source `BBQCoinCommunity/BBQCoinV3`. |
| **NewYorkCoin (NYC)** | Scrypt | 13,134,027 | 102,680 | 9 | 50 | Chain ID 1985. The original client runs synced beside the pool and only needs wiring; the v2.1.1 rewrite is unusable. |
| **Worldcoin (WDC)** | Scrypt | 11,874,884 | 148,035 | 7 | 1 | Old coin with an active seed list. |
| **eCanada (CDN)** | Scrypt | 636,997 | 899,070 | 1 | 2 | Source `Canada-eCoin/eCoinCore`; its hard seeder accepts connections. |
| **Meowcoin Scrypt lane (MEWC)** | Scrypt | 2,122,193 | 682,151 | 3 | 1,500 | Multi-algorithm chain; the Scrypt lane is merged. |
| **Craftcoin (CRC)** | Scrypt | 1,141,459 | 11,983,464 | 0 | 2 | Hard for a small reward. |
| **Bonkcoin (BONC)** | Scrypt | 900,511 | 1,387,964 | 1 | 8,500 | Alive (corrected 2026-10-24). Port 14327, chain ID 148, chain tip about 902,000. Its DNS seed and explorer sites are down, but one peer from the repo's fixed list (`contrib/seeds/nodes_main.txt`) answers in the Bonkcoin protocol and our node syncs from it. Difficulty is about 1.2 million, roughly 120 days per block at our hashrate, so not worth wiring. |
| **Argentum (ARG), SHA-256 lane** | SHA-256 | 7,390,924 | 674,884,447 | 90 | 3 | About 1.6 days per block at 21 TH/s. Source `argentumproject/argentum`. |
| Xaya (CHI) | SHA-256 | 8,033,459 | about 5.05 trillion | 0 | 1.9 | Alive and merged, but the difficulty is out of reach. |

Observations:

- Scale matters. The `dgb-scrypt-solo` pool runs about 470 MH/s. A block needs about difficulty x 2^32 hashes (Bitcoin-style
  difficulty 1, for Scrypt as well as SHA-256), so a Scrypt chain at difficulty 7,000 needs about 30 trillion hashes. At 470 MH/s
  that is roughly 18 hours per block on average. Cyberyen, BoskaCoin and Sexcoin are therefore the cheapest lottery tickets in
  this list, but blocks are only as valuable as the coin, and these have no established market.
- Chain IDs for these are read from each project's own source before any wiring; two chains with the same ID cannot share a pool.
- Zpool also lists live standalone (non-merged) SHA-256 coins that were not in the code searches: BitCash II (BCH2), Fix,
  Kvanta5, Mytherra, Nito, Tarcoin, Xero, Devault, MAZA, Friotaíoch, BlackGold (BGCO, 433,082 blocks, no recent workers).
  They have no AuxPoW and are only useful as pools of their own.

## Rejected

| Coin / project | Reason |
|---|---|
| Blockle (BLOCK, chain ID 16972) | Unidentified team, 210,000 premine, project-run exchange and bridge, unvetted Rust node from crates.io, non-standard `createauxblock` taking a parent-algorithm argument. |
| TEXITcoin, Iskandercoin, Zero Chill Units (ZCU) | Same operator cluster. TEXITcoin is under an emergency cease-and-desist order from a state securities regulator (multi-level-marketing "mining seats"). Avoid the whole cluster. |
| KitehCoin | Seeder and website domains no longer exist; no reachable peer. |
| DogecoinEV | Seeder domains dead, known peers closed. |
| Shiba (shibclub) | No seeds in the code; 1 quadrillion premine. |
| NewYorkCoin v2 (chain ID 1985) | **Reconsider:** the README's four peers refuse connections, but the chain is alive (13.1M blocks, merge-mined). Needs real peers (see the zpool section). |
| Badcoin | Hardcoded seed IPs time out; 21 billion supply. |
| Rafsen (RAF) | Chain ID is 0 and AuxPoW only starts at block 371,337; seed unreachable. |
| Broccolicoin | Seeds and domain gone; chain ID 163 would collide with Flopcoin on the Scrypt pools. |
| BoskaCoin | **Reconsider:** earlier rejected on seed tests, but the pool listing shows it alive and merge-mined (see the zpool section). Windows-only release means it would have to be compiled. |
| Lebowskiscoin | Revival experiment, no release, little activity. |
| Black Gold Coin (BGCO) | Standalone SHA-256 coin with no AuxPoW evidence; website gone; last code about a year old. A different "BGC" DeFi token with the same name exists. |
| Polcoin (PLC) | Alive and maintained, but a standalone coin (not AuxPoW); only useful as its own low-difficulty pool. |
| GoldBrix (GBX) | Standalone SHA-256d fair launch with no AuxPoW. |
| Xaya (CHI) | Alive and merge-mined with Bitcoin (chain ID 1829, standard `createauxblock`), but difficulty is about 5 trillion: unwinnable at our hashrate. Both DNS seeds are gone and the last release was June 2024. (An earlier version of this note called it dead; the pool listing shows it is not.) |
| BLOCH (bloch-sis-pow) | SHA-256d BlockDAG with AuxPoW from local height 8,500, but its README says it stopped on 13 August 2026, with supply concentrated in the founder. |
| WATTx | QTUM-based hybrid chain aimed at Monero merge-mining; no release. |
| Qbit (QBT) | AuxPoW difficulty about 25.6 trillion: unwinnable at our hashrate. |
| VargaMesh (VMESH) | Mandatory 1% fee tooling in the pool bridge; implausible self-reported hashrate; skipped. |
| modelOS, Nacre, Wcash, ZKas | Wrong algorithm (Pearl, Zcash, Kaspa parents). |
| BTQ Core | AuxPoW is only an open proposal. |

## Chains the pool already merge-mined before this research

SHA-256 parents: Namecoin, Emercoin, Terracoin, Ixcoin, Devcoin, Fractal Bitcoin, SpaceXpanse, Unobtanium, Doichain,
Myriad (Scrypt side also), CoiledCoin II, S256 (merge-mining activates at block 17,500), Qbit, VargaMesh, plus the
Rootstock (RSK) and Hathor (HTR) integrations on the SHA-256 pools.
Scrypt parents: Dogecoin, Dogecoin2, Dogmcoin, Dingocoin, Pepecoin, Bellscoin, Luckycoin, Junkcoin, Shibacoin, Flopcoin,
TrumPOW, Earthcoin, Marscoin, Viacoin, Mydogecoin, CorgiCoin (AuxPoW fork at block 50,000). Several of these have
never produced a block; see the earlier notes on difficulty before assuming they will.

## Limits of this research

- Only coins whose chain parameters sit in a public GitHub repository were found. Private or non-GitHub projects
  are invisible to this method.
- The seed test finds a listening port, not a healthy chain. A reachable seed can still be on a fork.
- Where a project publishes no checksums we pinned hashes of the files we downloaded; that detects tampering after
  the fact but does not prove the file was clean when published.
- Market value was not established for any coin.

<!-- APPENDIX -->
## Appendix: every repository screened

346 distinct repositories came out of the GitHub code searches (a third sweep with 20 further queries added nine more, listed after the table). `Port` and `Chain ID` are read from each repo's
mainnet chain parameters (`-` means not found). `Seed DNS` is the best result over that repo's DNS seed hosts on the
coin's own port (OPEN = a TCP connection succeeded, closed = resolves but refuses, nodns = host no longer exists).
`Hardcoded IPs` counts fixed-seed / node-list addresses on the coin's own port that accept a connection
(addresses shared by three or more repos are treated as inherited and excluded). Verdicts for the repos listed
above are the reasoned ones; the rest are mechanical.

| Repo | Last push | Port | Chain ID | Seed DNS | Hardcoded IPs | Verdict |
|---|---|---|---|---|---|---|
| soqucoin/soqucoin | 2026-10-19 | 33388 | 0x5351 | OPEN | 3 of 3 open | USE - wired (Soqucoin) |
| linkcoinproject/linkcoin | 2026-09-11 | 7200 | 0x4C4E | OPEN | 1 of 2 open | PARKED - no reachable peer, chain forked |
| mroarkcrypto/Azure-Coin | 2026-08-03 | 29825 | 1901 | nodns | - | UNDECIDED - compiling, low value |
| 1175Dev/1175 | 2026-07-18 | 25360 | 1175 | - | - | USE - wired (ESF) |
| jamesburrell2/newyorkcoin_v2 | 2026-07-09 | 17020 | 1985 | - | 1 of 4 open | CANDIDATE - NYC is alive and merge-mined; README peers were stale |
| project-bitmark/bitmark | 2026-06-21 | 9265 | 0x005B | OPEN | - | MAYBE LATER - multi-algo, heavy |
| boskacoin/boskacoin | 2026-06-07 | 8035 | 0xbf7c | OPEN | - | CANDIDATE - alive and merge-mined (BOSKA); compile needed |
| JustAResearcher/Meowcoin-Lab | 2026-03-29 | - | - | - | - | CANDIDATE - alive; Scrypt lane merge-mined (MEWC) |
| bittoshimoto/BitV2 | 2025-06-23 | 33317 | 0x0B17 | - | 0 of 3 open | SUPERSEDED - moved to OnlyPW/Bit-core |
| Bonkcoin/Bonkcoin-core | 2025-04-04 | 14327 | 0x0094 | closed | 0 of 3 open | CANDIDATE - alive and merge-mined (BONC); our node found no peers |
| electric-cash/electric-cash | 2025-01-15 | 8333 | 0x2137 | OPEN | - | USE - synced, pending wiring |
| BBQCoinCommunity/BBQCoinV3 | 2025-01-13 | 19323 | 0x4B74 | closed | - | CANDIDATE - alive and merge-mined (BQC) |
| craftcoin2013/craftcoin | 2024-12-16 | 12123 | 0x2024 | nodns | - | CANDIDATE - alive and merge-mined (CRC), hard |
| Muniru0/bitcoinvault | 2024-02-19 | 8333 | 0x0666 | OPEN | - | USE - BTCV clone; the node is built from bitcoinvault/bitcoinvault |
| OfficialWorldcoinGlobal/Worldcoin | 2024-01-02 | 11081 | A | closed | 0 of 11 open | CANDIDATE - alive and merge-mined (WDC) |
| sexcoin-project/sexcoin | 2022-12-29 | 9560 | - | nodns | - | CANDIDATE - alive and merge-mined (SXC) |
| Canada-eCoin/eCoinCore | 2020-11-02 | 34331 | - | OPEN | - | CANDIDATE - alive and merge-mined (CDN) |
| argentumproject/argentum-old | 2017-03-21 | 13580 | - | nodns | 0 of 1 open | CANDIDATE - SHA-256 lane merge-mined (ARG) |
| tiagobeltraoacioli-sketch/bloch-sis-pow | 2026-10-21 | - | 0xB10C0004 | - | - | REJECTED - BLOCH stopped Aug 2026 |
| ati1993de/vargamesh-core | 2026-10-17 | - | - | - | - | REJECTED - VMESH tooling with mandatory fee |
| Qbit-Org/qbit | 2026-10-06 | 8355 | 47 | - | - | REJECTED - AuxPoW difficulty unwinnable |
| blockchainmint1/texitcoin | 2026-10-04 | 15740 | 0x62 | OPEN | - | AVOID - TEXITcoin/Iskander/ZCU cluster |
| The-Mining-Game/WATTx | 2026-09-01 | - | - | - | - | REJECTED - QTUM-based, no release |
| kbadinger/badcoin | 2026-07-05 | 9012 | 0x006A | - | 0 of 3 open | REJECTED - seeds time out |
| shibclub/SHIBA | 2026-06-28 | 33888 | 0x0098 | - | - | REJECTED - no seeds, huge premine |
| blockchainmint1/iskandercoin | 2026-06-10 | 25366 | 0x4953 | OPEN | - | AVOID - TEXITcoin/Iskander/ZCU cluster |
| rafsencoin/rafsen | 2026-05-08 | 18777 | 0x0000 | closed | 0 of 1 open | REJECTED - chain ID 0, AuxPoW at 371,337 |
| DogecoinEV-Foundation/DogecoinEV | 2026-01-30 | 42069 | 0x50 | nodns | - | REJECTED - dead seeds |
| xaya/xaya | 2025-08-19 | - | - | - | - | REJECTED - alive but difficulty ~5e12, unwinnable |
| kitehcoin/Kitehcoin-core | 2025-06-03 | 15034 | 0x0097 | nodns | 0 of 2 open | REJECTED - dead seeds |
| blackluv/LebowskiscoinCore | 2025-05-19 | 8245 | 0x2040 | OPEN | - | REJECTED - revival experiment, no release |
| broccolicoinnet/broccolicoin | 2025-03-05 | 53118 | 0x00A3 | nodns | - | REJECTED - dead seeds, chain ID 163 collides with Flopcoin |
| corgicoin/corgicoin | 2026-10-22 | - | - | - | - | ALREADY MERGE-MINED (CorgiCoin, fork at 50,000) |
| dogecoin/dogecoin | 2026-10-19 | 22556 | 0x0062 | - | - | ALREADY MERGE-MINED (Dogecoin) - reference code |
| domob1812/namecoin-core | 2026-10-19 | - | - | - | - | ALREADY MERGE-MINED (Namecoin) - reference code |
| shibacoinppc/shibacoin | 2026-10-15 | 33864 | 0x004a | OPEN | 0 of 9 open | ALREADY MERGE-MINED (Shibacoin) |
| marscoin/marscoin | 2026-10-12 | - | - | - | - | ALREADY MERGE-MINED (Marscoin) |
| sha256coin/s256-core | 2026-10-12 | - | - | - | - | ALREADY MERGE-MINED (S256) |
| BellsChain/bellscoin | 2026-10-05 | - | - | - | - | ALREADY MERGE-MINED (Bellscoin) |
| Junkcoin-Foundation/junkcoin-core | 2026-09-19 | 9771 | 0x2020 | OPEN | - | ALREADY MERGE-MINED (Junkcoin) |
| pepecoinppc/pepecoin | 2026-09-16 | 33874 | 0x003f | OPEN | 6 of 23 open | ALREADY MERGE-MINED (Pepecoin) |
| devcoin/core | 2026-08-27 | 52333 | 4 | closed | 0 of 3 open | ALREADY MERGE-MINED (Devcoin) |
| trumpowppc/trumpow | 2026-08-16 | 33884 | 0x00A8 | closed | 0 of 3 open | ALREADY MERGE-MINED (TrumPOW) |
| fractal-bitcoin/fractal | 2026-08-10 | - | - | - | - | ALREADY MERGE-MINED (Fractal) |
| LivingLitecoin/Doge2 | 2026-08-07 | 22656 | 0x1d37 | OPEN | 2 of 3 open | ALREADY MERGE-MINED (Dogecoin2) |
| viacoin/viacoin | 2026-07-08 | 5223 | - | OPEN | 1 of 15 open | ALREADY MERGE-MINED (Viacoin) |
| Flopcoin/Flopcoin | 2026-03-18 | 32552 | 0x00A3 | OPEN | - | ALREADY MERGE-MINED (Flopcoin) |
| LuckycoinFoundation/luckycoin | 2025-12-22 | 9917 | 0x2013 | OPEN | - | ALREADY MERGE-MINED (Luckycoin) |
| Sandokaaan/Earthcoin | 2025-09-13 | 35677 | A | OPEN | 2 of 24 open | ALREADY MERGE-MINED (Earthcoin) |
| unobtanium-official/Unobtanium | 2025-06-25 | 65534 | - | closed | 0 of 17 open | ALREADY MERGE-MINED (Unobtanium) |
| luiselzate/luckycoin_1.8 | 2025-05-23 | 9917 | - | - | - | ALREADY MERGE-MINED (Luckycoin) |
| marscoin/blockbook | 2025-01-27 | - | - | - | - | ALREADY MERGE-MINED (Marscoin) |
| mbennett-labs/luckycoinwallet | 2024-11-24 | 9917 | 0x2013 | closed | - | ALREADY MERGE-MINED (Luckycoin) |
| blackluv/junkcoin | 2024-11-22 | 9771 | 0x2020 | nodns | - | ALREADY MERGE-MINED (Junkcoin) |
| CrimsonLuckyLabs/Luckycoin-Wallet-v2 | 2024-11-06 | 9917 | 0x2013 | closed | - | ALREADY MERGE-MINED (Luckycoin) |
| dbkeys/myriadcoin.clone | 2023-12-30 | 10888 | 0x005A | OPEN | 0 of 25 open | ALREADY MERGE-MINED (Myriad) |
| viacoin/viacore-viacoin | 2020-02-09 | 5223 | - | OPEN | - | ALREADY MERGE-MINED (Viacoin) |
| viacoin/viacoin-obsolete | 2017-05-20 | 5223 | - | OPEN | - | ALREADY MERGE-MINED (Viacoin) |
| 0x62e/Dogecoin-fork | }2024-03-03 | 22556 | 0x0062 | - | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| "documentation_url": "https://docs.github.com/en/rest/using-the-rest-api/getting-started-with-the-rest-api#rate-limiting", | ? | - | - | - | - | not a coin node (no chain parameters) |
| "message": "API rate limit exceeded for user ID 256970689. If you reach out to GitHub Support for help, please include the request ID DF26:2723B6:79035:B3367:6AD7D600 and timestamp 2026-10-20 20:58:41 UTC. For more on scraping GitHub and how it may affect your rights, please review our Terms of Service (https:\/\/docs.github.com\/en\/site-policy\/github-terms\/github-terms-of-service)", | ? | - | - | - | - | not a coin node (no chain parameters) |
| "message": "API rate limit exceeded for user ID 256970689. If you reach out to GitHub Support for help, please include the request ID DF27:129D1E:7BD4C:B6198:6AD7D601 and timestamp 2026-10-20 20:58:42 UTC. For more on scraping GitHub and how it may affect your rights, please review our Terms of Service (https:\/\/docs.github.com\/en\/site-policy\/github-terms\/github-terms-of-service)", | ? | - | - | - | - | not a coin node (no chain parameters) |
| "message": "API rate limit exceeded for user ID 256970689. If you reach out to GitHub Support for help, please include the request ID DF28:16AAD3:77F80:B1B69:6AD7D602 and timestamp 2026-10-20 20:58:43 UTC. For more on scraping GitHub and how it may affect your rights, please review our Terms of Service (https:\/\/docs.github.com\/en\/site-policy\/github-terms\/github-terms-of-service)", | ? | - | - | - | - | not a coin node (no chain parameters) |
| "message": "API rate limit exceeded for user ID 256970689. If you reach out to GitHub Support for help, please include the request ID DF29:30A313:79232:B38BE:6AD7D603 and timestamp 2026-10-20 20:58:44 UTC. For more on scraping GitHub and how it may affect your rights, please review our Terms of Service (https:\/\/docs.github.com\/en\/site-policy\/github-terms\/github-terms-of-service)", | ? | - | - | - | - | not a coin node (no chain parameters) |
| "message": "API rate limit exceeded for user ID 256970689. If you reach out to GitHub Support for help, please include the request ID ECC1:23EEBB:7275C:ABF86:6AD7D5FF and timestamp 2026-10-20 20:58:39 UTC. For more on scraping GitHub and how it may affect your rights, please review our Terms of Service (https:\/\/docs.github.com\/en\/site-policy\/github-terms\/github-terms-of-service)", | ? | - | - | - | - | not a coin node (no chain parameters) |
| trezor/blockbook | 2026-10-22 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| whoyoujoshin/aether | 2026-10-22 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| NSEE/VECTO_BR | 2026-10-20 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| frstrtr/c2pool | 2026-10-19 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| xanimo/koinu | 2026-10-19 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| chromatic/whippet-experimental-blockchain | 2026-10-18 | 33666 | 0x0062 | OPEN | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| contiki-ng/coresdk_cc13xx_cc26xx | 2026-10-18 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| frstrtr/p2pool-merged-v36 | 2026-10-17 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| cryptocodes-labs/umbrel-dogecoin-litecoin-merged | 2026-10-16 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| asherp/Book-of-Bitcoin | 2026-10-15 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| lsst-ts/ts_m1m3support | 2026-10-08 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Funnyhust/POC_Kaenx_Creator | 2026-10-07 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| SpiralPool/Spiral-Pool | 2026-10-07 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| code-grey/xomoi-core | 2026-09-28 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| qlpqlp/dogego | 2026-09-28 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| GoPastEverything/Dogecoin-Takeback | 2026-09-15 | 22556 | 0x0062 | - | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| GOLDBRIX-GBX/goldbrix-core | 2026-09-12 | - | - | - | 1 of 3 open | a hardcoded peer IP is reachable - not individually reviewed beyond the scan |
| doged-io/doged | 2026-09-10 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| chuong-vo/sdwsn-elise | 2026-09-09 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Mohamedniang1/Projet_machine_learning | 2026-09-08 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| ozanyn15/Vien | 2026-09-04 | 22556 | 0x0062 | - | 0 of 25 open | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| dogecoinfoundation/libdogecoin | 2026-08-29 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| cslab-pnu/ELK | 2026-08-21 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| bityecoin/coinyecoin | 2026-07-23 | 41338 | 0x0062 | OPEN | 1 of 5 open | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| canxium/go-canxium | 2026-07-19 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| xanimo/libdogecoinconsensus | 2026-07-16 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| verticalcoin/verticalcoin | 2026-07-08 | 54111 | - | nodns | 0 of 10 open | seed hostnames no longer exist |
| spesmilo/electrumx | 2026-07-03 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| BlakeBitcoin/BlakeBitcoin | 2026-07-01 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| BlueDragon747/universalmol | 2026-07-01 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Vin-117/VicCharacterCobaltCore | 2026-06-18 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| 5G-Cash/5G | 2026-06-13 | 22020 | - | OPEN | 1 of 3 open | seed reachable - not individually reviewed beyond the scan |
| Maharab2134/agro-system | 2026-06-08 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| AnduroProject/coordinate | 2026-05-22 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| cslab-pnu/SMORE | 2026-05-21 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| ChengqiLiu/2IMS30-Defence-Design | 2026-05-05 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| mieszki/picos | 2026-04-30 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| jamesburrell2/electrumx_nyc | 2026-04-23 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| bobparkerbob888-tech/happychina-umbrel-apps | 2026-04-22 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| ntilau/IoT | 2026-04-19 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| frostbytenova/pepecoin | 2026-03-28 | 33874 | 0x003f | OPEN | 0 of 12 open | seed reachable - not individually reviewed beyond the scan |
| porkie02/umbrel-app | 2026-03-25 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| lucadagati/contiki-rpl-lab | 2026-03-21 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Imusing/FirebugCoin | 2026-03-12 | 17673 | 0x2040 | closed | - | seeds resolve but refuse connections |
| tanawadeatharva/BharatVECTO | 2026-03-04 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| JustAResearcher/Test | 2026-02-11 | 8788 | 9 | closed | 1 of 5 open | a hardcoded peer IP is reachable - not individually reviewed beyond the scan |
| maithanhduyan/dogecoin_in_rust | 2026-02-04 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Imebeez/Azure | 2026-01-18 | 22556 | 0x0062 | - | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| M77171T/AlNajm | 2026-01-11 | 30001 | 0x0062 | - | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| lucianmocan/nRF52840-DK | 2025-12-22 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Funnyhust/fw_NCN5120_OpenKNXStack | 2025-12-10 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Funnyhust/POC_KNX_Stack | 2025-12-09 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Bi0Dim/Avalon-miner | 2025-11-28 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| thelsing/knx | 2025-11-04 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| syscoin/sys-docs | 2025-10-27 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Funnyhust/TP_UART | 2025-10-24 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| iobond/aib | 2025-10-16 | 5223 | - | closed | - | seeds resolve but refuse connections |
| ProjectFactor/FactWalletServer | 2025-09-20 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| cetic/6lbr | 2025-09-17 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Gujilde163904STI/lifi-project | 2025-07-12 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| techidock/open-source | 2025-07-08 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| ShaoboH2/argonDoge | 2025-05-07 | 22556 | 0x0062 | - | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| cpp-benchmark1/dogecoin | 2025-04-21 | 22556 | 0x0062 | - | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| fartcoin-project/fartcoin | 2025-04-11 | 13377 | 0x0062 | closed | 0 of 1 open | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| Maxson-dev/blockbook | 2025-04-04 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| crislmt/mqtt_temp_simulation-contiki-rednode | 2025-03-31 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| HootChain/HootChain | 2025-03-01 | 6887 | 0x0088 | nodns | - | seed hostnames no longer exist |
| ierfanrajabi/dogecoin | 2025-03-01 | 22556 | 0x0062 | - | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| lyncoin/lyncoin | 2025-02-19 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| flincoin/flincoin | 2025-02-06 | 9777 | 0x0088 | nodns | 0 of 9 open | seed hostnames no longer exist |
| justanyuser/dogecoinEV | 2025-01-28 | 42069 | 0x50 | nodns | - | seed hostnames no longer exist |
| BeersCoin/BeersCoin-BRC | 2025-01-17 | 19618 | 0x007f | closed | 0 of 2 open | seeds resolve but refuse connections |
| SpaceZ-Projects/ElectrumZ-server-win | 2025-01-11 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| blackluv/Tenebrix-Core | 2025-01-01 | 8697 | 0x2047 | closed | - | seeds resolve but refuse connections |
| akthomsen/WSN_Mini_Project | 2024-12-17 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| martinofiore/contiki-ng | 2024-12-16 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| farsider350/AustraliaCash-Core | 2024-12-05 | 1986 | 0x2000 | closed | - | seeds resolve but refuse connections |
| Frogwells/lk | 2024-12-02 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| moirai-project/moirai | 2024-09-21 | 7411 | 0x0062 | nodns | 0 of 6 open | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| IndexChain/Index | 2024-09-03 | 7082 | - | closed | - | seeds resolve but refuse connections |
| Pqcoin/pqcoin | 2024-07-20 | 7332 | 0x0062 | - | 1 of 2 open | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| manlytq/phuongnd | 2024-07-18 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| halal-beef/lk_linaro | 2024-05-12 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| OpenEPaperLink/Radio_AP_Other | 2024-05-10 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| liuchao0052/6lowpan-nd-sec | 2024-04-01 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| dime-coin/electrumx-dimecoin | 2024-03-20 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Hnit3003/contiki_wsn | 2024-03-03 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| namecoin/namecoin-legacy | 2024-02-26 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| unitusdev/unitus | 2024-02-18 | 50603 | 0x9B | nodns | 0 of 21 open | seed hostnames no longer exist |
| mouelhis/contiki | 2024-01-23 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| RoboticsLeon/IoT_Embedded_Application | 2024-01-02 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| dertepass/bader | 2023-12-22 | 22556 | 0x0062 | - | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| GioBar00/NSDS-2023-24 | 2023-12-20 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| bunkercoin/bunkercoin | 2023-11-12 | 14200 | 0x0042 | closed | 0 of 3 open | seeds resolve but refuse connections |
| PlatinumWrist5055/dogecoin | 2023-10-13 | 22556 | 0x0062 | - | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| contiki-ng/cc13xxware | 2023-09-08 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| contiki-ng/cc2640r2-sdk | 2023-09-08 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| contiki-ng/cc26xxware | 2023-09-08 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| martkist/martkist | 2023-05-28 | 4040 | 0xfff | nodns | 0 of 25 open | seed hostnames no longer exist |
| thavrakkku/LabWork | 2023-04-20 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| sdec-brasil/worker | 2023-03-31 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| patricklodder/fctn-blockbook | 2023-02-26 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| 0xGoerliMainnet/radiocoin | 2023-02-05 | 8333 | 0x0062 | nodns | 0 of 25 open | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| ESP-rCommunity/ESP-rSource | 2023-02-04 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| qtumproject/qtum-electrumx-server | 2023-01-21 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| lastditcheffort/nyancoin-client | 2023-01-19 | 33701 | 0x4C4 | nodns | 0 of 23 open | seed hostnames no longer exist |
| Crowndev/crown | 2022-12-25 | 9340 | 20 | closed | - | seeds resolve but refuse connections |
| TheMrRaGe/playchain | 2022-12-20 | 8394 | 1829 | closed | - | seeds resolve but refuse connections |
| SOME-BODY84/AuxPaw_Miau | 2022-11-11 | 33701 | 0x0151 | - | - | no DNS seeds; peers unknown |
| matrach/driverlib | 2022-11-06 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| solopaw/broCoin | 2022-10-06 | 22556 | 0x0062 | - | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| leonardobonati/launchxl-cc2650 | 2022-09-26 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Lkiraa/Contiki-ng | 2022-09-24 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| coughingcatcoin/coughingcatcoin | 2022-08-29 | 34701 | 0x0062 | - | 0 of 5 open | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| klever-io/huobi-blockbook | 2022-07-07 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| demining/dogecoin-Google-Colab | 2022-07-02 | 22556 | 0x0062 | - | 0 of 25 open | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| pbcllc/sfusd-core | 2022-06-20 | 47777 | 0x0333 | nodns | 0 of 4 open | seed hostnames no longer exist |
| photonproject/photon | 2022-03-24 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| NXP/littlekernel-lk | 2022-03-21 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| lithiumcoin/lithium | 2022-03-18 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| blaketrader/ElectronCoin | 2022-03-05 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| wtliang110/lk_hv | 2022-02-24 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| LucasFache/ThesisImprovedOrchestra- | 2022-02-16 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| iot-kntu/controlled-BlueFlood | 2022-02-14 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| priestc/moneywagon | 2022-02-11 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| iot-chalmers/wireless-paxos | 2022-01-28 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| albertolaros/RIOT-security | 2021-11-24 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| VanL/RIOT-Hardening | 2021-10-12 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| benmandrew/ocaml-riot-nrf52 | 2021-09-30 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| benmandrew/omicrob-riot-nrf52 | 2021-09-30 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| bezzantsam/Doge | 2021-09-23 | 22556 | 0x0062 | - | 2 of 25 open | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| liviobi/iot_contact_tracing | 2021-09-06 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| ds-kiel/master-scheduler | 2021-08-25 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| imi415/cc13xx_cc26xxware | 2021-08-10 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| ds-kiel/dimmer | 2021-08-04 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| MultiChain/multichain-explorer | 2021-06-16 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| dima-k144/Penetrationcoin | 2021-06-11 | 22556 | 0x0062 | - | 0 of 25 open | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| terracoin/terracoin | 2021-05-06 | 13333 | 0x0032 | closed | 0 of 25 open | seeds resolve but refuse connections |
| lemoncoin-project/lemoncoin | 2021-04-12 | 22331 | - | nodns | 0 of 1 open | seed hostnames no longer exist |
| RajibTheKing/IOT_Neighbour_Data_Collection | 2021-03-24 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| nandor/llir-riot | 2021-03-02 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Dkpe/Kuberbitcore | 2021-02-24 | 10598 | - | - | - | no DNS seeds; peers unknown |
| shitcoin-core/shitcoin | 2020-12-26 | 9403 | 0x0001 | closed | 0 of 4 open | seeds resolve but refuse connections |
| MiheerKulkarni/Controller-Compare-Thesis | 2020-12-13 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| UsFantom/PaydayCoin-Core | 2020-11-16 | 8123 | 0x1000 | nodns | 0 of 2 open | seed hostnames no longer exist |
| ahmedbodi/quebecoin | 2020-10-20 | 10889 | 0x006C | nodns | 0 of 1 open | seed hostnames no longer exist |
| MotoAcidic/Shroud | 2020-10-02 | 42998 | - | nodns | 0 of 8 open | seed hostnames no longer exist |
| MFrcoin/MFCoin | 2020-09-17 | 22824 | - | nodns | 0 of 2 open | seed hostnames no longer exist |
| nandroidj/rpl-attacks | 2020-09-13 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| FundacionPesetacoin/PesetacoinCore | 2020-08-08 | 16639 | - | closed | - | seeds resolve but refuse connections |
| mtk-watch/android_trusty | 2020-07-28 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| odeonus/nycoin | 2020-06-17 | 17020 | 0x7C1 | closed | - | seeds resolve but refuse connections |
| odeonus/MFCoin | 2020-06-08 | 22824 | - | nodns | 0 of 2 open | seed hostnames no longer exist |
| odeonus/Badcoin | 2020-06-01 | 9012 | 0x006A | - | 0 of 2 open | no DNS seeds; peers unknown |
| atiselsts/tsch-scheduling-comparison | 2020-05-20 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| evdenis/Contiki-NG | 2020-05-07 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| vpubchain/syscoin | 2020-03-26 | 8369 | 0x1000 | OPEN | 0 of 19 open | seed reachable - not individually reviewed beyond the scan |
| MorgothCreator/mSdk | 2020-03-07 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| HondaisCoin/HNDC | 2020-03-03 | 25547 | - | nodns | - | seed hostnames no longer exist |
| dtu-ese/contiki-ng-lora | 2020-03-02 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| iot-chalmers/BlueFlood | 2020-02-27 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| coldwallet2020/billiecoin | 2020-02-12 | 5369 | 0x1000 | nodns | 0 of 19 open | seed hostnames no longer exist |
| Organizational-Proof-Of-Work/viacore-viacoin | 2020-02-09 | 5223 | - | OPEN | - | seed reachable - not individually reviewed beyond the scan |
| YanDevDe/electrumx-pac-instantsend | 2020-01-09 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| YanDevDe/electrumx-ucacoin | 2019-12-26 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Veil-Project/Veil-ElectrumX | 2019-10-19 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| iot-chalmers/IoTLogBlock | 2019-10-17 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| abhiverma866/Project | 2019-09-12 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| prathyu1729/swarm-robotics | 2019-09-02 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| seungbumz/OST | 2019-08-28 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| smthmelv/SeniorDesignMay1622 | 2019-08-13 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| artiqox/artiqox | 2019-07-11 | 19427 | 0x002A | nodns | 0 of 1 open | seed hostnames no longer exist |
| simplerings/simple | 2019-06-15 | 21103 | 0x0062 | nodns | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| AdrenalineAI/Adrenaline | 2019-06-14 | 5232 | - | closed | 0 of 1 open | seeds resolve but refuse connections |
| jincoin/jincoin-core | 2019-06-14 | 23099 | 0x00BA | nodns | 0 of 1 open | seed hostnames no longer exist |
| eGarciaR/contiki-IDS | 2019-05-29 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| HuntCoinDeveloper/huntcoin | 2019-05-17 | 7377 | 0x003c | nodns | - | seed hostnames no longer exist |
| IXCore/IXCoin | 2019-05-10 | 8337 | 0x0003 | closed | - | seeds resolve but refuse connections |
| IXCoin-Dev/IXCoin | 2019-04-16 | 8337 | - | closed | - | seeds resolve but refuse connections |
| badcoin-net/Badcoin | 2019-04-15 | 9012 | 0x006A | - | 0 of 2 open | no DNS seeds; peers unknown |
| duguyifang/dogecoin-1.10.0 | 2019-04-12 | 22556 | 0x0062 | - | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| BerndBaumann/NetLoc | 2019-04-11 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| arronwwj/uzzz3.github.io | 2019-04-08 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| breakcrypto/mnplayground | 2019-04-08 | 8369 | 0x1000 | OPEN | - | seed reachable - not individually reviewed beyond the scan |
| waco-sim/waco | 2019-03-25 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| ETHZ-TEC/LWB-Baseline | 2019-02-19 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| willyko/temp | 2019-02-09 | 8369 | 0x1000 | OPEN | - | seed reachable - not individually reviewed beyond the scan |
| Alaris-Org/Alaris | 2019-01-31 | 9012 | 0x0001 | closed | - | seeds resolve but refuse connections |
| lavajumper/sexcoin-abe | 2019-01-05 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| leafcoin/leafcoin | 2019-01-01 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| leeminghao/wave | 2018-12-25 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| ScriptProdigy/Alaris | 2018-12-11 | 9012 | 0x0001 | closed | - | seeds resolve but refuse connections |
| zhangliuhuoxing/CC2640R2F_Ble5_Central_Peripheral | 2018-11-30 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| domob1812/huntercore | 2018-11-27 | 8398 | - | nodns | - | seed hostnames no longer exist |
| domob1812/crowncoin | 2018-11-25 | 9340 | - | nodns | 0 of 1 open | seed hostnames no longer exist |
| ultra-pool/bitcoinzero | 2018-11-03 | 29301 | - | - | 0 of 25 open | no DNS seeds; peers unknown |
| DanPhelanIDSY/tirtos_cc13xx_cc26xx_2_21_01_08 | 2018-11-01 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| chronokings/huntercoin | 2018-10-15 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| billyzhaoj/hamilton-contiki-port | 2018-10-04 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| zoinofficial/Obsolete-Zoin-Do-Not-Use | 2018-10-01 | 8255 | - | nodns | 0 of 25 open | seed hostnames no longer exist |
| mad-day/xk | 2018-09-13 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| generousRocky/radio-tomography | 2018-08-27 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| hackverket/zcoin | 2018-06-25 | 8168 | - | OPEN | - | seed reachable - not individually reviewed beyond the scan |
| smartgridadsc/Enhancement-of-RPL | 2018-05-22 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| bitmoney-core/lambocoin | 2018-05-20 | 22556 | 0x0062 | - | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| tedchain/gamechain-explorer | 2018-05-17 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| nzsquirrell/testcoin14 | 2018-05-06 | 58333 | 0x123 | nodns | - | seed hostnames no longer exist |
| bitcoinzero/bitcoinzero | 2018-05-03 | 8083 | 0x0abc | nodns | - | seed hostnames no longer exist |
| Hesquibet/cc2640r2_SDK_1.50.00.08 | 2018-04-25 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| syscoin/syscoin1 | 2018-03-20 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| BScong/contiki-sim | 2018-03-02 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| hppcoin/hppcoin | 2018-02-25 | 28878 | - | closed | - | seeds resolve but refuse connections |
| Cybnate/NuBits-Abe-explorer | 2018-02-03 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| LanshunNie/Contiki_1310 | 2017-12-20 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| iobond/aib-old | 2017-12-16 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| romanornr/viacore-viacoin | 2017-12-05 | 5223 | - | OPEN | - | seed reachable - not individually reviewed beyond the scan |
| minersc/msc | 2017-11-29 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| zoin/zoin | 2017-11-24 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| BadgeWiz/TR16Badge | 2017-10-31 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| hanyh2004/zebra | 2017-10-14 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| LanshunNie/LowPower_contiki | 2017-09-25 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| nxcoin/nxcoin-project | 2017-09-14 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Prototanium/Pr | 2017-09-08 | 65525 | - | closed | - | seeds resolve but refuse connections |
| PandaWalz/greencoin | 2017-09-01 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| pesetachain/pesetacoin-0.9 | 2017-08-21 | 16639 | - | closed | - | seeds resolve but refuse connections |
| fedoracoin-dev/fedoracoin | 2017-08-19 | 44890 | - | nodns | 0 of 1 open | seed hostnames no longer exist |
| memcoinio/memcoin | 2017-08-19 | 22556 | 0x0062 | - | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| wenhuizhang/LittleKernel | 2017-08-17 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| contiki-os/cc13xxware | 2017-08-10 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| contiki-os/cc26xxware | 2017-07-25 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| TryCoin-Team/abe-bootstrap | 2017-07-11 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Daparrag/Contiki3.0-CSLMAC | 2017-06-04 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Venussys/venusexplorer | 2017-05-21 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| gguoss/SharedBike-Chain | 2017-05-14 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| smatthewenglish/diff-test | 2017-05-11 | 22556 | 0x0062 | - | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| MatthewLM/PeercoinAbeExplorer | 2017-04-21 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| sschepis/syscoin2 | 2016-10-26 | 8369 | 0x1000 | OPEN | 0 of 25 open | seed reachable - not individually reviewed beyond the scan |
| Cryptcollector/ARG2.0 | 2016-10-15 | 13580 | - | nodns | - | seed hostnames no longer exist |
| peonycredit/peonycredit | 2016-09-30 | 11339 | - | nodns | - | seed hostnames no longer exist |
| tjd33/team14 | 2016-07-20 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| DemonTu/ALL_SmartBatterySwitch_CC2640 | 2016-07-07 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| coolt/bicycle_finish | 2016-07-02 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| presslab-us/ZigBee_SensorTag2 | 2016-07-02 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| domob1812/i0coin | 2016-06-11 | 7333 | 0x0002 | closed | 1 of 6 open | a hardcoded peer IP is reachable - not individually reviewed beyond the scan |
| Richcoin-Project/RichCoin | 2016-06-11 | 11777 | - | nodns | - | seed hostnames no longer exist |
| vaperscoin/vaperscoin | 2016-06-09 | 4444 | - | closed | 0 of 1 open | seeds resolve but refuse connections |
| ElvischenPY/ble-huatune | 2016-05-27 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| saitronkarthi/Over-the-Air-OAD-of-SensorTag-CC2650-Firmware-Android-App | 2016-05-26 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| coolt/sensortag | 2016-05-25 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| coolt/ba_bicycle_comp | 2016-05-16 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| axfunds/AXFunds-old-pow | 2016-05-07 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| aricoin/Aricoin | 2016-02-05 | 16567 | - | closed | - | seeds resolve but refuse connections |
| jupitercoin/jupitercoin | 2015-11-29 | 8568 | 0x0063 | closed | - | seeds resolve but refuse connections |
| adamdunkels/cc26xxware | 2015-10-05 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| jk14/mobilecash | 2015-09-23 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| sjwcoin/sjwcoin-core | 2015-08-09 | 19966 | - | closed | - | seeds resolve but refuse connections |
| vertcoin/eyeglass | 2015-06-25 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| coinkeeper/2015-06-22_18-37_dogecoin | 2015-06-22 | 22556 | - | closed | - | Dogecoin clone using Dogecoin's inherited seeds and chain ID 98 (cannot share a pool with Dogecoin) |
| coinkeeper/2015-06-22_18-41_ixcoin | 2015-06-22 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| mp2apps/pesetacoin-master | 2015-05-09 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Ziftr/namecoin | 2015-02-09 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| chrisfranko/MIM-MA-MM | 2015-01-30 | 13913 | - | nodns | - | seed hostnames no longer exist |
| chrisfranko/MIM-MM | 2015-01-19 | 13913 | - | nodns | - | seed hostnames no longer exist |
| stamhe/namecoin | 2015-01-17 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| bryceweiner/Dirac | 2014-12-01 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| sidhujag/devcoin093 | 2014-11-03 | 52333 | - | closed | 0 of 5 open | seeds resolve but refuse connections |
| UmbrellaHoldings/ULTC | 2014-10-22 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| cqtenq/0.9.3 | 2014-09-23 | 9336 | - | closed | - | seeds resolve but refuse connections |
| BuckyGreen/latiumexplorer | 2014-08-15 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| coinzen/devcoin0.9.2MM | 2014-07-31 | 52333 | - | closed | - | seeds resolve but refuse connections |
| sidhujag/bitcoin-0.9.2-Merge-Mining-fork | 2014-07-31 | 52333 | - | closed | - | seeds resolve but refuse connections |
| JeremyRand/libcoin-old | 2014-07-17 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| sidhujag/devcoin-wallet | 2014-07-05 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| alcamie101/parallaxcoin | 2014-05-05 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| 60E/naocanfen | 2014-04-19 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| fusioncoin/fusioncoin | 2014-04-19 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| azilber/devcoin | 2014-03-03 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| united-scrypt-coin-project/unitedscryptcoin | 2014-01-25 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| ahmedbodi/Bytecoin-MM | 2013-09-29 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| ahmedbodi/Bytecoinv0.1 | 2013-09-11 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| FuzzyBearBTC/CoiledCoin | 2013-07-06 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| petertodd/namecoin | 2013-03-10 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| sjmackenzie/i0coin | 2012-07-01 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| rsnel/coiledcoin | 2012-04-11 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| bytebar/devcoin | 2011-12-31 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| sacarlson/MultiCoin-exp | 2011-10-08 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |
| Lolcust/Tenebrix-daemon-exp | 2011-10-06 | - | - | - | - | chain parameters not parsed (older code layout, or not a coin repository) |

### Third sweep (nine further repositories with AuxPoW source)

Twenty more queries (RPC names, class names, constants) returned 966 repositories, 738 of them new. Only nine of those contain
AuxPoW source; the rest are pool software, miners, websites and unrelated forks. None is a candidate: all nine are old or
abandoned forks.

| Repo | Last push | Port | Verdict |
|---|---|---|---|
| BitcoinZerofficial/bitcoinzero-5.0.1.0 | 2018-12-15 | 29301 | Not used - abandoned (2018) |
| bzxcoin/bitcoinzero | 2018-08-30 | 29101 | Not used - abandoned (2018) |
| ahmedbodi/Terracoin-CRW | 2017-08-18 | 13333 | Not used - abandoned (2017) |
| ahmedbodi/terracoin-dev | 2017-09-09 | 13333 | Not used - abandoned (2017) |
| knotwork/old-devcoin-qt | 2013-12-22 | - | Not used - 2013 experiment |
| knotwork/old-devcoind | 2013-12-22 | - | Not used - 2013 experiment |
| noirofficial/noir | 2022-06-06 | 8255 | Not used - no activity since 2022 |
| odeonus/GravityCoin-1 | 2020-05-29 | 29100 | Not used - abandoned (2020) |
| presstab/crown-core | 2018-11-20 | 9340 | Not used - the maintained Crown project is a separate repository |

**Wired into the Scrypt pools as of 2026-10-24:** BoskaCoin, Sexcoin, NewYorkCoin and BBQCoin (each in all six Scrypt pools, with chain IDs 49020, 105, 1985 and 19316, none of which collide with another chain in those pools). Still syncing and not yet wired: Cyberyen, Meowcoin, Worldcoin, Bitmark, Lebowskiscoin and Argentum.
