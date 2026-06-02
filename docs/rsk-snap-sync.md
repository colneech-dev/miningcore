# RSK (Rootstock) Snap Sync Guide

How to sync the RSK blockchain locally using WSL and copy it to the server, avoiding a multi-day full sync from genesis.

## Prerequisites

- WSL2 (Ubuntu) installed and running
- SSH access to the server (`colin@192.168.1.100`)
- RSK container already deployed on the server (`rsk:local` image)

---

## 1. Get the RSKj JAR from the server

```bash
# On the server — copy JARs out of the running container
ssh colin@192.168.1.100
docker cp rsk:/app/rsk.jar /tmp/rsk.jar
docker cp rsk:/app/ForkDetectionPatch.jar /tmp/ForkDetectionPatch.jar
exit

# In WSL — download from server
scp colin@192.168.1.100:/tmp/rsk.jar ~/rsk.jar
scp colin@192.168.1.100:/tmp/ForkDetectionPatch.jar ~/ForkDetectionPatch.jar
```

---

## 2. Install Java in WSL (if not present)

```bash
sudo apt-get update && sudo apt-get install -y openjdk-17-jre-headless libicu-dev
java -version   # should show 17.x
```

---

## 3. Create node.conf

```bash
mkdir -p ~/.rsk/database
cat > ~/.rsk/node.conf << 'EOF'
blockchain.config.name = main

database.dir = /home/colin/.rsk/database
keyvalue.datasource = rocksdb

peer.discovery.enabled = true
peer.port = 5050

rpc.providers.web.http {
    enabled = true
    port = 4444
    bind_address = "127.0.0.1"
    hosts = ["localhost"]
}

rpc.modules = {
    eth { version: "1.0", enabled: "true" }
    net { version: "1.0", enabled: "true" }
}

miner.server.enabled = false
miner.client.enabled = false
wallet.enabled = false

sync.snapshot.client.enabled = true
sync.snapshot.client.parallel = true
sync.snapshot.client.checkHistoricalHeaders = true
sync.snapshot.client.chunkSize = 50
sync.snapshot.client.limit = 10000
sync.snapshot.client.snapBootNodes = [
    {
        nodeId = "f0093935353f94c723a9b67d143ad62464aaf3c959dc05a87f00b637f9c734513493d53f7223633514ea33f2a685878620f0d002cabc05d7f37e6c152774d5da"
        ip = "snapshot-sync-euw2-1.mainnet.rskcomputing.net"
        port = 5050
    },
    {
        nodeId = "e3a25521354aa99424f5de89cdd2e36aa9b9a96d965d1f7f47d876be0cdbd29c7df327a74170f6a9ea44f54f6ab8ae0dae28e40bb89dbd572a617e2008cfc215"
        ip = "snapshot-sync-use1-1.mainnet.rskcomputing.net"
        port = 5050
    }
]
EOF
```

---

## 4. Start RSKj

```bash
java -Xmx3g \
  -Drsk.conf.file=/home/colin/.rsk/node.conf \
  -cp /home/colin/rsk.jar:/home/colin/ForkDetectionPatch.jar \
  co.rsk.Start &
```

**Note:** RSKj logs to `logs/rsk.log` in whichever directory you run the command from — not to `~/.rsk/`. Run from a known location or `cd ~` first.

---

## 5. Monitor progress

```bash
# Watch snap sync chunks (logs in working directory)
tail -f ~/logs/rsk.log | grep -E 'chunkNumber|complete|Snapshot|ERROR'

# Check peer count and block number via RPC
curl -s -X POST -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","method":"net_peerCount","params":[],"id":1}' \
  http://localhost:4444/

curl -s -X POST -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","method":"eth_syncing","params":[],"id":1}' \
  http://localhost:4444/
```

Snap sync progress shows as chunk positions against `total size ~901601392`. Divide current position by total size for percentage. Typically completes in 1–3 hours depending on network speed.

---

## 6. Wait for sync to complete

Sync is complete when `eth_syncing` returns `false` and `eth_blockNumber` matches the current RSK chain tip (~8.9M blocks as of mid-2026).

```bash
curl -s -X POST -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","method":"eth_syncing","params":[],"id":1}' \
  http://localhost:4444/
# Returns: {"result":false} when done

# Kill the local RSKj process
pkill -f 'co.rsk.Start'
```

---

## 7. Copy database to server

```bash
# Stop the server's RSK container first
ssh colin@192.168.1.100 "cd /gitrepos/linux3040-infrastructure/miningcore && docker compose stop rsk"

# Rsync the database (use --delete to clear stale files)
rsync -avz --progress --delete \
  /home/colin/.rsk/database/ \
  colin@192.168.1.100:/data/.rsk/database/

# Restart RSK on the server
ssh colin@192.168.1.100 "cd /gitrepos/linux3040-infrastructure/miningcore && docker compose start rsk"
```

---

## 8. Verify server sync

```bash
ssh colin@192.168.1.100 "docker exec rsk curl -s -X POST \
  -H 'Content-Type: application/json' \
  -d '{\"jsonrpc\":\"2.0\",\"method\":\"eth_syncing\",\"params\":[],\"id\":1}' \
  http://localhost:4444/"
```

Should return the current block close to the chain tip within a few minutes of startup.

---

## Notes

- RSKj uses ~3GB RAM (`-Xmx3g`). Reduce to `-Xmx2g` if WSL is memory-constrained.
- The database directory (`~/.rsk/database/`) will grow to ~10–20GB after a full snap sync.
- Snap sync downloads state at the chain tip, then fills in historical headers backwards. `eth_blockNumber` may stay at `0x0` until the state phase completes even though sync is progressing.
- The `logs/rsk.log` location is relative to the working directory when java was launched, not `~/.rsk/`.
