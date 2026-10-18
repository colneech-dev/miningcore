#!/usr/bin/env bash
# Generates the SQL that converts blocks.difficulty and auxblocks.difficulty from stratum units
# (hash difficulty x share multiplier) to daemon units for every pool whose coin has a
# shareMultiplier other than 1. Rows written before the 2026-10-18 "difficulty units" change
# (docs/decisions.md) are inflated by that multiplier; SHA256 pools (multiplier 1) are unaffected.
#
#   usage: backfill_difficulty_units.sh <miningcore config.json> <coins.json> | psql -d miningcore
#
# Safe to re-run: a row is only divided while its difficulty is still at least <multiplier> times
# its own network difficulty. A real block beats its network difficulty by a factor that is almost
# never anywhere near 65536, while every inflated row is above it by construction. Aux rows that
# predate the networkdifficulty column cannot be told apart and are left alone.
set -euo pipefail

if [ $# -ne 2 ]; then
  echo "usage: $0 <config.json> <coins.json>" >&2
  exit 1
fi
command -v jq >/dev/null || { echo "jq is required" >&2; exit 1; }

cfg=$1
coins=$2

echo "BEGIN;"
jq -r --slurpfile coins "$coins" \
  '.pools[] | . as $p | ($coins[0][$p.coin].shareMultiplier // 1) as $m | select($m != 1) | "\($p.id)\t\($m)"' "$cfg" |
while IFS=$'\t' read -r pool mult; do
  cat <<SQL
-- pool $pool (shareMultiplier $mult)
UPDATE blocks SET difficulty = difficulty / $mult
  WHERE poolid = '$pool' AND difficulty IS NOT NULL
    AND networkdifficulty > 0 AND difficulty / networkdifficulty >= $mult;
UPDATE auxblocks SET difficulty = difficulty / $mult
  WHERE poolid = '$pool' AND difficulty IS NOT NULL
    AND networkdifficulty IS NOT NULL AND networkdifficulty > 0 AND difficulty / networkdifficulty >= $mult;
SQL
done
echo "COMMIT;"
