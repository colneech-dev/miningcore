#!/usr/bin/env bash
# Canonical Miningcore server deploy.
#
# Builds the FULL app (managed .NET + native .so libraries) through the repo
# Dockerfile — whose `native-builder` stage compiles libmultihash et al. from
# src/Native with the correct CPU flags — then extracts the complete publish
# output and installs it over the bind-mounted config dir the container runs from.
#
# This replaces the old broken flow (a bare `dotnet publish -p:SkipNativeLibBuild=true`
# that refreshed ONLY managed code and left stale native .so files in place — which
# silently shipped a non-consensus OdoCrypt hash for weeks). The native-builder layer
# is Docker-cached, so deploys are fast unless src/Native actually changed.
set -euo pipefail

REPO=/gitrepos/miningcore
BRANCH="${1:-rsk}"
CONFIG_DIR=/data/.miningcore/config
IMAGE=miningcore:local
STAMP=$(date +%Y%m%d-%H%M%S)

log(){ echo "[deploy $(date '+%H:%M:%S')] $*"; }

log "Fetching $BRANCH"
cd "$REPO"
git fetch origin -q
git reset --hard "origin/$BRANCH" -q
log "HEAD: $(git log --oneline -1)"

log "Building image (native-builder cached unless src/Native changed)"
docker build -t "$IMAGE" "$REPO"

# Extract the complete published app (managed + native) from the built image.
log "Extracting build output"
CID=$(docker create "$IMAGE")
rm -rf /tmp/mc-deploy && mkdir -p /tmp/mc-deploy
docker cp "$CID":/app/. /tmp/mc-deploy/
docker rm "$CID" >/dev/null

# Never overwrite live config/coins with image defaults.
rm -f /tmp/mc-deploy/config.json /tmp/mc-deploy/coins.json

# Sanity: the native lib and managed dll must both be present.
test -f /tmp/mc-deploy/libmultihash.so || { log "ERROR: libmultihash.so missing from build"; exit 1; }
test -f /tmp/mc-deploy/Miningcore.dll  || { log "ERROR: Miningcore.dll missing from build"; exit 1; }

# Consensus gate on the EXACT libmultihash.so we are about to ship (dlopen the
# real built library, not an isolated recompile — that's what caught the stale
# native that rejected 100% of DGB odo shares).
log "Verifying OdoCrypt consensus of the extracted libmultihash.so"
docker run --rm -v "$REPO/src/Native/libmultihash/verify_odocrypt_so.c":/v.c:ro -v /tmp/mc-deploy:/d:ro ubuntu:24.04 bash -c \
  'apt-get -qq update >/dev/null 2>&1 && apt-get -qq install -y build-essential libssl3 libsodium23 >/dev/null 2>&1;
   cp /v.c /tmp/v.c && cc /tmp/v.c -ldl -o /tmp/verify && /tmp/verify /d/libmultihash.so' \
  | grep -q PASS || { log "ERROR: OdoCrypt consensus gate FAILED on built .so — aborting deploy"; exit 1; }

log "Installing over $CONFIG_DIR (backup: config-backup-$STAMP.tar.gz)"
docker run --rm -v "$CONFIG_DIR":/cfg -v /tmp/mc-deploy:/new:ro -v /tmp:/t ubuntu:24.04 bash -c "
  cd /cfg && tar czf /t/config-backup-$STAMP.tar.gz *.dll *.so Miningcore Miningcore.deps.json Miningcore.runtimeconfig.json runtimes 2>/dev/null || true
"

# Binary is memory-mapped while running — must stop to replace it.
log "Stopping miningcore"
docker stop miningcore >/dev/null
docker run --rm -v "$CONFIG_DIR":/cfg -v /tmp/mc-deploy:/new:ro ubuntu:24.04 bash -c \
  "cp -a /new/. /cfg/ && chmod +x /cfg/Miningcore"
log "Starting miningcore"
docker start miningcore >/dev/null

log "Done. Backup at /tmp/config-backup-$STAMP.tar.gz"
