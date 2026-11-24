-- Adds a strictly-increasing id column to shares, used as a keyset-pagination tiebreaker
-- alongside `created` in ReadSharesBeforeAsync. Without it, share pages that are resumed from
-- the last row's `created` timestamp can silently skip rows sharing that exact timestamp with
-- the page boundary (shares are batch-inserted and timestamp resolution is not fine enough to
-- guarantee uniqueness under load), under-crediting miners in PPLNS/PPLNSBF/PROP payout runs.
-- Safe to re-run.

ALTER TABLE shares ADD COLUMN IF NOT EXISTS id BIGSERIAL;

CREATE INDEX IF NOT EXISTS IDX_SHARES_POOL_CREATED_ID ON shares(poolid, created DESC, id DESC);
