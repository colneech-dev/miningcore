-- Migration: add auxblocks table for tracking AuxPoW (merged mining) block submissions.
-- Run once against an existing database. The IF NOT EXISTS guards make it safe to re-run.
-- Confirmation status updates (pending → confirmed/orphaned) are deferred to a future
-- AuxBlockConfirmer service; rows remain 'pending' until then.

CREATE TABLE IF NOT EXISTS auxblocks
(
    id                      BIGSERIAL       NOT NULL PRIMARY KEY,
    poolid                  TEXT            NOT NULL,
    chainid                 TEXT            NOT NULL,
    chainname               TEXT            NULL,
    blockheight             BIGINT          NULL,
    auxblockhash            TEXT            NOT NULL,
    parentblockhash         TEXT            NULL,
    status                  TEXT            NOT NULL DEFAULT 'pending',
    confirmationprogress    FLOAT           NOT NULL DEFAULT 0,
    reward                  DECIMAL(28,12)  NULL,
    miner                   TEXT            NULL,
    worker                  TEXT            NULL,
    source                  TEXT            NULL,
    submittedvia            TEXT            NULL,
    created                 TIMESTAMPTZ     NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_auxblocks_pool_chain_hash
    ON auxblocks(poolid, chainid, auxblockhash);

CREATE INDEX IF NOT EXISTS idx_auxblocks_pool_created
    ON auxblocks(poolid, created DESC);

CREATE INDEX IF NOT EXISTS idx_auxblocks_miner
    ON auxblocks(poolid, miner);

CREATE INDEX IF NOT EXISTS idx_auxblocks_status
    ON auxblocks(poolid, status);
