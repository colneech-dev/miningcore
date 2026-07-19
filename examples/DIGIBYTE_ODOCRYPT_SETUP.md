# DigiByte OdoCrypt on Miningcore — local setup

Verified end-to-end against DigiByte Core 8.26.2 (regtest) on 2026-06-11.
The OdoCrypt hash Miningcore produces is bit-exact with DigiByte consensus
and an independent FPGA implementation.

## 0. You MUST rebuild Miningcore first

The fix that makes OdoCrypt work touches both layers:
- native `src/Native/libmultihash/odocrypt.{cpp,h}` (was a wrong RNG)
- `src/Miningcore/Crypto/Hashing/Algorithms/Odocrypt.cs` (key formula)

So a stale `bin/` will still be broken. Rebuild the native lib AND the .NET:
```
# native (Linux/WSL build of libmultihash) then:
dotnet publish -c Release --framework net8.0 -o ../../build src/Miningcore/Miningcore.csproj
```
Sanity-check the algorithm without the full stack:
```
cd src/Native/libmultihash && ./build_test_odocrypt.sh   # prints PASS
```

## 1. Node (digibyted) — three things people get wrong

`digibyte.conf` (regtest shown; testnet uses `testnet=1`, `rpcport=14022`):
```
regtest=1
server=1
rpcuser=user
rpcpassword=pass
rpcallowip=127.0.0.1
rpcport=18443
fallbackfee=0.0001
txindex=1
# ZMQ — lets Miningcore detect blocks instantly (else it polls)
zmqpubhashblock=tcp://127.0.0.1:28332
zmqpubrawtx=tcp://127.0.0.1:28332
```

**(a) OdoCrypt is not active until block 600 on regtest.** Below that,
`getblocktemplate ... "odo"` returns *"Algorithm 'odo' is not currently
active."* Mine past it with the default (scrypt) algo:
```
digibyte-cli ... createwallet test
ADDR=$(digibyte-cli ... getnewaddress)
digibyte-cli ... generatetoaddress 601 $ADDR
```
(Testnet/mainnet activate at their own fork heights; a synced node is past
them.) NOTE: a common launcher script generates only 100 blocks — that is NOT
enough; it must be 601.

**(b) The algo name is `odo`** (or `odosha3`) — *not* `odocrypt`. An
unknown name silently falls back to scrypt. Miningcore's coin definition
already passes the right one via `blockTemplateRpcExtraParams: ["odo"]`.

**(c) Confirm it works** before starting Miningcore:
```
digibyte-cli ... getblocktemplate '{"rules":["segwit"]}' 'odo'
# must show "pow_algo": "odo" and an "odokey": <number>
```

## 2. OdoCrypt epoch interval — per network (verified in 8.26.2)

Miningcore computes the key as `nTime - nTime % interval` and the node
validates the submitted block with the same formula on the submitted
nTime, so they always agree **as long as the interval matches**:

| Network | nOdoShapechangeInterval | Miningcore setting |
|---|---|---|
| mainnet | 864000 (10 days) | default — nothing to set |
| **regtest** | **864000 (10 days)** | default — nothing to set |
| testnet | 86400 (1 day) | `MININGCORE_ODO_INTERVAL=86400` |

(Regtest using 864000, not 60, is specific to this 8.26.2 build — verified
against the running node, not assumed from source.)

## 3. Miningcore pool config

Use `examples/digibyte_odocrypt_pool.json` (in this dir). Replace `address`
with one of your node's addresses, point `daemons` at the node's RPC (and
ZMQ), set the Postgres connection. Then:
```
# regtest / mainnet (interval 864000 = default):
dotnet build/Miningcore.dll -c config.json

# testnet (interval 86400):
MININGCORE_ODO_INTERVAL=86400 dotnet build/Miningcore.dll -c config.json
```
Postgres is required — create the DB and import `src/Miningcore/Persistence/
Postgres/Scripts/createdb.sql` first (see the main README).

## 4. Point a miner at it

Stratum is on `127.0.0.1:3052` (per the example). Any standard Stratum-v1
OdoCrypt miner — including the odo-miner FPGA — connects with
`<addr> 3052 <worker>`.

## What was verified on 2026-06-11 (regtest, live node)
- `odocrypt_export` hash == DigiByte 8.26.2 == FPGA (build_test_odocrypt: PASS)
- GBT `"odo"` after height 600 returns `pow_algo: odo` + `odokey`
- block `version` encodes the algo (odo `0x20000E02` vs scrypt `0x20000002`),
  so a submitted block carrying the template version validates as OdoCrypt
- regtest interval is 864000 (odokey was an exact multiple of 864000)
