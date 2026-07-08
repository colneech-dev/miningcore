# Hathor (HTR) Merged Mining — Integration Plan

Status: PLANNED (not started). Written 2026-07-08.

## Why

Hathor is the last high-value SHA256d merged-mining target not in this fork. It is NOT
standard Namecoin-style AuxPoW — it has its own protocol (RFC 0006) — so it needs code,
analogous to the existing RSK integration (`Blockchain/Bitcoin/RSK/RskManager.cs`), which is
the proven pattern to copy: RSK also commits a hash in the parent coinbase and submits a
bundle (header + coinbase + merkle path) to its own node.

## Protocol summary (from HathorNetwork RFC 0006)

- **Coinbase commitment**: `magic || aux_block_hash` embedded in the coinbase scriptSig.
  - magic = `48 61 74 68` (ASCII `Hath`)
  - `aux_block_hash` = sha256d(hathor block-without-nonce), where the block-without-nonce
    serialization = sha256(block_funds) + sha256(block_graph) portions per RFC.
  - The magic must sit at the END of `coinbase_tx_head` (i.e. immediately before the hash)
    and must not occur anywhere earlier in the serialized coinbase — add a guard for this
    (our coinbase already contains arbitrary strings: pool tag, RSK OP_RETURN, aux tree).
- **AuxPOW bundle** submitted to Hathor:

  | field | size |
  |---|---|
  | bitcoin_header_head | first 36 bytes of parent header |
  | coinbase_tx_head | varint + bytes before aux_block_hash |
  | coinbase_tx_tail | varint + bytes after aux_block_hash |
  | merkle_path | varint count + 32B×count (coinbase→root branch) |
  | bitcoin_header_tail | last 12 bytes of parent header |

  Coinbase is tx index 0, so merkle_path is the standard coinbase branch we already compute.
- **Difficulty**: the parent header sha256d hash is compared against the Hathor block's
  weight. Hathor uses logarithmic weight, not bits: target ≈ 2^(256 − weight).
  **VERIFY EXACTLY** against hathor-core source (`daa.py` / `get_mining_info`) before coding —
  off-by-2x here silently loses every block.

## Architecture in this fork

1. `Blockchain/Bitcoin/Hathor/HathorChainConfig.cs` — mirrors `RskChainConfig`:
   `{ id:"hathor", name, daemons:[{host,port}], address (HTR payout address), pollIntervalSeconds, silentErrors }`.
2. `Blockchain/Bitcoin/Hathor/HathorManager.cs` — mirrors `RskManager`:
   - Poll the hathor-core node HTTP API for a mining block template for our address.
     Modern hathor-core exposes `GET /v1a/get_block_template` and `POST /v1a/submit_block`,
     plus a mining websocket (`/v1a/mining_ws`) used by the official merged-mining
     coordinator. **Step 0 of implementation: confirm the exact endpoints/payloads on the
     current hathor-core release** (the RFC predates the current API; the official
     `hathor merged_mining` coordinator source is the reference client to crib from).
   - Build block-without-nonce bytes, compute `aux_block_hash`, expose it + target to the
     job pipeline (same shape as `RskManager` exposes its work).
3. `BitcoinJob` coinbase: append `Hath || aux_block_hash` at the end of the scriptSig
   (after the existing AuxPoW tree commitment and pool tag), i.e. so that the bytes before
   the hash form `coinbase_tx_head` with magic at its end. Add a serializer test asserting
   the magic occurs exactly once in the final coinbase.
4. Share processing (`ProcessShareInternal`): alongside RSK/aux checks, compare header hash
   against the Hathor target; on hit, build the AuxPOW bundle and submit via HathorManager.
   Record in the existing `auxblocks` table with `chainid='hathor'` (it's a text column) —
   same as RSK entries; explorer link `https://explorer.hathor.network/transaction/{hash}`.
5. Confirmation tracking: poll node/explorer for the block hash status, update
   confirmationprogress like other aux blocks.

## Node deployment (server)

- Image: `hathornetwork/hathor-core` (official). Needs a generated peer-id, ~tens of GB,
  and a long first sync unless a snapshot/fast-sync flag is used — check current docs for
  `--x-fast-sync` / snapshot bootstrap.
- Wallet: HTR payout address comes from a Hathor wallet (headless wallet container or
  desktop wallet) — the NODE does not hold the address's keys; `get_block_template` mines to
  any given address. Generate the address in the official wallet, back up its seed into the
  infra backup repo (encrypt or store privately — the seed is 24 words, git repo is private
  but consider gpg).

## Serializer tests

RFC 0006 contains worked byte-level examples — turn them into unit tests
(`Miningcore.Tests/Blockchain/Bitcoin/HathorAuxPowTests.cs`) before wiring live, same way
`AuxPowSerializerTests` covers the Namecoin tree.

## Effort estimate

- Manager + config + coinbase commitment: ~1 day (heavy reuse of RskManager pattern).
- AuxPOW serializer + tests: ~0.5 day.
- Node + wallet deployment and endpoint verification: ~0.5 day.
- Testnet validation before mainnet: point a NerdMiner at a SHA256d pool with Hathor
  testnet configured, confirm accepted merged blocks on the testnet explorer.

## Risks / open questions

- Exact current node API shapes (RFC is old; verify against hathor-core master).
- Weight→target conversion must be exact (2^(256−weight) assumed).
- Coinbase size: commitment adds 36 bytes to scriptSig; we already carry the aux tree
  commitment + RSK output — verify total scriptSig stays ≤100 bytes limit for the sig
  script portion Hathor parses (RFC notes ~44 bytes used of 100) and ≤ Bitcoin's own
  coinbase constraints on each parent chain (some of our parents are old codebases).
- Multiple parents: only wire Hathor into SHA256d pools (like RSK today).
