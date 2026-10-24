-- Migration: add the hash-difficulty columns that BlockRepository and AuxBlockRepository write.
--
-- Why: commits f7aa692c and dbb50b63 added blocks.difficulty and auxblocks.difficulty /
-- auxblocks.networkdifficulty to the entities and INSERT statements but shipped no schema
-- change. On a database created from the scripts every block insert failed with
-- "column does not exist"; because the block insert shares a transaction with the share
-- batch in ShareRecorder, the whole batch failed and shares went to the recovery file.
-- Run once against an existing database. IF NOT EXISTS makes it safe to re-run.

ALTER TABLE blocks
    ADD COLUMN IF NOT EXISTS difficulty DOUBLE PRECISION NULL;

ALTER TABLE auxblocks
    ADD COLUMN IF NOT EXISTS difficulty DOUBLE PRECISION NULL;

ALTER TABLE auxblocks
    ADD COLUMN IF NOT EXISTS networkdifficulty DOUBLE PRECISION NULL;
