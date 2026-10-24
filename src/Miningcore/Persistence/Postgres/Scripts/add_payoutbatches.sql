-- Migration: payout batches (payout intents) for idempotent payouts.
--
-- Why: a payout used to be sendmany followed by "insert payments, debit balances" in a second
-- step. If that second step failed (database down, process killed) the coins were gone and the
-- balances untouched, so the next run paid the same miners again. A batch row is now written
-- before the send and completed after it; see docs/decisions.md (2026-10-18, Payout idempotency).
-- Run once against an existing database. IF NOT EXISTS makes it safe to re-run.

CREATE TABLE IF NOT EXISTS payoutbatches
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	status TEXT NOT NULL,
	recipients TEXT NOT NULL,
	transactionconfirmationdata TEXT NULL,
	error TEXT NULL,
	created TIMESTAMPTZ NOT NULL,
	updated TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS IDX_PAYOUTBATCHES_POOL_STATUS on payoutbatches(poolid, status);
