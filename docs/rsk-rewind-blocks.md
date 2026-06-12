# RSK RewindBlocks — Fix "Best block not consistent with state db"

## When to use

RSK crashes on startup with:
```
ERROR: Best block is not consistent with the state db.
Consider using `RewindBlocks` cli tool to rewind inconsistent blocks
```

This happens after any unclean shutdown (OOM kill, power loss, docker stop without grace period).

## Fix

Run `RewindBlocks` with the `-rbc` flag (rewind to best consistent block). This automatically finds the last consistent state and rolls back to it.

**Stop the container first**, then run:

```bash
docker stop rsk

docker run --rm \
  --entrypoint sh \
  -v /data/.rsk:/root/.rsk \
  rsk:local -c \
  'java \
    -Drsk.conf.file=/root/.rsk/node.conf \
    -Ddatabase.dir=/root/.rsk/database \
    -Xmx2g \
    -XX:MaxMetaspaceSize=256m \
    -cp /app/ForkDetectionPatch.jar:/app/rsk.jar \
    co.rsk.cli.tools.RewindBlocks -rbc 2>&1'

docker compose -f /deploy/miningcore/docker-compose.yml up -d rsk
```

## Expected output

```
INFO [clitool]  RewindBlocks started
INFO [clitool]  Min inconsistent block number: 8910651
INFO [clitool]  Highest block number stored in db: 8910946
INFO [clitool]  Block number to rewind to: 8910650
INFO [clitool]  Rewinding...
INFO [clitool]  Done
INFO [clitool]  New highest block number stored in db: 8910650
INFO [clitool]  RewindBlocks finished
```

RSK will re-sync the rewound blocks from the network on next start (usually seconds to minutes).

## Other RewindBlocks options

| Flag | Effect |
|------|--------|
| `-rbc` | Auto-rewind to best consistent block (recommended) |
| `-fmi` | Just report the min inconsistent block, don't rewind |
| `-b=<blockNum>` | Rewind to a specific block number |

## Notes

- `-D` JVM flags must come **before** the `-cp` and class name
- Use `--entrypoint sh` to bypass the docker-entrypoint.sh which always runs `co.rsk.Start`
- The rewind only affects the state DB; RSK re-downloads the missing blocks from peers
- `ForkDetectionPatch.jar` must be first in the classpath (it patches `ForkDetectionDataCalculator`)
