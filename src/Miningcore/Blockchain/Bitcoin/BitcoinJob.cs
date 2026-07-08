using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Crypto;
using Miningcore.Extensions;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;
using Miningcore.Blockchain.Bitcoin.AuxPoW;
using Contract = Miningcore.Contracts.Contract;
using NLog;
using Transaction = NBitcoin.Transaction;
using System.Numerics;

namespace Miningcore.Blockchain.Bitcoin;

public class BitcoinJob
{
    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
    protected IHashAlgorithm blockHasher;
    protected IMasterClock clock;
    protected IHashAlgorithm coinbaseHasher;
    protected double shareMultiplier;
    protected int extraNoncePlaceHolderLength;
    protected IHashAlgorithm headerHasher;
    protected bool isPoS;
    protected string txComment;
    protected PayeeBlockTemplateExtra payeeParameters;

    protected Network network;
    protected IDestination poolAddressDestination;
    protected BitcoinTemplate coin;
    protected BitcoinPoolConfigExtra extraPoolConfig;
    protected AuxBlockData[] auxBlocks;    // standard aux chain work for merge mining (multi-chain tree)
    protected AuxBlockData rskAuxBlock;  // RSK standalone commitment (separate from aux tree)
    protected AuxBlockData hathorAuxBlock;  // Hathor standalone commitment (separate from aux tree)
    private BitcoinTemplate.BitcoinNetworkParams networkParams;
    protected readonly ConcurrentDictionary<string, bool> submissions = new(StringComparer.OrdinalIgnoreCase);
    protected uint256 blockTargetValue;
    protected byte[] coinbaseFinal;
    protected string coinbaseFinalHex;
    protected byte[] coinbaseInitial;
    protected string coinbaseInitialHex;
    protected string[] merkleBranchesHex;
    protected MerkleTree mt;
    protected string[] merkleSegwitBranchesHex;
    protected MerkleTree mtSegwit;
	
    ///////////////////////////////////////////
    // GetJobParams related properties

    protected object[] jobParams;
    protected string previousBlockHashReversedHex;
    protected Money rewardToPool;
    protected Transaction txOut;

    // serialization constants
    protected byte[] scriptSigFinalBytes;

    // Aux chain merkle tree (built once per job from active aux blocks)
    private byte[][] auxMerkleNodes;
    private int auxMerkleTreeSize = 1;
    private uint auxMerkleTreeNonce = 0;

    protected static byte[] sha256Empty = new byte[32];
    protected uint txVersion = 1u; // transaction version (currently 1) - see https://en.bitcoin.it/wiki/Transaction
    private const double MinimumShareDifficultyRatio = 0.97d;

    protected static uint txInputCount = 1u;
    protected static uint txInPrevOutIndex = (uint) (Math.Pow(2, 32) - 1);
    protected static uint txInSequence;
    protected static uint txLockTime;

    protected virtual void BuildMerkleBranches()
    {
        var transactionHashes = BlockTemplate.Transactions
            .Select(tx => (tx.TxId ?? tx.Hash)
                .HexToByteArray()
                .ReverseInPlace())
            .ToArray();

        mt = new MerkleTree(transactionHashes);

        merkleBranchesHex = mt.Steps
            .Select(x => x.ToHexString())
            .ToArray();
    }

    private static byte[] Sha256Double(byte[] input)
    {
        using (var sha256 = SHA256.Create())
        {
            byte[] hash1 = sha256.ComputeHash(input);
            byte[] hash2 = sha256.ComputeHash(hash1);
            return hash2;
        }
    }

    private MerkleTree BuildSegwitMerkleBranches()
    {
        var segwitTransactionHashes = BlockTemplate.Transactions
            .Where(tx => IsSegWitTransaction(tx))
            .Select(tx => (tx.TxId ?? tx.Hash)
                .HexToByteArray()
                .ReverseInPlace())
            .ToArray();

        // Build Merkle Tree with SegWit transactions
        return new MerkleTree(segwitTransactionHashes);
    }

    private bool IsSegWitTransaction(BitcoinBlockTransaction tx)
    {
        // Convert hex string to byte array
        byte[] txBytes = HexStringToByteArray(tx.Data);

        // Convert byte array to hex string
        string hexString = ByteArrayToHexString(txBytes);

        // Parse the transaction using NBitcoin
        var transaction = Transaction.Parse(hexString, network);

        return transaction.HasWitness;
    }

    private byte[] HexStringToByteArray(string hex)
    {
        int length = hex.Length;
        byte[] bytes = new byte[length / 2];
        for (int i = 0; i < length; i += 2)
        {
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }
        return bytes;
    }

    private string ByteArrayToHexString(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", string.Empty);
    }

    protected virtual void BuildCoinbase()
    {
        // Build aux chain merkle tree before GenerateScriptSigInitial so the
        // commitment bytes are available when the script is assembled.
        if(auxBlocks != null)
        {
            var activeAux = auxBlocks
                .Where(a => a != null && !string.IsNullOrEmpty(a.Hash))
                .ToList();

            if(activeAux.Count > 0)
            {
                var (nodes, treeSize, nonce) = AuxPowSerializer.BuildAuxTree(activeAux);
                auxMerkleNodes = nodes;
                auxMerkleTreeSize = treeSize;
                auxMerkleTreeNonce = nonce;
            }
        }

        // generate script parts
        var sigScriptInitial = GenerateScriptSigInitial();
        var sigScriptInitialBytes = sigScriptInitial.ToBytes();

        var sigScriptLength = (uint) (
            sigScriptInitial.Length +
            extraNoncePlaceHolderLength +
            scriptSigFinalBytes.Length);

        // output transaction
        txOut = CreateOutputTransaction();

        // build coinbase initial
        using(var stream = new MemoryStream())
        {
            var bs = new BitcoinStream(stream, true);

            // version
            bs.ReadWrite(ref txVersion);

            // timestamp for POS coins
            if(isPoS)
            {
                var timestamp = BlockTemplate.CurTime;
                bs.ReadWrite(ref timestamp);
            }

            // serialize (simulated) input transaction
            bs.ReadWriteAsVarInt(ref txInputCount);
            bs.ReadWrite(sha256Empty);
            bs.ReadWrite(ref txInPrevOutIndex);

            // signature script initial part
            bs.ReadWriteAsVarInt(ref sigScriptLength);
            bs.ReadWrite(sigScriptInitialBytes);

            // done
            coinbaseInitial = stream.ToArray();
            coinbaseInitialHex = coinbaseInitial.ToHexString();
        }

        // build coinbase final
        using(var stream = new MemoryStream())
        {
            var bs = new BitcoinStream(stream, true);

            // signature script final part
            bs.ReadWrite(scriptSigFinalBytes);

            // tx in sequence
            bs.ReadWrite(ref txInSequence);

            // serialize output transaction
            var txOutBytes = SerializeOutputTransaction(txOut);
            bs.ReadWrite(txOutBytes);

            // misc
            bs.ReadWrite(ref txLockTime);

            // Extension point
            AppendCoinbaseFinal(bs);

            // done
            coinbaseFinal = stream.ToArray();
            coinbaseFinalHex = coinbaseFinal.ToHexString();
        }
    }

    protected virtual void AppendCoinbaseFinal(BitcoinStream bs)
    {
        if(!string.IsNullOrEmpty(txComment))
        {
            var data = Encoding.ASCII.GetBytes(txComment);
            bs.ReadWriteAsVarString(ref data);
        }

        if(coin.HasMasterNodes && !string.IsNullOrEmpty(masterNodeParameters.CoinbasePayload))
        {
            var data = masterNodeParameters.CoinbasePayload.HexToByteArray();
            bs.ReadWriteAsVarString(ref data);
        }
    }

    protected virtual byte[] SerializeOutputTransaction(Transaction tx)
    {
        var withDefaultWitnessCommitment = !string.IsNullOrEmpty(BlockTemplate.DefaultWitnessCommitment);

        var outputCount = (uint) tx.Outputs.Count;
        if(withDefaultWitnessCommitment)
            outputCount++;

        using(var stream = new MemoryStream())
        {
            var bs = new BitcoinStream(stream, true);

            // write output count
            bs.ReadWriteAsVarInt(ref outputCount);

            long amount;
            byte[] raw;
            uint rawLength;

            // serialize witness (segwit)
            if(withDefaultWitnessCommitment)
            {
                amount = 0;
                raw = BlockTemplate.DefaultWitnessCommitment.HexToByteArray();
                rawLength = (uint) raw.Length;

                if (coin.Symbol == "RVH" || coin.Symbol == "ANOK")
                {
                    // Compute witness commitment
                    // DefaultWitnessCommitment is a 38-byte script: OP_RETURN(1) + push36(1) + magic(4) + witnessRootHash(32)
                    // Extract the 32-byte witness root hash starting at byte offset 6
                    var commitmentScript = BlockTemplate.DefaultWitnessCommitment.HexToByteArray();
                    byte[] witnessRoot = commitmentScript.Skip(6).Take(32).ToArray();
                    byte[] witnessNonce = new byte[32];

                    // Build Merkle Tree
                    var mtSegwit = BuildSegwitMerkleBranches();
                    var merkleRoot = mtSegwit.WithFirst(new byte[32]);

                    // Concatenate witness root and nonce
                    byte[] witnessRootAndNonce = new byte[witnessRoot.Length + witnessNonce.Length];
                    Buffer.BlockCopy(witnessRoot, 0, witnessRootAndNonce, 0, witnessRoot.Length);
                    Buffer.BlockCopy(witnessNonce, 0, witnessRootAndNonce, witnessRoot.Length, witnessNonce.Length);

                    // Generate SHA256^2 hash
                    byte[] hash = Sha256Double(witnessRootAndNonce);

                    // Create scriptPubKey
                    byte[] magic = new byte[] { 0xaa, 0x21, 0xa9, 0xed };
                    byte[] scriptPubKey = new byte[36];
                    Buffer.BlockCopy(magic, 0, scriptPubKey, 0, magic.Length);
                    Buffer.BlockCopy(hash, 0, scriptPubKey, magic.Length, hash.Length);

                    raw = scriptPubKey;
                    rawLength = (uint)raw.Length;
                }
				
                bs.ReadWrite(ref amount);
                bs.ReadWriteAsVarInt(ref rawLength);
                bs.ReadWrite(raw);
            }

            // serialize outputs
            foreach(var output in tx.Outputs)
            {
                amount = output.Value.Satoshi;
                var outScript = output.ScriptPubKey;
                raw = outScript.ToBytes(true);
                rawLength = (uint) raw.Length;

                bs.ReadWrite(ref amount);
                bs.ReadWriteAsVarInt(ref rawLength);
                bs.ReadWrite(raw);
            }

            return stream.ToArray();
        }
    }

    protected virtual Script GenerateScriptSigInitial()
    {
        var now = ((DateTimeOffset) clock.Now).ToUnixTimeSeconds();

        // script ops
        var ops = new List<Op>();

        // push block height
        ops.Add(Op.GetPushOp(BlockTemplate.Height));

        // optionally push aux-flags
        if(!coin.CoinbaseIgnoreAuxFlags && !string.IsNullOrEmpty(BlockTemplate.CoinbaseAux?.Flags))
            ops.Add(Op.GetPushOp(BlockTemplate.CoinbaseAux.Flags.HexToByteArray()));

        // push timestamp
        ops.Add(Op.GetPushOp(now));

        // push placeholder
        ops.Add(Op.GetPushOp(0));

        // Embed the aux chain merkle tree commitment built in BuildCoinbase().
        // Format: 0xfabe6d6d + merkleRoot(32) + treeSize(4LE) + nonce(4LE) = 44 bytes
        // The root is stored internally in LE byte order (nodes[1]). The aux daemon
        // (Namecoin auxpow.cpp::CAuxPow::check) reverses its locally-reconstructed root
        // before searching the coinbase scriptSig, so we must embed the root in
        // display/BE order (reversed from the internal tree representation).
        if(auxMerkleNodes != null)
        {
            var rootDisplayOrder = auxMerkleNodes[1].Reverse().ToArray();
            var commitment = AuxPowSerializer.BuildCoinbaseCommitment(rootDisplayOrder, auxMerkleTreeSize, auxMerkleTreeNonce);
            ops.Add(Op.GetPushOp(commitment));
        }

        // RSK commitment is placed in a coinbase OP_RETURN output (see CreateOutputTransaction),
        // not in the scriptSig, so it does not consume any of the coinbase scriptSig byte budget.

        return new Script(ops);
    }

    protected virtual Transaction CreateOutputTransaction()
    {
        rewardToPool = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);
        var tx = Transaction.Create(network);

        if(coin.HasPayee)
            rewardToPool = CreatePayeeOutput(tx, rewardToPool);

        if(coin.HasMasterNodes)
            rewardToPool = CreateMasternodeOutputs(tx, rewardToPool);

        if (coin.HasFounderFee)
            rewardToPool = CreateFounderOutputs(tx, rewardToPool);

        if (coin.HasFundReward)
            rewardToPool = CreateFundRewardOutputs(tx, rewardToPool);

        if(coin.HasFortuneReward)
            rewardToPool = CreateFortuneOutputs(tx, rewardToPool);

        if (coin.HasMinerFund)
            rewardToPool = CreateMinerFundOutputs(tx, rewardToPool);

        if(coin.HasCommunityAddress)
            rewardToPool = CreateCommunityAddressOutputs(tx, rewardToPool);

        if(coin.HasCoinbaseDevReward)
            rewardToPool = CreateCoinbaseDevRewardOutputs(tx, rewardToPool);

        if(coin.HasCoinbaseStakingReward)
            rewardToPool = CreateCoinbaseStakingRewardOutputs(tx, rewardToPool);

        if(coin.HasCommunity)
            rewardToPool = CreateCommunityOutputs(tx, rewardToPool);

        if(coin.HasDataMining)
            rewardToPool = CreateDataMiningOutputs(tx, rewardToPool);

        if(coin.HasDeveloper)
            rewardToPool = CreateDeveloperOutputs(tx, rewardToPool);

        if(coin.HasFoundation)
            rewardToPool = CreateFoundationOutputs(tx, rewardToPool);

        if(coin.HasGovernanceAddress)
            rewardToPool = CreateGovernanceAddressOutputs(tx, rewardToPool);

        // Remaining amount goes to pool
        tx.Outputs.Add(rewardToPool, poolAddressDestination);

        // RSK merge mining: append OP_RETURN output with RSKBLOCK: commitment.
        // RSKj searches the full serialised coinbase transaction for this pattern,
        // so placing it in an output keeps the coinbase scriptSig length unchanged
        // and works on coins with strict 100-byte scriptSig limits (e.g. GateviaViacoin).
        if(rskAuxBlock?.Hash != null)
        {
            var rskData = new byte[9 + 32]; // "RSKBLOCK:" (9) + hash (32)
            Encoding.ASCII.GetBytes("RSKBLOCK:").CopyTo(rskData, 0);
            // Hash is big-endian as returned by mnr_getWork and stored in AuxBlockData.Hash.
            // RSKj searches the raw coinbase bytes for "RSKBLOCK:" followed by that same big-endian hash — no reversal.
            rskAuxBlock.Hash.HexToByteArray().CopyTo(rskData, 9);
            tx.Outputs.Add(Money.Zero, new Script(OpcodeType.OP_RETURN, Op.GetPushOp(rskData)));
        }

        // Hathor merge mining: append OP_RETURN output with "Hath" + mining base hash.
        // Hathor requires the magic to IMMEDIATELY precede the hash and to not occur
        // earlier in the serialized coinbase (verify_magic_number). Placing it in the
        // LAST output keeps the maximum amount of coinbase bytes ahead of it, and an
        // OP_RETURN output consumes no scriptSig budget (same rationale as RSK above).
        if(hathorAuxBlock?.Hash != null)
        {
            var hathorData = new byte[4 + 32]; // "Hath" (4) + mining base hash (32)
            Hathor.HathorSerializer.MagicNumber.CopyTo(hathorData, 0);
            // Raw sha256d bytes as computed from the template — committed verbatim, no reversal.
            hathorAuxBlock.Hash.HexToByteArray().CopyTo(hathorData, 4);
            tx.Outputs.Add(Money.Zero, new Script(OpcodeType.OP_RETURN, Op.GetPushOp(hathorData)));
        }

        return tx;
    }

    protected virtual Money CreatePayeeOutput(Transaction tx, Money reward)
    {
        if(payeeParameters?.PayeeAmount != null && payeeParameters.PayeeAmount.Value > 0)
        {
            var payeeReward = new Money(payeeParameters.PayeeAmount.Value, MoneyUnit.Satoshi);
            reward -= payeeReward;

            tx.Outputs.Add(payeeReward, BitcoinUtils.AddressToDestination(payeeParameters.Payee, network));
        }

        return reward;
    }

    protected bool RegisterSubmit(string extraNonce1, string extraNonce2, string nTime, string nonce, string versionBits = null)
    {
        var key = new StringBuilder()
            .Append(extraNonce1)
            .Append(extraNonce2)
            .Append(nTime)
            .Append(nonce)
            .Append(versionBits ?? string.Empty)
            .ToString();

        return submissions.TryAdd(key, true);
    }

    private uint GetVersionRollingMask(uint? versionMask = null)
    {
        if(versionMask.HasValue)
            return versionMask.Value;

        if(extraPoolConfig?.VersionRollingMask != null)
            return uint.Parse(extraPoolConfig.VersionRollingMask, NumberStyles.HexNumber);

        return BitcoinConstants.VersionRollingPoolMask;
    }

    private uint GetBaseVersion(uint? versionMask = null)
    {
        var version = BlockTemplate.Version;

        if(extraPoolConfig?.EnableVersionRolling != false)
            version &= ~GetVersionRollingMask(versionMask);

        return version;
    }

    protected byte[] SerializeHeader(Span<byte> coinbaseHash, uint nTime, uint nonce, uint? versionMask, uint? versionBits)
    {
        // build merkle-root
        var merkleRoot = mt.WithFirst(coinbaseHash.ToArray());

        // Build version from the stripped job version and apply miner-submitted rolling bits
        // using masked-merge semantics. This keeps shares consistent with the job version sent
        // to miners and avoids collisions when the rolling region overlaps an AsicBoost bit.
        var version = GetBaseVersion(versionMask);

        if(versionBits.HasValue && versionBits.Value != 0)
        {
            var mask = versionMask ?? GetVersionRollingMask();
            version = (version & ~mask) | (versionBits.Value & mask);
        }

#pragma warning disable 618
        var blockHeader = new BlockHeader
#pragma warning restore 618
        {
            Version = unchecked((int) version),
            Bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)),
            HashPrevBlock = uint256.Parse(BlockTemplate.PreviousBlockhash),
            HashMerkleRoot = new uint256(merkleRoot),
            BlockTime = DateTimeOffset.FromUnixTimeSeconds(nTime),
            Nonce = nonce
        };

            return blockHeader.ToBytes();
    }

    private static bool IsShareDifficultyAcceptable(double shareDiff, double requiredDifficulty)
    {
        // Some miners and daemons can report work that is slightly under the pool's exact target
        // due to client/daemon differences or timing. A small tolerance avoids false low-difficulty
        // rejects while still keeping the pool strict enough to reject clearly weak shares.
        return shareDiff >= requiredDifficulty * MinimumShareDifficultyRatio;
    }

    protected virtual (Share Share, string BlockHex, List<(AuxBlockData AuxBlock, byte[] HeaderBytes, byte[] Coinbase)> AuxCandidates) ProcessShareInternal(
        StratumConnection worker, string extraNonce2, uint nTime, uint nonce, uint? versionBits)
    {
        var context = worker.ContextAs<BitcoinWorkerContext>();
        var extraNonce1 = context.ExtraNonce1;

        // build coinbase
        var coinbase = SerializeCoinbase(extraNonce1, extraNonce2);
        Span<byte> coinbaseHash = stackalloc byte[32];
        coinbaseHasher.Digest(coinbase, coinbaseHash);

        // hash block-header
        var headerBytes = SerializeHeader(coinbaseHash, nTime, nonce, context.VersionRollingMask, versionBits);
        Span<byte> headerHash = stackalloc byte[32];
        headerHasher.Digest(headerBytes, headerHash, (ulong) nTime, BlockTemplate, coin, networkParams);
        var headerValue = new uint256(headerHash);

        // calc share-diff
        var diff1 = coin.Diff1 != null ? BigInteger.Parse(coin.Diff1, NumberStyles.HexNumber) : BitcoinConstants.Diff1; 
		var shareDiff = (double) new BigRational(diff1, headerHash.ToBigInteger()) * shareMultiplier;
        var stratumDifficulty = context.Difficulty;
        var ratio = shareDiff / stratumDifficulty;

        // check if the share meets the much harder block difficulty (block candidate)
        var isBlockCandidate = headerValue <= blockTargetValue;

        // test if share meets at least workers current difficulty
        if(!isBlockCandidate && !IsShareDifficultyAcceptable(shareDiff, stratumDifficulty))
        {
            // check if share matched the previous difficulty from before a vardiff retarget
            if(context.VarDiff?.LastUpdate != null && context.PreviousDifficulty.HasValue)
            {
                ratio = shareDiff / context.PreviousDifficulty.Value;

                if(!IsShareDifficultyAcceptable(shareDiff, context.PreviousDifficulty.Value))
                    throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");

                // use previous difficulty
                stratumDifficulty = context.PreviousDifficulty.Value;
            }

            else
                throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
        }

        var result = new Share
        {
            BlockHeight = BlockTemplate.Height,
            NetworkDifficulty = Difficulty,
            Difficulty = stratumDifficulty / shareMultiplier,
            HashDifficulty = shareDiff,
        };

        // Check aux chain targets for merged mining
        List<(AuxBlockData AuxBlock, byte[] HeaderBytes, byte[] Coinbase)> auxCandidates = null;
        if(auxBlocks != null)
        {
            foreach(var aux in auxBlocks)
            {
                if(aux?.TargetValue != null && headerValue <= aux.TargetValue)
                {
                    auxCandidates ??= new();
                    auxCandidates.Add((aux, headerBytes.ToArray(), coinbase));
                    logger.Info(() => "[" + worker.ConnectionId + "] Merged mining candidate: meets aux target for " + (aux.Hash.Length > 16 ? aux.Hash[..16] : aux.Hash) + "...");
                }
            }
        }

        // Check RSK target (standalone coinbase commitment, separate from aux tree)
        if(rskAuxBlock?.TargetValue != null && headerValue <= rskAuxBlock.TargetValue)
        {
            auxCandidates ??= new();
            auxCandidates.Add((rskAuxBlock, headerBytes.ToArray(), coinbase));
            logger.Info(() => $"[{worker.ConnectionId}] RSK merged mining candidate: hash={rskAuxBlock.Hash[..Math.Min(16, rskAuxBlock.Hash.Length)]}...");
        }

        // Check Hathor target (standalone coinbase commitment, separate from aux tree)
        if(hathorAuxBlock?.TargetValue != null && headerValue <= hathorAuxBlock.TargetValue)
        {
            auxCandidates ??= new();
            auxCandidates.Add((hathorAuxBlock, headerBytes.ToArray(), coinbase));
            logger.Info(() => $"[{worker.ConnectionId}] Hathor merged mining candidate: baseHash={hathorAuxBlock.Hash[..Math.Min(16, hathorAuxBlock.Hash.Length)]}...");
        }

        if(isBlockCandidate)
        {
            result.IsBlockCandidate = true;

            Span<byte> blockHash = stackalloc byte[32];
            blockHasher.Digest(headerBytes, blockHash, nTime);
            result.BlockHash = blockHash.ToHexString();

            var blockBytes = SerializeBlock(headerBytes, coinbase);
            var blockHex = blockBytes.ToHexString();

            return (result, blockHex, auxCandidates);
        }

        return (result, null, auxCandidates);
    }

    protected virtual byte[] SerializeCoinbase(string extraNonce1, string extraNonce2)
    {
        var extraNonce1Bytes = extraNonce1.HexToByteArray();
        var extraNonce2Bytes = extraNonce2.HexToByteArray();

        using(var stream = new MemoryStream())
        {
            stream.Write(coinbaseInitial);
            stream.Write(extraNonce1Bytes);
            stream.Write(extraNonce2Bytes);
            stream.Write(coinbaseFinal);

            return stream.ToArray();
        }
    }

    protected virtual byte[] SerializeBlock(byte[] header, byte[] coinbase)
    {
        var rawTransactionBuffer = BuildRawTransactionBuffer();
        var transactionCount = (uint) BlockTemplate.Transactions.Length + 1; // +1 for prepended coinbase tx

        using(var stream = new MemoryStream())
        {
            var bs = new BitcoinStream(stream, true);

            bs.ReadWrite(header);
            bs.ReadWriteAsVarInt(ref transactionCount);

            bs.ReadWrite(coinbase);
            bs.ReadWrite(rawTransactionBuffer);

            // POS coins require a zero byte appended to block which the daemon replaces with the signature
            if(isPoS)
                bs.ReadWrite((byte) 0);

            // if pool supports MWEB, we have to append the MWEB data to the block
            // https://github.com/litecoin-project/litecoin/blob/0.21/doc/mweb/mining-changes.md
            if(coin.HasMWEB)
            {
                var separator = new byte[] { 0x01 };
                var mweb = BlockTemplate.Extra.SafeExtensionDataAs<MwebBlockTemplateExtra>();
                if (mweb != null && mweb.Mweb != null) {
                    var mwebRaw = mweb.Mweb.HexToByteArray();
                    bs.ReadWrite(separator);
                    bs.ReadWrite(mwebRaw);
                }
            }

            return stream.ToArray();
        }
    }

    protected virtual byte[] BuildRawTransactionBuffer()
    {
        using(var stream = new MemoryStream())
        {
            foreach(var tx in BlockTemplate.Transactions)
            {
                var txRaw = tx.Data.HexToByteArray();
                stream.Write(txRaw);
            }

            return stream.ToArray();
        }
    }

    #region Masternodes

    protected MasterNodeBlockTemplateExtra masterNodeParameters;

    protected virtual Money CreateMasternodeOutputs(Transaction tx, Money reward)
    {
        if(masterNodeParameters.Masternode != null)
        {
            Masternode[] masternodes;

            // Dash v13 Multi-Master-Nodes
            if(masterNodeParameters.Masternode.Type == JTokenType.Array)
                masternodes = masterNodeParameters.Masternode.ToObject<Masternode[]>();
            else
                masternodes = new[] { masterNodeParameters.Masternode.ToObject<Masternode>() };

            if(masternodes != null)
            {
                foreach(var masterNode in masternodes)
                {
                    if(!string.IsNullOrEmpty(masterNode.Payee))
                    {
                        var payeeDestination = BitcoinUtils.AddressToDestination(masterNode.Payee, network);
                        var payeeReward = masterNode.Amount;

                        tx.Outputs.Add(payeeReward, payeeDestination);
                        reward -= payeeReward;
                    }
                }
            }
        }

        if(masterNodeParameters.SuperBlocks is { Length: > 0 })
        {
            foreach(var superBlock in masterNodeParameters.SuperBlocks)
            {
                var payeeAddress = BitcoinUtils.AddressToDestination(superBlock.Payee, network);
                var payeeReward = superBlock.Amount;

                tx.Outputs.Add(payeeReward, payeeAddress);
                reward -= payeeReward;
            }
        }

        if(!coin.HasPayee && !string.IsNullOrEmpty(masterNodeParameters.Payee))
        {
            var payeeAddress = BitcoinUtils.AddressToDestination(masterNodeParameters.Payee, network);
            var payeeReward = masterNodeParameters.PayeeAmount;

            tx.Outputs.Add(payeeReward, payeeAddress);
            reward -= payeeReward;
        }

        return reward;
    }

    #endregion // Masternodes

    #region Fortune

    protected FortuneBlockTemplateExtra fortuneParameters;

    protected virtual Money CreateFortuneOutputs(Transaction tx, Money reward)
    {
        if(fortuneParameters.Fortune != null)
        {
            Fortune[] fortunes;
            if(fortuneParameters.Fortune.Type == JTokenType.Array)
                fortunes = fortuneParameters.Fortune.ToObject<Fortune[]>();
            else
                fortunes = new[] { fortuneParameters.Fortune.ToObject<Fortune>() };

            if(fortunes != null)
            {
                foreach(var Fortune in fortunes)
                {
                    if(!string.IsNullOrEmpty(Fortune.Payee))
                    {
                        var payeeAddress = BitcoinUtils.AddressToDestination(Fortune.Payee, network);
                        var payeeReward = Fortune.Amount;

                        tx.Outputs.Add(payeeReward, payeeAddress);
                        reward -= payeeReward;
                    }
                }
            }
        }

        return reward;
    }

    #endregion // Fortune

    #region Founder

    protected FounderBlockTemplateExtra founderParameters;

    protected virtual Money CreateFounderOutputs(Transaction tx, Money reward)
    {
        if(founderParameters == null)
            return reward;

        var founderOutputAdded = false;

        if(founderParameters.Founder != null && founderParameters.Founder.Type != JTokenType.Null)
        {
            Founder[] founders;
            if(founderParameters.Founder.Type == JTokenType.Array)
                founders = founderParameters.Founder.ToObject<Founder[]>();
            else
                founders = new[] { founderParameters.Founder.ToObject<Founder>() };

            if(founders != null)
            {
                foreach(var founder in founders)
                {
                    if(founder != null && !string.IsNullOrEmpty(founder.Payee))
                    {
                        var payeeAddress = BitcoinUtils.AddressToDestination(founder.Payee, network);
                        var payeeReward = founder.Amount;

                        tx.Outputs.Add(payeeReward, payeeAddress);
                        reward -= payeeReward;
                        founderOutputAdded = true;
                    }
                }
            }
        }

        // Separate check so FounderReward is used even when Founder was present but empty (fxtc-style)
        if(!founderOutputAdded && founderParameters.FounderReward != null && !string.IsNullOrEmpty(founderParameters.FounderReward.Founderpayee))
        {
            var payeeAddress = BitcoinUtils.AddressToDestination(founderParameters.FounderReward.Founderpayee, network);
            var payeeReward = founderParameters.FounderReward.Amount;

            tx.Outputs.Add(payeeReward, payeeAddress);
            reward -= payeeReward;
        }

        return reward;
    }

    #endregion // Founder

    #region FundReward

    protected FundRewardBlockTemplateExtra fundRewardParameters;

    protected virtual Money CreateFundRewardOutputs(Transaction tx, Money reward)
    {
        if (fundRewardParameters.FundReward != null)
        {
            FundReward[] fundRewards;
            if (fundRewardParameters.FundReward.Type == JTokenType.Array)
                fundRewards = fundRewardParameters.FundReward.ToObject<FundReward[]>();
            else
                fundRewards = new[] { fundRewardParameters.FundReward.ToObject<FundReward>() };

            if(fundRewards != null)
            {
                foreach(var FundReward in fundRewards)
                {
                    if(!string.IsNullOrEmpty(FundReward.Payee))
                    {
                        Script payeeAddress = new Script(FundReward.Script.HexToByteArray());
                        var payeeReward = FundReward.Amount;

                        tx.Outputs.Add(payeeReward, payeeAddress);
                        reward -= payeeReward;
                    }
                }
            }
        }

        return reward;
    }

    #endregion // FundReward

    #region Minerfund

    protected MinerFundTemplateExtra minerFundParameters;

    protected virtual Money CreateMinerFundOutputs(Transaction tx, Money reward)
    {
        var payeeReward = minerFundParameters.MinimumValue;

        if (!string.IsNullOrEmpty(minerFundParameters.Addresses?.FirstOrDefault()))
        {
            var payeeAddress = BitcoinUtils.AddressToDestination(minerFundParameters.Addresses[0], network);
            tx.Outputs.Add(payeeReward, payeeAddress);
            reward -= payeeReward;
        }

        return reward;
    }

    #endregion // Founder

    #region CommunityAddress

    protected virtual Money CreateCommunityAddressOutputs(Transaction tx, Money reward)
    {
        if(BlockTemplate.CommunityAutonomousValue > 0)
        {
            var payeeReward = new Money(BlockTemplate.CommunityAutonomousValue, MoneyUnit.Satoshi);
            var payeeAddress = BitcoinUtils.AddressToDestination(BlockTemplate.CommunityAutonomousAddress, network);
            tx.Outputs.Add(payeeReward, payeeAddress);
            reward -= payeeReward;
        }
        return reward;
    }
    #endregion // CommunityAddres

    #region CoinbaseDevReward

    protected CoinbaseDevRewardTemplateExtra CoinbaseDevRewardParams;

    protected virtual Money CreateCoinbaseDevRewardOutputs(Transaction tx, Money reward)
    {
        if(CoinbaseDevRewardParams.CoinbaseDevReward != null)
        {
            CoinbaseDevReward[] CBRewards;
            CBRewards = new[] { CoinbaseDevRewardParams.CoinbaseDevReward.ToObject<CoinbaseDevReward>() };

            foreach(var CBReward in CBRewards)
            {
                if(!string.IsNullOrEmpty(CBReward.ScriptPubkey))
                {
                    Script payeeAddress = new Script(CBReward.ScriptPubkey.HexToByteArray());
                    var payeeReward = CBReward.Value;
                    tx.Outputs.Add(payeeReward, payeeAddress);
                    reward -= payeeReward;
                }
            }
        }
        return reward;
    }

    #endregion // CoinbaseDevReward

    #region CoinbaseStakingReward

    protected CoinbaseStakingRewardTemplateExtra coinbaseStakingRewardParameters;

    protected virtual Money CreateCoinbaseStakingRewardOutputs(Transaction tx, Money reward)
    {
        if(!string.IsNullOrEmpty(coinbaseStakingRewardParameters.PayoutScript?.ScriptPubkey))
        {
            Script payeeAddress = new Script(coinbaseStakingRewardParameters.PayoutScript.ScriptPubkey.HexToByteArray());
            var payeeReward = coinbaseStakingRewardParameters.MinimumValue;
            tx.Outputs.Add(payeeReward, payeeAddress);
            reward -= payeeReward;
        }
        return reward;
    }

    #endregion // CoinbaseStakingReward

    #region Community

    protected CommunityBlockTemplateExtra communityParameters;

    protected virtual Money CreateCommunityOutputs(Transaction tx, Money reward)
    {
        if (communityParameters.Community != null)
        {
            Community[] communitys;
            if (communityParameters.Community.Type == JTokenType.Array)
                communitys = communityParameters.Community.ToObject<Community[]>();
            else
                communitys = new[] { communityParameters.Community.ToObject<Community>() };

            if(communitys != null)
            {
                foreach(var Community in communitys)
                {
                    if(!string.IsNullOrEmpty(Community.Script))
                    {
                        Script payeeAddress = new (Community.Script.HexToByteArray());
                        var payeeReward = Community.Amount;

                        tx.Outputs.Add(payeeReward, payeeAddress);
                        reward -= payeeReward;
                    }
                }
            }
        }

        return reward;
    }

    #endregion //Community

    #region DataMining

    protected DataMiningBlockTemplateExtra dataminingParameters;

    protected virtual Money CreateDataMiningOutputs(Transaction tx, Money reward)
    {
        if (dataminingParameters.DataMining != null)
        {
            DataMining[] dataminings;
            if (dataminingParameters.DataMining.Type == JTokenType.Array)
                dataminings = dataminingParameters.DataMining.ToObject<DataMining[]>();
            else
                dataminings = new[] { dataminingParameters.DataMining.ToObject<DataMining>() };

            if(dataminings != null)
            {
                foreach(var DataMining in dataminings)
                {
                    if(!string.IsNullOrEmpty(DataMining.Script))
                    {
                        Script payeeAddress = new (DataMining.Script.HexToByteArray());
                        var payeeReward = DataMining.Amount;

                        tx.Outputs.Add(payeeReward, payeeAddress);
                        reward -= payeeReward;
                    }
                }
            }
        }

        return reward;
    }

    #endregion //DataMining

    #region Developer

    protected DeveloperBlockTemplateExtra developerParameters;

    protected virtual Money CreateDeveloperOutputs(Transaction tx, Money reward)
    {
        if (developerParameters.Developer != null)
        {
            Developer[] developers;
            if (developerParameters.Developer.Type == JTokenType.Array)
                developers = developerParameters.Developer.ToObject<Developer[]>();
            else
                developers = new[] { developerParameters.Developer.ToObject<Developer>() };

            if(developers != null)
            {
                foreach(var Developer in developers)
                {
                    if(!string.IsNullOrEmpty(Developer.Script))
                    {
                        Script payeeAddress = new (Developer.Script.HexToByteArray());
                        var payeeReward = Developer.Amount;

                        tx.Outputs.Add(payeeReward, payeeAddress);
                        reward -= payeeReward;
                    }
                }
            }
        }

        return reward;
    }

    #endregion //Developer

    #region Foundation

    protected FoundationBlockTemplateExtra foundationParameters;

    protected virtual Money CreateFoundationOutputs(Transaction tx, Money reward)
    {
        if(foundationParameters.Foundation != null)
        {
            Foundation[] foundations;
            if(foundationParameters.Foundation.Type == JTokenType.Array)
                foundations = foundationParameters.Foundation.ToObject<Foundation[]>();
            else
                foundations = new[] { foundationParameters.Foundation.ToObject<Foundation>() };

            if(foundations != null)
            {
                foreach(var Foundation in foundations)
                {
                    if(!string.IsNullOrEmpty(Foundation.Payee))
                    {
                        var payeeAddress = BitcoinUtils.AddressToDestination(Foundation.Payee, network);
                        var payeeReward = Foundation.Amount;

                        tx.Outputs.Add(payeeReward, payeeAddress);
                        reward -= payeeReward;
                    }
                }
            }
        }
        return reward;
    }

    #endregion // Foundation

    #region GovernanceAddress

    protected virtual Money CreateGovernanceAddressOutputs(Transaction tx, Money reward)
    {
        if(BlockTemplate.GovernanceReward > 0)
        {
            var payeeReward = BlockTemplate.GovernanceReward;
            var payeeAddress = BitcoinUtils.BechSegwitAddressToDestination(BlockTemplate.GovernanceAddress, network, coin?.BechPrefix);
            tx.Outputs.Add(payeeReward, payeeAddress);
            reward -= payeeReward;
        }
        return reward;
    }
    #endregion // GovernanceAddress

    #region API-Surface

    public BlockTemplate BlockTemplate { get; protected set; }
    public double Difficulty { get; protected set; }
    public string JobId { get; protected set; }

    public List<byte[]> MerkleBranchSteps => mt?.Steps?.ToList() ?? new List<byte[]>();
    public AuxBlockData[] AuxBlocks => auxBlocks;
    public AuxBlockData RskAuxBlock => rskAuxBlock;
    public AuxBlockData HathorAuxBlock => hathorAuxBlock;
    public byte[][] AuxMerkleNodes => auxMerkleNodes;
    public int AuxMerkleTreeSize => auxMerkleTreeSize;
    public uint AuxMerkleTreeNonce => auxMerkleTreeNonce;

    public void Init(BlockTemplate blockTemplate, string jobId,
        PoolConfig pc, BitcoinPoolConfigExtra extraPoolConfig,
        ClusterConfig cc, IMasterClock clock,
        IDestination poolAddressDestination, Network network,
        bool isPoS, double shareMultiplier, IHashAlgorithm coinbaseHasher,
        IHashAlgorithm headerHasher, IHashAlgorithm blockHasher,
        AuxBlockData[] auxBlocks = null,
        AuxBlockData rskAuxBlock = null,
        AuxBlockData hathorAuxBlock = null)
    {
        Contract.RequiresNonNull(blockTemplate);
        Contract.RequiresNonNull(pc);
        Contract.RequiresNonNull(cc);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(poolAddressDestination);
        Contract.RequiresNonNull(coinbaseHasher);
        Contract.RequiresNonNull(headerHasher);
        Contract.RequiresNonNull(blockHasher);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId));

        coin = pc.Template.As<BitcoinTemplate>();
        this.extraPoolConfig = extraPoolConfig;
        this.auxBlocks = auxBlocks;
        this.rskAuxBlock = rskAuxBlock;
        this.hathorAuxBlock = hathorAuxBlock;
        networkParams = coin.GetNetwork(network.ChainName);
        txVersion = coin.CoinbaseTxVersion;
        this.network = network;
        this.clock = clock;
        this.poolAddressDestination = poolAddressDestination;
        BlockTemplate = blockTemplate;
        JobId = jobId;

        var coinbaseString = !string.IsNullOrEmpty(cc.PaymentProcessing?.CoinbaseString) ?
            cc.PaymentProcessing?.CoinbaseString.Trim() : "Miningcore";

        scriptSigFinalBytes = new Script(Op.GetPushOp(Encoding.UTF8.GetBytes(coinbaseString))).ToBytes();

        Difficulty = new Target(System.Numerics.BigInteger.Parse(BlockTemplate.Target, NumberStyles.HexNumber)).Difficulty;

        extraNoncePlaceHolderLength = BitcoinConstants.ExtranoncePlaceHolderLength;
        this.isPoS = isPoS;
        this.shareMultiplier = shareMultiplier;

        txComment = !string.IsNullOrEmpty(extraPoolConfig?.CoinbaseTxComment) ?
            extraPoolConfig.CoinbaseTxComment : coin.CoinbaseTxComment;

        if(coin.HasMasterNodes)
        {
            masterNodeParameters = BlockTemplate.Extra.SafeExtensionDataAs<MasterNodeBlockTemplateExtra>();

            if(coin.HasSmartNodes)
            {
                if(masterNodeParameters.Extra?.ContainsKey("smartnode") == true)
                {
                    masterNodeParameters.Masternode = JToken.FromObject(masterNodeParameters.Extra["smartnode"]);
                }
            }

            if(!string.IsNullOrEmpty(masterNodeParameters.CoinbasePayload))
            {
                txVersion = 3;
                const uint txType = 5;
                txVersion += txType << 16;
            }
        }

        if(coin.HasPayee)
            payeeParameters = BlockTemplate.Extra.SafeExtensionDataAs<PayeeBlockTemplateExtra>();

        if(coin.HasFounderFee)
        {
            founderParameters = BlockTemplate.Extra.SafeExtensionDataAs<FounderBlockTemplateExtra>();

            // Direct extraction fallback: SafeExtensionDataAs silently swallows all exceptions.
            // If it returned null or left FounderReward empty, pull directly from the raw Extra dict.
            if(founderParameters?.FounderReward == null &&
               BlockTemplate.Extra != null &&
               BlockTemplate.Extra.TryGetValue("founderreward", out var frToken) &&
               frToken is JToken frJToken && frJToken.Type != JTokenType.Null)
            {
                founderParameters ??= new FounderBlockTemplateExtra();
                founderParameters.FounderReward = frJToken.ToObject<FounderRewardEntry>();
            }

            logger.Info(() => $"HasFounderFee: founderParameters={founderParameters != null}, " +
                $"Founder={founderParameters?.Founder?.Type}, " +
                $"FounderReward={founderParameters?.FounderReward != null}, " +
                $"Payee={founderParameters?.FounderReward?.Founderpayee}, " +
                $"Amount={founderParameters?.FounderReward?.Amount}");
        }

        if(coin.HasFundReward)
            fundRewardParameters = BlockTemplate.Extra.SafeExtensionDataAs<FundRewardBlockTemplateExtra>();

        if(coin.HasFortuneReward)
            fortuneParameters = BlockTemplate.Extra.SafeExtensionDataAs<FortuneBlockTemplateExtra>();

        if(coin.HasMinerFund)
            minerFundParameters = BlockTemplate.Extra.SafeExtensionDataAs<MinerFundTemplateExtra>("coinbasetxn", "minerfund");

        if(coin.HasCoinbaseDevReward)
            CoinbaseDevRewardParams = BlockTemplate.Extra.SafeExtensionDataAs<CoinbaseDevRewardTemplateExtra>();

        if (coin.HasCoinbaseStakingReward)
            coinbaseStakingRewardParameters = BlockTemplate.Extra.SafeExtensionDataAs<CoinbaseStakingRewardTemplateExtra>("coinbasetxn", "stakingrewards");

        if(coin.HasCommunity)
            communityParameters = BlockTemplate.Extra.SafeExtensionDataAs<CommunityBlockTemplateExtra>();

        if(coin.HasDataMining)
            dataminingParameters = BlockTemplate.Extra.SafeExtensionDataAs<DataMiningBlockTemplateExtra>();

        if(coin.HasDeveloper)
            developerParameters = BlockTemplate.Extra.SafeExtensionDataAs<DeveloperBlockTemplateExtra>();

        if (coin.HasFoundation)
            foundationParameters = BlockTemplate.Extra.SafeExtensionDataAs<FoundationBlockTemplateExtra>();

        this.coinbaseHasher = coinbaseHasher;
        this.headerHasher = headerHasher;
        this.blockHasher = blockHasher;

        // Always use Bits field for target calculation - some nodes (like DGB v9) return Target in wrong byte order
        // Bits is the standard compact target format that's guaranteed to be correct
        var tmp = new Target(BlockTemplate.Bits.HexToByteArray());
        blockTargetValue = tmp.ToUInt256();

        previousBlockHashReversedHex = BlockTemplate.PreviousBlockhash
            .HexToByteArray()
            .ReverseByteOrder()
            .ToHexString();

        BuildMerkleBranches();
        BuildCoinbase();

        var jobVersion = GetBaseVersion();

        jobParams = new object[]
        {
            JobId,
            previousBlockHashReversedHex,
            coinbaseInitialHex,
            coinbaseFinalHex,
            merkleBranchesHex,
            jobVersion.ToStringHex8(),
            BlockTemplate.Bits,
            BlockTemplate.CurTime.ToStringHex8(),
            false
        };
    }

    public object GetJobParams(bool isNew)
    {
        jobParams[^1] = isNew;
        return jobParams;
    }

    public virtual (Share Share, string BlockHex) ProcessShare(StratumConnection worker,
        string extraNonce2, string nTime, string nonce, string versionBits = null)
    {
        Contract.RequiresNonNull(worker);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce2));
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nTime));
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce));

        var context = worker.ContextAs<BitcoinWorkerContext>();

        // validate nTime
        if(nTime.Length != 8)
            throw new StratumException(StratumError.Other, "incorrect size of ntime");

        var nTimeInt = uint.Parse(nTime, NumberStyles.HexNumber);
        if(nTimeInt < BlockTemplate.CurTime || nTimeInt > ((DateTimeOffset) clock.Now).ToUnixTimeSeconds() + 7200)
            throw new StratumException(StratumError.Other, "ntime out of range");

        // validate nonce
        if(nonce.Length != 8)
            throw new StratumException(StratumError.Other, "incorrect size of nonce");

        var nonceInt = uint.Parse(nonce, NumberStyles.HexNumber);

        // validate version-bits (overt ASIC boost or firmware-applied rolling without negotiation)
        uint versionBitsInt = 0;

        if(versionBits != null)
        {
            versionBitsInt = uint.Parse(versionBits, NumberStyles.HexNumber);

            // Only enforce mask constraint when version rolling was formally negotiated
            if(context.VersionRollingMask.HasValue && (versionBitsInt & ~context.VersionRollingMask.Value) != 0)
                throw new StratumException(StratumError.Other, "rolling-version mask violation");
        }

        // dupe check — include versionBits so AsicBoost miners can submit same nonce with different version bits
        if(!RegisterSubmit(context.ExtraNonce1, extraNonce2, nTime, nonce, versionBits))
            throw new StratumException(StratumError.DuplicateShare, "duplicate share");

        var (share, blockHex, auxCandidates) = ProcessShareInternal(worker, extraNonce2, nTimeInt, nonceInt, versionBitsInt);
        share.AuxCandidates = auxCandidates;

        // If the coin reserves nVersion bits for PoW type selection (e.g. LCC bit 16),
        // suppress block submission when those bits are set in the miner-submitted nVersion.
        // The share is still counted for difficulty; we just don't submit an invalid block.
        if(share.IsBlockCandidate && blockHex != null && extraPoolConfig?.VersionBlockedBits != null && versionBitsInt != 0)
        {
            var blockedMask = uint.Parse(extraPoolConfig.VersionBlockedBits, System.Globalization.NumberStyles.HexNumber);
            if((versionBitsInt & blockedMask) != 0)
            {
                share.IsBlockCandidate = false;
                blockHex = null;
                logger.Info(() => "[" + worker.ConnectionId + "] Block candidate suppressed: miner nVersion=0x" + versionBitsInt.ToString("X8") + " has blocked bits " + extraPoolConfig.VersionBlockedBits + " set - not submitting to daemon");
            }
        }

        return (share, blockHex);
    }

    #endregion // API-Surface
}
