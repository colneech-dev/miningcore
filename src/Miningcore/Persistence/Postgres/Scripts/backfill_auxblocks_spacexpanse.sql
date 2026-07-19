-- One-time backfill of 3 SpaceXpanse aux blocks found before the auxblocks table existed.
-- Worker attribution is not recoverable; a representative worker is assigned per pool.
-- Run AFTER add_auxblocks.sql. The unique index makes this script idempotent.

INSERT INTO auxblocks(poolid, chainid, chainname, blockheight, auxblockhash, parentblockhash,
    status, confirmationprogress, reward, miner, worker, source, submittedvia, created)
VALUES
    ('gvia-solo', 'spacexpanse', 'SpaceXpanse', NULL,
     '6f9c7b4548823282f407fbe0e6d241d8345a11f51434e24a8c679a319a75d01a', NULL,
     'confirmed', 1.0, 100.0,
     'gvia1qt2c3k3tm926lhfqxkvul4lxp0s9x4zf2prtzy2', 'NerdQAxe01',
     NULL, 'backfill', '2026-05-19 07:02:24+00'),

    ('dgb-solo', 'spacexpanse', 'SpaceXpanse', NULL,
     '71adb2b474aca0816cd7bf0e5af46f28ac9e846909a78caf36ea49bb8f6366ec', NULL,
     'confirmed', 1.0, 100.0,
     'DTGwfAPbxQaKViGpoy8XfVguMPj5sGxTdS', 'NerdOctAxe01',
     NULL, 'backfill', '2026-05-20 06:05:14+00'),

    ('dgb-solo', 'spacexpanse', 'SpaceXpanse', NULL,
     '81e77ad3df4f895fbc00590a830b1adff6f4b0286407821c78df4c0e20e40596', NULL,
     'confirmed', 1.0, 100.0,
     'DTGwfAPbxQaKViGpoy8XfVguMPj5sGxTdS', 'NerdOctAxe01',
     NULL, 'backfill', '2026-05-20 23:06:32+00')

ON CONFLICT (poolid, chainid, auxblockhash) DO NOTHING;
