-- Migration: add worker column to blocks and connectedworkers column to poolstats
-- Run once against existing databases created before this change.

ALTER TABLE blocks
    ADD COLUMN IF NOT EXISTS worker TEXT NULL;

ALTER TABLE poolstats
    ADD COLUMN IF NOT EXISTS connectedworkers INT NOT NULL DEFAULT 0;
