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
