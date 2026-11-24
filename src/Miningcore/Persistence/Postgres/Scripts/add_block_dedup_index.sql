-- Migration: unique index preventing a block from being recorded (and credited) twice.
--
-- Why: blocks had no unique constraint, only non-unique indexes. A retried commit whose
-- acknowledgement was lost (the COMMIT succeeded on the server but the client saw a
-- DbException and Polly retried), or replaying a recovery file more than once, could insert
-- the same block hash a second time. BitcoinPayoutHandler's only duplicate guard
-- (IsCompetingBlock) compares hashes and lets two rows with the SAME hash both through, so
-- both got confirmed and credited in full - the pool paying one block's reward twice.
-- Run once against an existing database. Safe to re-run (IF NOT EXISTS).

CREATE UNIQUE INDEX IF NOT EXISTS ux_blocks_pool_height_hash ON blocks(poolid, blockheight, hash) WHERE hash IS NOT NULL;
