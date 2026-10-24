#!/usr/bin/env bash
# Generates the SQL that converts blocks.difficulty and auxblocks.difficulty from stratum units
# (hash difficulty x share multiplier) to daemon units, for every pool whose coin has a shareMultiplier
# other than 1. See docs/decisions.md, "Difficulty units".
#
#   usage: backfill_difficulty_units.sh <miningcore config.json> <cutoff> <coins.json> [more coin template files...] | psql -d miningcore
#
# <cutoff> is the moment the build that stores daemon units started writing blocks, as an ISO 8601
# timestamp (for example 2026-10-18T14:00:00Z). Every row created BEFORE it was written by the old code and
# is inflated by the pool's multiplier; every row at or after it is already correct. Using the creation time
# is exact, unlike inferring it from the data: a value-based test (difficulty / networkdifficulty >= multiplier)
# misclassifies about 1 in 256 correct rows at a multiplier of 256 and breaks on aux chains whose network
# difficulty is small or unrecorded.
#
# Safe to run twice: each pool is converted at most once, guarded by a row in schema_fixes written in the
# same transaction. Pass every coin template file the pools use (the first is usually coins.json).
# Review the printed SQL before piping it to psql; it prints, it does not execute.
set -euo pipefail

if [ $# -lt 3 ]; then
  echo "usage: $0 <config.json> <cutoff ISO-8601> <coins.json> [more coin template files...]" >&2
  exit 1
fi
command -v jq >/dev/null || { echo "jq is required" >&2; exit 1; }

cfg=$1
cutoff=$2
shift 2

if ! [[ $cutoff =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}[T\ ][0-9:.]+(Z|[+-][0-9]{2}(:?[0-9]{2})?)?$ ]]; then
  echo "cutoff must be an ISO 8601 timestamp such as 2026-10-18T14:00:00Z, got: $cutoff" >&2
  exit 1
fi

echo "BEGIN;"
echo "CREATE TABLE IF NOT EXISTS schema_fixes (name TEXT NOT NULL PRIMARY KEY, applied TIMESTAMPTZ NOT NULL);"

# merge all template files (later files win), then emit pool id and multiplier, SQL-escaping the id
jq -r --slurpfile cfg "$cfg" --arg q "'" \
  '. as $coins | $cfg[0].pools[] | . as $p | ($coins[$p.coin].shareMultiplier // 1) as $m | select($m != 1)
   | "\(.id | gsub($q; $q + $q))\t\($m)"' \
  < <(jq -s 'add' "$@") |
while IFS=$'\t' read -r pool mult; do
  cat <<SQL
-- pool $pool (shareMultiplier $mult): rows created before $cutoff
DO \$\$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM schema_fixes WHERE name = 'difficulty_units:$pool') THEN
    UPDATE blocks SET difficulty = difficulty / $mult
      WHERE poolid = '$pool' AND difficulty IS NOT NULL AND created < '$cutoff';
    UPDATE auxblocks SET difficulty = difficulty / $mult
      WHERE poolid = '$pool' AND difficulty IS NOT NULL AND created < '$cutoff';
    INSERT INTO schema_fixes(name, applied) VALUES ('difficulty_units:$pool', now());
  END IF;
END
\$\$;
SQL
done
echo "COMMIT;"
