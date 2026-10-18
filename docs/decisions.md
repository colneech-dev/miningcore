# Decision log

Short records of non-obvious choices in this fork: what was decided, why, and what it
costs. Newest first. Each entry names the commit that made the change; the commit message
carries the full detail and the verification that was done.

## 2026-10-18 — Replace AutoMapper with Mapperly (aa04eaeb)

**Decision.** All object mapping is generated at compile time by Riok.Mapperly (Apache-2.0)
in `src/Miningcore/Mapping/MiningcoreMapper.cs`. AutoMapper is gone.

**Why.** AutoMapper 12.0.1 carried an open high-severity advisory (GHSA-rvv3-g6hj-g44x). The
only fixed releases (15.1.1+, 16.1.1+) are dual-licensed by Lucky Penny Software: Reciprocal
Public License 1.5 (strong copyleft that counts deployment) or a paid commercial licence.
Neither was acceptable for a utility library. Mapperly also removes reflection and expression
compilation from startup and turns an unmapped member into a build warning instead of a
silent null at runtime.

**Consequences.** Mappings are explicit, typed methods (`ToEntity`, `ToModel`, `ToResponse`),
so adding a persisted or served type means adding a method. Two behaviours tightened on
purpose: pool-info responses are fresh copies (the API strips wallet secrets from them, and
that must never touch the live configuration), and `payoutSchemeConfig` in `/api/pools` is
now read from the configured JSON rather than left to AutoMapper's handling of a JToken.

## 2026-10-18 — Regenerate the OdoCrypt unit-test vector (3f60a1fd)

**Decision.** `Odocrypt_Hash` now expects the output of the consensus-correct
implementation.

**Why.** The old expected value predated d3b7584 (2026-06-11, "make OdoCrypt
consensus-correct") and was the output of the implementation that commit replaced, so the
test had been failing against every correct build. The new value comes from a build that
passes `verify_odocrypt_so` against the DigiByte reference vector, confirmed by the operator.

## 2026-10-18 — Target .NET 10 LTS (6602e26f)

**Decision.** Both projects target `net10.0`; Dockerfile, CI, docs and build scripts follow.

**Why.** .NET 8 leaves support on 10 November 2026 and receives no security fixes after
that. .NET 9 was a short-term release and ended support in May 2026. .NET 10 is LTS until
November 2028. The move needed no source changes: build and tests are identical on both.

**Consequences.** The Docker build installs the SDK from Ubuntu 24.04's own archive, so the
Microsoft package feed is no longer a build-time dependency. Build scripts for Debian 11 and
Ubuntu 20.04/21.04 were removed: those releases are end-of-life and have no .NET 10 packages.

## 2026-10-17/18 — Dependency clean-up (afe2626a, 9438dff1)

**Decision.** BouncyCastle.Cryptography 2.x replaces Portable.BouncyCastle as our reference;
MailKit 4.18; SHA3.Net removed; System.Security.Cryptography.Pkcs lifted; Argon2 pinned to
the version that actually resolves; test stack (xunit, runner, Test.Sdk) updated.

**Why.** Portable.BouncyCastle 1.x is unmaintained. MailKit 3.x pulled it back in through
MimeKit. SHA3.Net existed for one SHA3-256 call and dragged in BouncyCastle.NetCore 1.8.2 and
NETStandard.Library 1.6, which was the source of every flagged `System.*` transitive. xunit
2.4's NETStandard.Library 1.6.1 was the test project's copy of the same advisories.

**Consequences.** Portable.BouncyCastle 1.8.2 still ships because NBitcoin.Zcash (Equihash,
Beam, Veruscoin) has no newer release and loads it at runtime. It is pinned with an alias so
our `Org.BouncyCastle.Math.BigInteger` resolves to 2.x. NuGet's audit does not flag it; the
reason to prefer 2.x is maintenance, not a specific CVE.

## 2026-10-17 — CI runs on `main`, and the restore is the vulnerability gate (c78b2e0d, 6602e26f)

**Decision.** The workflow triggers on `main` and fails the restore on any unsuppressed
high/critical NuGet advisory, transitive packages included.

**Why.** The workflow had triggered only on `master` and `dev`, neither of which is the
default branch any more, and installed .NET 6 against a net8.0 solution, so nothing on `main`
had been tested by CI. `dotnet list package --vulnerable` ignores `<NuGetAuditSuppress>`
justifications and exits zero on findings; `dotnet restore -warnaserror:NU1902,NU1903,NU1904`
honours them and fails. Verified: exit 0 with a suppression present, exit 1 without.

## 2026-10-17 — Pin the Panthera native source (afe2626a)

**Decision.** `build-libs-linux.sh` checks out a fixed commit of scala-network/Panthera.

**Why.** It was the only unpinned `git clone` in the native build; every other source is on
a tag or commit. A compromised or broken upstream could otherwise change what the next
deploy compiles.

## 2026-10-17 — Warn when an aux chain's target is unusable (503eb9f1)

**Decision.** `AuxPowManager.ResolveTarget` reports how it read the daemon's target, and
`RefreshAsync` warns once per distinct value when there is no usable target or when the
target matches `bits` in neither byte order.

**Why.** 669fcec fixed the byte-order bug that made S256 and Qbit unreachable, but both
remaining failure paths were still silent: the daemon polls fine, candidates are built, and
nothing is ever submitted. These warnings make that visible in the log.

## 2026-10-17 — Deploy from `main` by default (9b5df17)

**Decision.** `deploy-server.sh` defaults to `main`; an explicit branch argument still wins.

**Why.** `main` became the default branch; `rsk` and `dev` were identical and about to be
retired, and the deploy must not depend on a branch that disappears.

## 2026-10-18 — Ship the schema for the hash-difficulty columns, and test for drift

**Decision.** `createdb.sql` and `add_auxblocks.sql` now create `blocks.difficulty`,
`auxblocks.difficulty` and `auxblocks.networkdifficulty`; `add_block_difficulty.sql` adds them
to existing databases; `SchemaDriftTests` fails the build when a repository INSERT or COPY
names a column `createdb.sql` does not create, or when a fork-added column has no
`add_*.sql` migration.

**Why.** f7aa692c and dbb50b63 added the columns to the entities and INSERT statements only.
On a database built from the scripts every block insert failed with "column does not exist",
and because `ShareRecorder` inserts the block inside the share batch's transaction, the whole
batch failed, tripped the circuit breaker and went to the recovery file. Production only
worked because the columns had been added by hand. The test makes the next such drift a
compile-time-cheap failure instead of a production incident.

## 2026-10-18 — Release the aux-submit claim when a submit does not succeed

**Decision.** `submittedAuxHashes` still claims an aux block before submitting (two shares that
meet the same aux target in quick succession must not both submit), but the claim is removed
again on RPC error, explicit rejection, exception, or when no manager owns the block.

**Why.** The claim was never released, so one transient daemon failure made every later share
for the same aux block skip as a "duplicate" and the block was lost. Re-submitting a block the
daemon already rejected is cheap and rare: `RefreshAsync` is invalidated after each submit, so
later shares normally carry a new aux block anyway.

## 2026-10-18 — Record RSK blocks by merged-mining hash and resolve them in the confirmer

**Decision.** After `mnr_submitBitcoinBlock` succeeds the record stores `blockHashForMergedMining`
with `BlockHeight = null` unless the node's `latest` block already carries it.
`ConfirmRskBlocksAsync` scans recent RSK blocks (incrementally, cached, up to 2000 blocks for
old records) for a matching `hashForMergedMining`, then writes the real hash and height.
`AuxBlockRepository.UpdateAsync` therefore also updates `auxblockhash`. A record that has not
appeared on-chain after two hours, with the whole window scanned, is orphaned. Comparisons
accept either hash so legacy rows are upgraded instead of orphaned.

**Why.** The old code stored `eth_blockNumber` (the tip at submit time, not our block) as the
height, so the confirmer compared the tip's hash with our merged-mining hash, found them
different by construction, and orphaned every RSK block. The height-heal path never ran
because the height was not null.

## 2026-10-18 — Aux refresh, aux submits and aux persistence leave the hot paths

**Decision.** `UpdateJob` no longer awaits the aux, RSK or Hathor `RefreshAsync`; a 1 s background
tick (`StartAuxRefreshLoop`) refreshes them and `UpdateJob` reads the cache. Aux submissions are
queued to a single background worker (`ProcessAuxSubmissionsAsync`) instead of running inside
`SubmitShareAsync`. Aux block records are persisted with retries (1 s to 60 s backoff, eight
attempts) and, if that fails, appended to `recovered-aux-blocks.jsonl` next to the share recovery
file.

**Why.** `RpcClient` has a fixed 30 s timeout. With the refresh awaited in `UpdateJob`, one hung
aux daemon delayed every Bitcoin job by up to 30 s per chain, during which miners worked on a
stale template. With the submits inline, the same hang stalled the submitting miner's
`mining.submit`. The fire-and-forget insert gave up on the first database error, losing a block
the daemon had already accepted. Each manager's `RefreshAsync` is already rate-limited to its
poll interval and re-armed by ZMQ and submits, so the 1 s tick costs nothing between polls.

## 2026-10-18 — Pool effort uses the same units as block and miner effort

**Decision.** `GetEffortBetweenCreatedAsync` is `SUM(difficulty / networkdifficulty)` and no
longer takes a share multiplier.

**Why.** `shares.difficulty` is stored normalised (`stratumDifficulty / shareMultiplier`) and
`networkdifficulty` is in daemon units, which is exactly what block effort and miner effort
divide. Multiplying by the multiplier again made `poolEffort` 65536x too high on scrypt pools,
and the web UI had to read `coins.json` from a bind mount to divide it back out. The API is the
one place that knows the units, so it owns the fix; the UI workaround is removed in the same
release. Deploy both sides together: the old UI with the new API shows effort 65536x too low on
scrypt pools, and the reverse too high.

## 2026-10-18 — pageSize is clamped to 500; /summary carries per-chain aux counts

**Decision.** Every paging action clamps `pageSize` to `[1, 500]` (`ApiControllerBase.MaxPageSize`).
`SummaryMergeMineEntry` gains `totalBlocks`, `blocksToday`, `lastBlockTime` and `lastReward`,
computed in one grouped query, and the Hathor chain is now listed.

**Why.** `pageSize` flowed straight into `FETCH NEXT`, so any client could request millions of
rows. The UI asked for 2000 aux blocks twice a minute only to count them per chain; with counts in
`/summary` it asks for nothing and the numbers are right even when a pool has more aux blocks
than one page. Hathor was the only merged chain missing from the list.

## 2026-10-18 — Payout result collections are locked

**Decision.** `BitcoinPayoutHandler`, `HandshakePayoutHandler` and `WarthogPayoutHandler` take a
lock around `successBalances.Add` and `txFailures.Add` inside `Parallel.ForEachAsync`.

**Why.** `Dictionary` and `List` are not safe for concurrent writes; with eight parallel sends a
corrupted collection either throws (payment sent, nothing persisted) or silently drops an entry
(payment sent, balance not debited, paid again next run).

## 2026-10-18 — Difficulty units: everything stored in daemon units, the share multiplier never reaches the database

**Decision.** `Share.HashDifficulty`, and therefore `blocks.difficulty` and `auxblocks.difficulty`,
are the hash's difficulty against the chain's difficulty-1 target with no stratum share
multiplier, the same unit as `shares.difficulty` and every `networkdifficulty` column. Aux chains
can declare their own `diff1`; the parent's hash difficulty is rescaled by the ratio of the two
targets (identical for every Bitcoin-derived chain, and used as-is for RSK and Hathor). Helpers
and the convention live in `Blockchain/Bitcoin/DifficultyUnits.cs`; existing scrypt rows are
corrected with `Persistence/Postgres/Scripts/backfill_difficulty_units.sh`.

**Why.** Every ratio the pool computes (effort, miner effort, block luck) divides a difficulty
by a network difficulty, and network difficulty only exists in daemon units. The share multiplier
(65536 for scrypt, 256 or 4096 for groestl coins, monacoin unlike other scrypt coins) is a display
convention for miner software with no on-chain meaning; storing it made `blocks.difficulty` and
`auxblocks.difficulty` 65536x too high on scrypt pools while the columns next to them were not.
If a miner-facing number is ever wanted, the API should expose the multiplier as metadata and
the UI multiply at render time.

## 2026-10-18 — Daemon TLS certificates are validated; RPC timeouts are per daemon and per call

**Decision.** `RpcClient` (and the Alephium and Ergo client factories) validate TLS certificates
unless the daemon endpoint sets `allowInsecureTls: true`. Each `RpcClient` owns its `HttpClient`.
The client-level timeout is gone; every request carries its own, defaulting to the endpoint's
`requestTimeoutSeconds` (30) and overridable per call. Aux refresh uses 10 s.

**Why.** Every daemon connection accepted any certificate, so TLS to a remote daemon protected
nothing. The single 30 s timeout was far too long for anything on a hot path and could not be
changed without changing it for `sendmany` too. Operators with self-signed daemon certificates
must now set `allowInsecureTls` on that endpoint; plain-HTTP localhost daemons are unaffected.

## 2026-10-18 — An aux-work change is a rebroadcast, not a new block

**Decision.** `UpdateJob` tracks aux changes separately from `isNew`. An aux-only change rebuilds
the job and rebroadcasts it with `clean_jobs = false`, logs at Debug, and leaves
`LastNetworkBlockTime` and the block-height stats alone.

**Why.** Aux changes used to set `isNew`, so the log said "Detected new block" on every aux
template, the pool stats recorded aux cadence as primary block cadence, and miners were told to
drop their jobs for a change that did not invalidate their work.

## 2026-10-18 — Aux rewards use the chain's decimals

**Decision.** `AuxChainConfig.Decimals` (default 8) converts the aux coinbase value to the
recorded reward. Validation rejects values outside 0..18.

**Why.** The reward was divided by 10^8 for every standard aux chain regardless of its precision.

## 2026-10-18 — Shares in the tolerance band are credited what they proved; difficulty comes from bits

**Decision.** `BitcoinJob` records `min(stratumDifficulty, hashDifficulty)` for a share, so a
share accepted inside the 3 % tolerance below the worker's difficulty is credited its actual
difficulty, not the full stratum difficulty. Job difficulty is derived from `bits`, the same
field the block target already used; a template whose `target` disagrees is logged once.

**Why.** The tolerance existed to avoid false rejects around vardiff changes, but crediting full
difficulty for a 3 % miss was a small standing overpayment a miner could aim for. Deriving
difficulty from `target` while the block target came from `bits` let the two disagree on daemons
that report `target` in the wrong byte order, recording a wrong network difficulty on every share.

## 2026-10-18 — Hardening: stratum TLS, PROXY header, admin API, CORS, notifications, effort sweep

**Decision.**
- Stratum TLS accepts 1.2 and 1.3 only; a PROXY protocol v1 header longer than 108 bytes or with
  fewer than six fields closes the connection.
- Admin endpoints that change state (log level, payment processing on/off) are POST, and the log
  level change re-levels the existing NLog rules instead of appending a new rule on every call.
- CORS allows cross-origin GET/HEAD only, from any origin by default or from `api.corsOrigins`
  when set.
- The share-recovery admin alert is gated on admin notifications being enabled, not on the
  unrelated payment-success flag. Email is optional: an unset `notifications.email` logs a warning
  instead of throwing in the constructor, and message bodies are HTML-encoded.
- Daemon errors during the connection check no longer null-reference on the missing version.
- The suspicious-effort ban check runs once a minute on its own timer, one query per distinct
  miner, instead of two queries per connection on every job broadcast and vardiff pass.
- Config validation rejects ports outside 1..65535, RSK or Hathor sections without daemons, and
  chain ids shared between aux, RSK and Hathor chains.

**Why.** Each is a finding from the Oct 2026 negative review; none changes
behaviour for a correctly configured pool except the CORS method restriction and the admin
endpoints' verb, both of which only affect clients that were mutating state through a browser.

## 2026-10-18 — Hygiene batch

**Decision.**
- Per-job and per-connection diagnostics (founder-fee parameters, `mining.configure` details,
  version-rolling mask) log at Debug.
- `StatsRecorder` resets a pool's performance stats once per tick; the second, overlapping reset
  block is gone.
- `PoolApiController` no longer re-deserialises `payoutSchemeConfig`; the mapper already does.
- Native hashing contexts use `DateTime.UtcNow`.
- The payout retry log prints the retry number instead of the literal `{1}`.
- The one silent `catch { }` (cluster API enrichment) logs at Debug. The review's "57 empty catch
  blocks" was an overcount: 23 are `catch(OperationCanceledException)` on shutdown paths and
  the rest carry a comment explaining the fall-through.
- Aux confirmers write a row only when its status or progress changed, and run the deep-reorg
  re-check on every tenth tick.
- `AuxChainConfig.PollIntervalSeconds` keeps its default of 10 (RSK and Hathor use 5) and the
  comment now says why: aux chains are also invalidated by ZMQ and after each submit.
- `examples/digibyte_sha256_merged_pool.json` shows every merged-mining option.
- Tests that need a native library are marked `[NativeLibFact("lib...")]` and skip with a reason
  when it cannot be loaded, instead of failing with `DllNotFoundException`.

**Why.** Log volume was dominated by two per-connection lines; the duplicate reset masked which
path was needed; the hand mapping drifted from the mapper; local time breaks timing across a
DST change. The native-test change makes a plain `dotnet test` meaningful on a machine without
`build-libs-linux.sh` output: before, 36 red tests hid any real failure.

**Not done.** The 37 `#pragma warning disable` directives (mostly CS4014, CS1591, CS3021,
CS0612) are left; removing them needs the underlying warnings fixed one by one. The RVH/ANOK
witness-commitment question needs those coins' consensus rules to settle.

## 2026-10-18 — Self-review fixes: aux manager lookup by chain id, refresh loop without a backlog

**Decision.** The aux submit worker finds the manager by `ChainId` and submits even when the
manager has moved on to a newer template; the refresh loop is a plain loop with a one second
pause instead of `Observable.Interval(...).Concat()`.

**Why.** Both were introduced earlier the same day when aux refresh and submits moved off the hot
paths. Looking the manager up by "is this still its current aux block" was harmless inline but,
behind a queue, a submission processed after the next template arrived was dropped as
"superseded": a valid mined block lost. The daemon is the right judge of staleness. The Rx loop
queued one deferred pass per second while a daemon was slow, a backlog that only drained after
recovery.

## 2026-10-18 — Review round 2 (server): corrections to the entries above

**Decision.** Fixes from an independent review of `fix/negative-review`:
- Aux submissions are claimed when queued (not in the worker), run up to eight at a time, and
  are dropped after five minutes in the queue. The single serial worker let one hung daemon hold
  up a valid block for a healthy chain, and duplicates reached the queue.
- The RSK confirmer reads only pending rows without a height. An RSK block that never reached
  the main chain is orphaned without a height; the unfiltered query kept returning it, rewrote it
  every tick, pinned the lookback at 2000 blocks and, after 50 of them, starved newer rows.
- The suspicious-effort sweep survives an error in a tick (it used to end for good on the first
  database timeout); `RunAsync` has explicit braces around the vardiff task.
- The PROXY header length bound applies to lines that start with `PROXY ` only; with an optional
  header a long first request (a `mining.authorize` over 108 bytes) was being rejected.
- **CORS methods are unrestricted again.** The earlier entry said nothing needs cross-origin
  writes; the public miner-settings endpoint is a POST that third-party UIs call from the browser.
  `corsOrigins` still narrows origins.
- `diff1` is validated at startup (plain hex) and parsed with a fallback, so a bad value can no
  longer throw while the record of an already-accepted aux block is built.
- Aux persistence retries stop on shutdown by writing the record to the recovery file; the
  confirmation timer and the refresh loops are plain loops, one refresh loop per chain.
- A failed aux/RSK/Hathor poll is not retried before the poll interval has passed.
- The `target`/`bits` mismatch warns once, then logs at Debug.
- RSK: an object response with `blockImportedResult: IMPORTED_*` counts as accepted. RSKj
  documents this response shape; before, any object counted as a rejection. **Not verified against
  a live node from here: check with one found block.**

**Not changed, with reasons.**
- A reviewer said daemon units are 65536x off for scrypt coins. They are not: Bitcoin forks'
  `getdifficulty` use the same formula (reference `0x00ffff` at exponent 0x1d), which is what
  NBitcoin's `Target.Difficulty` computes; the 65536 only appears in the stratum share
  difficulty, which is exactly what is divided out.
- Block candidates are credited `min(stratumDifficulty, hashDifficulty)` too, not only shares in
  the tolerance band; a candidate below the worker's difficulty is credited what it proved.

## 2026-10-18 — Review round 3 (API and persistence): corrections

**Decision.**
- **Backfill is keyed on creation time, not on the data.** `backfill_difficulty_units.sh` now takes a
  required cutoff (when the new build began writing) and converts rows created before it, once per pool
  (a `schema_fixes` marker row in the same transaction), across any number of coin template files, with
  pool ids SQL-escaped. The earlier guard (`difficulty / networkdifficulty >= multiplier`) was wrong: about
  1 in 256 correct rows qualifies at a multiplier of 256, aux chains with small network difficulty or an
  unrecorded one are misclassified either way, and a re-run divided correct rows again.
- **Pool effort keeps the share multiplier outside the Bitcoin family.** `IMiningPool.EffortShareConstant`
  is 1 for `BitcoinPool` (whose shares are stored as `stratumDifficulty / shareMultiplier`, the unit block
  effort divides by) and the share multiplier everywhere else, which is what those pools returned before.
  Dropping the multiplier for every family, as the earlier entry did, made Ergo pool effort 256x too low:
  Ergo, Kaspa and Alephium scale block effort in `AdjustBlockEffort`. The web UI never divided those pools
  (their multiplier is a constant in the pool class, not in `coins.json`), so nothing changes for the UI.
- `pageSize` and `page` are clamped on the two cluster endpoints (`/api/blocks`, `/api/auxblocks`) as well,
  so the earlier "all paging actions" is now true; a negative or huge `page` no longer reaches `OFFSET`.
- The RSK confirmer no longer stalls on a duplicate: if resolving a row to its real hash hits the unique
  index (the same merged-mining work submitted twice, possible after a restart empties the in-memory claim
  set), the row is marked orphaned under its own hash instead of failing the whole pass every tick.
- `/api/summary`: an orphaned aux block no longer supplies a chain's last reward.
- Miner/pool effort SQL guards `networkdifficulty = 0` with `NULLIF`.
- The test that needs no native library is no longer marked as needing one; `SchemaDriftTests` covers
  UPDATE statements as well as INSERT/COPY; a job-level test pins that share and hash difficulty are the same
  for a multiplier-1 and a 65536 coin (verified to fail if the multiplier leaks back in).
- The merged-mining example puts `shareRecoveryFile` at the top level, where it is read.

**Corrections to earlier entries.**
- Moving the admin state changes from GET to POST removes cacheable and prefetchable mutations. It is not
  CSRF protection: a cross-site form POST is a simple request. The protection is the admin IP whitelist
  (and the optional token); add an anti-forgery check if admin is ever exposed beyond localhost.
- The "all 15 paging actions" and "pool effort is `difficulty / networkdifficulty`" statements applied to
  the Bitcoin family only (see above).

**Known limits.** `DifficultyUnitsTests` and the job test cover the helper and the Bitcoin job; the
effort SQL, `ToBlock` and the backfill are verified by reading and by running the script's output through
review, not against a database. Native-library tests are skipped on machines without the libraries; CI
builds them, local green runs are weaker than they look.
## 2026-10-18 — Payout idempotency: a payout batch is recorded before coins leave the wallet

**Decision.** `PayoutHandlerBase` gains payout batches: `BeginPayoutBatchAsync` writes a
`payoutbatches` row (recipients as JSON, status `pending`) before the send and throws if it
cannot; `CompletePayoutBatchAsync` first marks the row `sent` with the transaction id, then
inserts payments, debits balances and marks it `completed` in one transaction;
`MarkPayoutBatchFailedAsync` records a daemon rejection. `RecoverPayoutBatchesAsync`, called by
`PayoutManager` before every payout run, finishes `sent` batches and refuses to pay while a
`pending` batch exists, with one admin notification per batch. Transport failures from
`RpcClient` (code -500: timeout, connection error) are treated as unknown outcome and leave the
batch pending; daemon-reported errors mark it failed. Operators resolve a pending batch through
`POST api/admin/pools/{id}/payoutbatches/{batchId}/resolve?outcome=sent&txid=…` or
`?outcome=notsent`. The Bitcoin-family, Equihash (both async-operation flows) and Handshake handlers use
the batches.

**Why.** A payout was `sendmany` followed by "insert payments, debit balances" as a second step
with no record in between. If the second step failed, or the process died between the two, the
coins were gone and the balances untouched, so the next run paid the same miners again. There
was also no way to tell afterwards whether a send had happened. The batch row closes both gaps
and fails closed: when the pool cannot know whether coins moved, it stops paying and says so,
rather than guessing either way. Finishing a `sent` batch before paying new balances is what
prevents the double payment; the operator step is the only part that cannot be automated
coin-agnostically (wallets differ in how to look a send up).

**Deployment guard.** `PayoutManager` checks once that `payoutbatches` exists before the first
payout; if it does not, it logs the migration to run at Fatal and skips payouts (balance updates
still run) instead of failing every run with a raw database error.

**Review round 2 changes (independent review of the first version).**
- Equihash's own `z_sendmany` and `sendcurrency` flows are wired in (the first version claimed
  Equihash was covered through inheritance; only pools with `useBitcoinPayoutHandler` were). A
  failed or cancelled async operation marks the batch failed; an unrecognised status, a missing
  operation id or a request that may have reached the wallet leaves it pending.
- Every status change is a compare-and-set (`UpdateStatusAsync(..., fromStatuses)`). Finishing a
  sent batch first moves it sent -> completed in the same transaction and writes nothing else if that
  moves no row, so a retried transaction whose acknowledgement was lost, a concurrent recovery or an
  operator resolving at the wrong moment can never insert payments or debit balances twice. The admin
  resolve endpoints only act on pending batches and answer 409 if the batch changed.
- `CompletePayoutBatchAsync` no longer throws: the coins have already left the wallet, so a persistence
  failure is logged (with recipients), notified to admins, and left to the recovery instead of being
  reported as a failed transfer. A recovered batch sends the payout-success notification.
- "Unknown outcome" is narrower and wider than "any -500": a refused connection or unknown host proves
  nothing was sent (not unknown); wallet verify codes -25/-26/-27, which old wallets can return after
  recording the transaction, are treated as unknown. Sends use a 5 minute timeout so a slow `sendmany`
  is not classed unknown at 30 s, and per-address mode starts no further sends after an unknown outcome.
- `BeginPayoutBatchAsync` does not retry: nothing is sent yet, and 8 backoff steps per recipient would
  stall payouts for hours during a database outage.
- The stuck-batch admin notification is deduplicated in a static set (handlers are transient, so the
  instance set notified every cycle). The table guard reports only a genuinely missing table, once.
- An empty transaction id is stored as `unknown` and excluded from explorer links.

**Known limits.** A daemon-reported error is taken to mean nothing was sent, apart from the verify codes
above. A lost commit acknowledgement on `Begin` can leave an orphan pending row; that halts the pool
until resolved (fail-closed). The compare-and-set and recovery paths are tested by reading and by unit
tests of the classifier, not against a real database or wallet.

**Not covered.** The other payout handlers (Ethereum, Kaspa, Cryptonote, Ergo, Alephium, Beam,
Conceal, Warthog, Xelis, Zano) still persist directly after sending; the base helpers are there
for them and the change per handler is the same shape as the Bitcoin one.

## 2026-10-18 — Review round 4 (migration branch): corrections and fixes

**Corrections to earlier entries.**
- *OdoCrypt vector.* `Odocrypt_Hash` pins this implementation's own output on an input with nTime 0, where the
  correct key formula and the old buggy one both give key 0; it is a regression pin, not independent evidence.
  The independent anchor is the DigiByte odo-miner reference vector (`25b32ac3...`, header byte i*7+3, key
  `0x683b9800`) used by `verify_odocrypt_so.c`; it is now also asserted from `dotnet test`
  (`Odocrypt_Native_Matches_The_DigiByte_Reference_Vector`, passes against the built library), and
  `Odocrypt_Key_Is_NTime_Rounded_Down_To_The_Interval` pins the key formula on realistic times (verified to
  fail with `nTime / interval`). The reference value's own provenance is a code comment, not a block.
- *AutoMapper entry.* `payoutSchemeConfig` in `/api/pools` was already read from the configured JSON by the
  controller before the migration, which overwrites the mapper's value, so that output did not change. Pool-info
  copies: only `Ports` was aliased before; `VarDiff` and `TcpProxyProtocol` are still shared references, and
  `Clone` is hand-maintained (a test now fails if `PoolEndpoint` gains a property it does not copy).
- *.NET 10 entry.* The statement that build and tests are identical on both frameworks was too strong. The
  suite has not been fully green anywhere that lacks the native libraries; with them built it passes.
- The scripts for Debian 11 and Ubuntu 20.04/21.04 are deleted and the README no longer points at them.

**Fixes.**
- `deploy-server.sh` refuses to continue if the running `miningcore` container's image has no .NET 10 runtime
  (a container stays pinned to the image it was created from, so restarting an old container with the new
  binaries fails at start-up). Recreate it from `miningcore:local` once.
- `build-ubuntu-22.04.sh` no longer adds a package feed that has no .NET 10; it installs the SDK with
  Microsoft's installer script (not run on a 22.04 machine from here).
- Mapperly's unmapped-target diagnostic (RMG020) is now an error; it is only a warning by default.
- `/api/pools` no longer returns 500 for a malformed template with no hasher (the algorithm name falls back to
  null, as AutoMapper's null propagation did); an out-of-range `mode` on the miner performance endpoint is a 400.
- CI actions bumped to checkout@v4 and setup-dotnet@v4.

**Known and accepted.** An empty-string block status in the database now throws in the mapper (it mapped to
the first enum value under AutoMapper); the column is NOT NULL and the code never writes an empty one.
`MiningcoreMapper` is registered by hand in `Program.cs` and the test module and no test boots the host.
The tracked `build/` directory still holds the old net8/AutoMapper binaries; do not deploy from it.

**Integration.** This line of work is based on an older `main`: `main` has seven newer commits (including the
duplicate-block orphaning in the payout path and the GBT-child merged-mining path). Merge `main` before deploying
from a branch; `main`'s GBT-child path calls the two-argument `AuxPowManager.ResolveTarget`, so it will not emit
the target warning until it uses the new overload.
