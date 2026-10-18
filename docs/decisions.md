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
