# SHA256 version-rolling fix for Miningcore

## Summary

Miningcore was rejecting some SHA256 shares after newer DigiByte-style work changed how the version bits were interpreted. The issue was caused by the pool sending a job version that still contained the rolling region, while some SHA256 firmware and ckpool-style frontends treat the AsicBoost/rolling region differently.

The fix changes Miningcore to:

- strip the rolling region from the job version sent to miners;
- keep share validation consistent by applying the rolling bits with masked-merge semantics during share processing; and
- preserve the existing block-candidate behavior for reserved bits and version-blocked bits.

## What changed

### 1. Job version sent to miners

Miningcore now derives a sanitized base version for the job parameters by clearing the configured rolling mask before sending the job to the miner.

This prevents miners that use the AsicBoost range (bits 13 to 28) from seeing a job version that still contains the pool's rolling bits.

### 2. Share validation

Share processing now builds the header version from the sanitized base version and then reapplies the miner-submitted rolling bits using the same mask.

That keeps the share validation path aligned with the job version that miners actually received, which avoids false rejects and restores rolling behavior for compatible SHA256 firmware.

### 3. Regression coverage

A regression test was added to cover the case where a job version contains the rolling region and the job params sent to miners must be stripped accordingly.

## Files changed

- src/Miningcore/Blockchain/Bitcoin/BitcoinJob.cs
- src/Miningcore.Tests/Blockchain/Bitcoin/BitcoinJobTests.cs

## Verification

The Bitcoin job regression tests were run successfully:

- dotnet test src/Miningcore.Tests/Miningcore.Tests.csproj --filter "FullyQualifiedName~Miningcore.Tests.Blockchain.Bitcoin.BitcoinJobTests"

Result: 7 passed, 0 failed.

## Notes

This fix is specifically aimed at the SHA256/ASICBOOST-style rolling case where bit 23 falls inside the rolling region and can break compatibility when the pool version and miner-submitted version bits are combined incorrectly.
