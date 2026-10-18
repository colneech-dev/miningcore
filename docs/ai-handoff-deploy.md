# Handoff brief for the AI on the production machine

You are an AI agent on the machine that runs a Miningcore mining pool and its web UI. A human owner
is reachable and decides anything marked HUMAN. This brief tells you what changed, how to roll it
out safely, how to verify it, and what you must never do. Read all of it before running anything.

Real money is involved: the pool pays miners from a wallet. When something is unclear, stop and
ask. A paused deploy costs minutes; a wrong payout or a corrupted database costs real coins.

## 0. Ground rules

1. Do nothing destructive without a backup you have verified exists.
2. Do not push to `main`, force-push, delete branches, or open pull requests. Read-only git on the server.
3. Never print, log, commit or send wallet passwords, RPC passwords, the admin token, API keys, or the
   contents of `config.json`. Refer to them by name only.
4. Never resolve a payout batch (section 7) and never run SQL that changes balances, payments or
   payoutbatches by yourself. These are HUMAN actions, after the human has checked the wallet.
5. After each stage, run its verification. If anything fails, stop, roll back that stage, report.
6. Keep a log of every command you run and its result. Paste it in your final report.

## 1. What this is, and what every branch means

Two repositories (GitHub, owner `colneech-dev`): `miningcore` (a C# pool server, .NET 10, PostgreSQL)
and `Miningcore-Web-UI` (a Node 22 / Express / React dashboard). This file lives on `main` of
`miningcore` so you can read it without checking out a work branch. The work it describes is NOT on
`main`. It sits on branches that have not been merged or reviewed by a human. Branch state below was
measured on 2026-10-18; re-measure it yourself with `git fetch origin` and
`git rev-list --left-right --count origin/main...origin/<branch>` (left number = commits only on main,
right = commits only on the branch).

### miningcore

| Branch | Role | Commits only on main / only on branch |
|---|---|---|
| `main` | What the owner runs today and where upstream-style fixes land. Still .NET 8 and AutoMapper. | reference |
| `ccr-0400960a-70n7cb` | Stage 0. .NET 8 to .NET 10, AutoMapper replaced by Mapperly, dependency security fixes, OdoCrypt test vector, CI audit gate, `docs/decisions.md` started. Base of the two branches below. | 8 / 8 |
| `fix/negative-review` | Stage 0 plus the fixes from an adversarial review: merged mining, security hardening, API, schema migrations, difficulty units. Has no payout batches. | 0 / 25 (contains `main`) |
| `fix/payout-idempotency` | Everything above plus idempotent payouts (a payout batch row is written before coins leave the wallet). This is the tip that contains all the work. | 0 / 30 (contains `main`) |
| `vmesh` | Owner's own line: child-template merged mining, CorgiCoin, legacy-daemon fallbacks, duplicate-block orphaning. Same as `main` before the handoff brief was added. | 1 / 0 |
| `dev`, `rsk` | Older owner branches, 8 commits behind `main` and nothing ahead. Not part of this rollout. Ignore. | 9 / 0 |

The three server work branches are stacked (verified with `git merge-base --is-ancestor`):
`ccr-0400960a-70n7cb` is inside `fix/negative-review`, which is inside `fix/payout-idempotency`.
On 2026-10-18 `main` was merged into `fix/negative-review` and `fix/payout-idempotency` (merge commits,
no history rewritten), so both now contain everything on `main`. `ccr-0400960a-70n7cb` was NOT updated;
it is only a stage 0 base. Never deploy `main` expecting the work, and never deploy
`ccr-0400960a-70n7cb`.

**Deploy branches:** stage A is `fix/negative-review`, stage B is `fix/payout-idempotency`.

### Miningcore-Web-UI

| Branch | Role | Commits only on main / only on branch |
|---|---|---|
| `main` | The dashboard the owner runs today. | reference |
| `ccr-0400960a-70n7cb` | Early dashboard work: npm security fixes (proxy-addr, qs, esbuild, Node 22), a CI gate on high npm advisories, phone layout fixes, a decision log. | 3 / 4 |
| `fix/negative-review` | The dashboard changes that pair with the server fixes (admin gate, proxy trust, API client, tests). Contains the previous branch (verified). | 0 / 11 (contains `main`) |

Web UI `main` had moved by 3 commits (logos, explorer links and tickers for new coins). It was merged
into the web UI `fix/negative-review` on 2026-10-18, so that branch is the web UI deploy branch.
The web UI `ccr-0400960a-70n7cb` branch was not updated; do not deploy it.

### Which server branch goes with which UI branch

Server stage A (`fix/negative-review`-based) pairs with web UI `fix/negative-review`. Server stage B
(`fix/payout-idempotency`-based) pairs with the same UI branch; the UI needs no extra change for payouts.

Read these in the miningcore checkout first; they are the source of truth and explain every choice:
`docs/decisions.md` (dated decision log), `README.md` (the "Colneech Customisations" section),
`CLAUDE.md`. The web UI repo has its own `docs/decisions.md` and `README.md`.

## 2. `main` moves: check it before every deploy

`main` had 7 commits the work branches lacked (a duplicate-block orphaning fix in the payout path, a
child-template merged-mining path, CorgiCoin, legacy-daemon fallbacks). The deploy script does
`git reset --hard origin/<branch>`, so deploying a branch that lacks `main` would DROP those commits.

That was fixed on 2026-10-18 by merging `main` into the two deploy branches. One conflict came up, in
`src/Miningcore/Blockchain/Bitcoin/AuxPoW/AuxPowManager.cs`: two adjacent field declarations
(`lastFailureTicks` from the work, `children` and `childOrder` from main). All three were kept. After the merge
the server test suite passed (254 passed, 0 failed, 6 skipped on the payout tip; 236 passed on stage A).
The 6 skipped tests need native libraries this build machine did not have. The web UI server tests passed
(10 of 10). The web UI browser drive test could not run there because it loads React from a CDN that the
build machine could not reach, so it was NOT run on the merged web UI; run it, or look at the dashboard,
after deploying.

Optional follow-up, not done: main's child-template path calls `ResolveTarget(target, bits)`; it could
use the overload with `out TargetResolution` followed by `LogTargetResolution(block, resolution)`, so an
unusable target is reported.

If `main` gets new commits later, the deploy branch will no longer contain it. Before you deploy, verify:

    git fetch origin
    git merge-base --is-ancestor origin/main origin/<DEPLOY_BRANCH> && echo OK-CONTAINS-MAIN

If that does not print OK-CONTAINS-MAIN, stop and tell the HUMAN: `main` has moved and they must supply a
branch that contains it. The check applies to both repositories. In the commands below, `<DEPLOY_BRANCH>`
is `fix/payout-idempotency` (or `fix/negative-review` for stage A only) for the server, and
`fix/negative-review` for the web UI.

Recommended rollout (HUMAN decides): stage A the review fixes without payout batches, observe for a
day, then stage B the payout-idempotency tip. If the human wants everything at once, that is fine; the
steps are the same.

## 3. What changes operationally (read before deploying)

Behaviour you may see change after the upgrade, all intentional:

- Runtime is .NET 10. A container created from an older image stays pinned to that image and cannot run
  the new binaries (see section 5, step 4).
- TLS certificates to daemons are now validated. A daemon over HTTPS or WSS with a self-signed or
  mismatched certificate needs `"allowInsecureTls": true` on that daemon endpoint in `config.json`.
  Plain `http://127.0.0.1` daemons are unaffected. Optional `"requestTimeoutSeconds"` per daemon.
- Admin API state changes are POST (`logging/level`, `payment/processing/...`). Any script that calls them
  with GET must switch to POST. Admin stays on its own port and IP whitelist.
- Config validation is stricter and the server refuses to start with an invalid config: ports must be
  1 to 65535; `rskChain` and `hathorChain` need `daemons`; `chainId` must be unique across `auxChains`,
  `rskChain` (default 151) and `hathorChain` (default 4646), and two aux chains that both leave `chainId`
  unset (0) now collide; aux `decimals` 0 to 18; aux `diff1`, if set, must be plain hex without `0x`.
- Optional new config: `api.corsOrigins` (list of browser origins allowed; empty keeps "any"),
  `auxChains[].decimals`, `auxChains[].diff1`.
- Stored hash difficulty is now in daemon units (the stratum share multiplier no longer reaches the
  database). Pools whose coin has a `shareMultiplier` other than 1 (scrypt, some groestl) have older rows
  that need a one-off conversion (section 5, step 8).
- Pool effort for the Bitcoin family changed meaning (scrypt pools no longer read 65536 times too high).
  The web UI change that pairs with it is required; deploy both together or effort looks wrong.
- Payouts (stage B): a row is written to the new `payoutbatches` table before every send. The table is
  required; without it payouts are skipped and a Fatal log tells you the migration to run.
- Merged mining: aux submissions are queued and sent in the background; aux block records survive a
  database outage via retries and a `recovered-aux-blocks.jsonl` file next to the share recovery file.

## 4. Preflight (read-only, do all of it, report findings before proceeding)

On the server, record and report:

1. Which containers run, their images and how they were created:
   `docker ps --format '{{.Names}} {{.Image}} {{.Status}}'` and
   `docker inspect miningcore --format '{{.Config.Image}} {{.Image}}'` (same for `miningcore-web-ui`).
   Find how they are started (docker-compose file path, or a run script) and copy those exact options to
   the report; you will need them to recreate the container.
2. The miningcore checkout (the deploy script expects `/gitrepos/miningcore`), current commit and branch,
   and `git status` (must be clean).
3. PostgreSQL: version, database name, size, and that you can run `psql` as the pool's DB user.
   Which migrations exist already: does `auxblocks` exist; do `blocks.difficulty`, `blocks.minereffort`,
   `blocks.worker`, `poolstats.connectedworkers`, `auxblocks.difficulty`, `auxblocks.networkdifficulty`
   exist; does `payoutbatches` exist. A query for each against `information_schema.columns` is enough.
4. Config sanity: read (do not print secrets) `/data/.miningcore/config/config.json` and `coins.json`.
   Report, for each pool: id, coin, any `ssl: true` daemons (candidates for `allowInsecureTls`), whether
   `auxChains`, `rskChain`, `hathorChain` are configured, `chainId` collisions, and any port outside
   1 to 65535. List coins with `shareMultiplier` other than 1 that have pools.
5. Pending money: `SELECT poolid, count(*), sum(amount) FROM balances GROUP BY poolid;` and the age of the
   last payment per pool. Report. If a payout is currently running (check logs), wait for it to finish.
6. Disk space for a database dump, and that `/tmp` has room for the deploy script's backup.

## 5. Rollout procedure

Stop at every VERIFY. Do not skip steps. Times are for the report.

1. **Back up the database** (HUMAN confirms the destination): `pg_dump -Fc <db> > /path/miningcore-pre-upgrade-<STAMP>.dump`.
   Verify it is non-empty and `pg_restore --list` reads it.
2. **Apply schema migrations while the OLD build is still running.** They are additive (new nullable
   columns, new tables) and safe for the old build. They live in
   `src/Miningcore/Persistence/Postgres/Scripts/` and each is safe to re-run. Apply, only the ones
   the preflight showed missing, in this order:
   `add_worker_connectedworkers.sql`, `add_minereffort.sql`, `add_auxblocks.sql`, `add_block_difficulty.sql`,
   and for stage B `add_payoutbatches.sql`.
   `add_block_difficulty.sql` MUST be applied before the new build starts writing blocks.
   VERIFY with the information_schema query from preflight step 3.
3. **Config edits** (show the human a diff first; make a copy `config.json.pre-upgrade`): add
   `allowInsecureTls` where needed, fix any collision found in preflight. Do not change anything else.
4. **Container runtime.** `deploy-server.sh` refuses to continue if the running `miningcore` container's
   image has no .NET 10 runtime. If it refuses: build once (`docker build -t miningcore:local <repo>`),
   then recreate the container from `miningcore:local` with exactly the options you recorded in preflight
   (`docker rm -f miningcore` then your recorded run/compose command), keeping the bind mount of
   `/data/.miningcore/config`. This is a HUMAN-approved step because it restarts the pool.
5. **Deploy miningcore.** In `/gitrepos/miningcore`: `./deploy-server.sh <DEPLOY_BRANCH>`. It builds the
   image (including the native libraries), runs an OdoCrypt consensus check against the built
   `libmultihash.so`, backs up the app files to `/tmp/config-backup-<STAMP>.tar.gz`, stops the container,
   copies the new build over the config directory (keeping `config.json` and `coins.json`) and starts it.
   Record the exact UTC time the new build started: call it `CUTOFF` (needed for step 8).
   VERIFY (give it two minutes): `docker logs --since 3m miningcore` shows no config validation error, no
   `Fatal`, each pool reports it is online and connected to its daemon, shares are accepted, and
   `curl -s localhost:4000/api/pools | jq '.pools[].id'` lists the pools. If the container restarts in a
   loop, read the first error: a validation message names the field to fix; "Framework ... 10.0.0 not
   found" means step 4 was skipped. Roll back (section 8) if you cannot fix it in a few minutes.
6. **Deploy the web UI** (`miningcore-web-ui`, branch `fix/negative-review`): rebuild its image and restart
   its container. Changes: it no longer needs the read-only mount of the pool's config directory or the
   `MININGCORE_CONFIG_PATH` variable; new optional `TRUST_PROXY` (number of reverse proxies in front of it,
   default 1; use 0 if its port is published directly), `FETCH_TIMEOUT_MS`; `/admin/*` returns 404 unless
   `ADMIN_TOKEN` is set. Its config file is `/data/.miningcore-web-ui/config.json` and must keep working.
   VERIFY: the dashboard loads; All Pools shows the pools; a scrypt or groestl pool's effort is plausible
   (a typical value is tens to a few hundred percent, never thousands or millions); the Merge Mining page
   shows per-chain counts; the Aux Blocks page loads.
7. **API checks**: `curl -s 'localhost:4000/api/pools/<id>/blocks?page=0&pageSize=100000' | jq length` is at
   most 500; `curl -s localhost:4000/api/summary | jq '.pools[0].mergeMinedCoins'` shows `totalBlocks` for
   merge-mined pools.
8. **Difficulty backfill** (only for pools whose coin `shareMultiplier` is not 1, and only if they have
   blocks recorded before `CUTOFF`). This is the one step that changes existing data; it needs the
   dump from step 1. Generate the SQL, show it to the HUMAN, and apply only with their approval:

       src/Miningcore/Persistence/Postgres/Scripts/backfill_difficulty_units.sh \
         /data/.miningcore/config/config.json <CUTOFF ISO-8601, e.g. 2026-10-18T14:00:00Z> \
         /data/.miningcore/config/coins.json [other coin template files...] > /tmp/backfill.sql

   Read `/tmp/backfill.sql`: it must contain one block per pool with a multiplier other than 1, each guarded
   by a row in `schema_fixes`, in one transaction. Then `psql -d <db> -f /tmp/backfill.sql`. It converts each
   pool once; running it twice does nothing the second time. VERIFY: `SELECT * FROM schema_fixes;` lists the
   pools, and for one affected pool, recent blocks' `difficulty / networkdifficulty` (luck) is around 0.1 to 10,
   not tens of thousands.

## 6. Payout verification (stage B; do this on the first real payout, with the HUMAN watching)

The idempotent-payout code has been reviewed and unit-tested but never run against a real wallet.

1. Before: note the pool's wallet balance and the miners' balances for the payout (`SELECT * FROM balances`).
2. Watch `docker logs -f miningcore` through the payout. Expected lines: `Payout batch <id>: <n> recipient(s)`,
   then the transaction id, then `Resetting balance of ...`.
3. After: `SELECT id, poolid, status, error, transactionconfirmationdata FROM payoutbatches ORDER BY id DESC LIMIT 5;`
   must show `completed` rows with the txid; `payments` has matching rows; balances are debited exactly once;
   the wallet balance fell by the sum plus fee. Report these numbers.
4. The HUMAN decides whether to also test failure handling (for example stop the database briefly right
   after a send on a test pool). Do not do this on a live pool without their explicit instruction.

## 7. If a payout batch is stuck: DO NOT guess

A batch in status `pending` older than a few minutes means the pool could not tell whether coins moved
(the process died between send and record, or the wallet call timed out). The pool then HALTS payouts for
that pool on purpose and sends an admin notification. Another status, `sent`, is finished automatically
before the next payout; leave those alone.

What you do: read the batch (`GET /api/admin/pools/<poolId>/payoutbatches` on the admin port from an
allowed IP, or SELECT from `payoutbatches`), read its `recipients`, and report to the HUMAN with the wallet's
recent transactions for those addresses. The HUMAN decides one of:

- the coins WERE sent: `POST /api/admin/pools/<poolId>/payoutbatches/<id>/resolve?outcome=sent&txid=<real txid>`
- the coins were NOT sent: `POST .../resolve?outcome=notsent`

Choosing wrongly either way pays miners twice or not at all. You never choose.

## 8. Rollback

- The app files: the deploy script wrote `/tmp/config-backup-<STAMP>.tar.gz` (app files only). Stop the
  container, extract that tarball into `/data/.miningcore/config`, start the container. Note the container
  must also be able to run the older runtime: if you recreated it from the .NET 10 image in step 4, keep
  that image and re-deploy the previous branch with `./deploy-server.sh <old branch>` instead of extracting.
- Schema migrations are additive and need not be reverted. The old build tolerates the new columns/tables.
- The backfill is the only data change. To undo it, restore from the dump in step 1 (HUMAN decision) or
  multiply the affected rows back; do not attempt this without the HUMAN.
- Web UI: redeploy its previous image.

## 9. Known unverified items (call them out in your report with what you observed)

- Payout recovery and compare-and-set against a real database and wallet (section 6).
- RSK merged mining: the pool now treats an `IMPORTED_*` result from `mnr_submitBitcoinBlock` as accepted,
  per RSKj's documented response. Not checked against a live node. After the first RSK block is found,
  check the log line, that a row appears in `auxblocks` for chain `rsk`, and that the RSK confirmer later
  fills in its height and a confirmed status.
- Daemon TLS: any HTTPS daemon that stops connecting after the upgrade needs `allowInsecureTls` or a
  proper certificate.
- `build-ubuntu-22.04.sh` was never run on a 22.04 machine (not used by the deploy script).
- The tracked `build/` directory in the repo holds old .NET 8 binaries; never deploy from it.

## 10. Final report (send to the HUMAN)

Reply with: the branch and commit deployed; the preflight findings; each step with PASS or FAIL and the
evidence (command output, trimmed, secrets removed); the CUTOFF timestamp; whether the backfill ran and
for which pools; the first-payout results if it happened; anything unexpected in the logs; every file you
changed on the machine (config edits with a diff, no secrets); the rollback you would use; and open
questions for the human.
