using FluentValidation;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Extensions;
using System.Security.Cryptography.X509Certificates;

namespace Miningcore.Configuration;

#region Validators

public class EmailSenderConfigValidator : AuthenticatedNetworkEndpointConfigValidator<EmailSenderConfig>
{
    public EmailSenderConfigValidator()
    {
        RuleFor(j => j.FromAddress)
            .NotNull()
            .NotEmpty()
            .WithMessage("EmailSender fromAddress missing or empty");
    }
}

public class AdminNotificationsValidator : AbstractValidator<AdminNotifications>
{
    public AdminNotificationsValidator()
    {
        RuleFor(j => j.EmailAddress)
            .NotNull()
            .NotEmpty()
            .When(x => x.Enabled)
            .WithMessage("Admin notification recipient missing or empty");
    }
}

public class NotificationsConfigValidator : AbstractValidator<NotificationsConfig>
{
    public NotificationsConfigValidator()
    {
        RuleFor(j => j.Email)
            .NotNull()
            .When(x => x.Enabled)
            .WithMessage("You must configure at least one notifications provider when notifications are enabled");
    }
}

public class NetworkEndpointConfigValidator<T> : AbstractValidator<T>
    where T : NetworkEndpointConfig
{
    public NetworkEndpointConfigValidator()
    {
        RuleFor(j => j.Host)
            .NotNull()
            .NotEmpty()
            .WithMessage("Host missing or empty");

        RuleFor(j => j.Port)
            .InclusiveBetween(1, 65535)
            .WithMessage("Invalid port number '{PropertyValue}'");
    }
}

public class AuthenticatedNetworkEndpointConfigValidator<T> : NetworkEndpointConfigValidator<T>
    where T : AuthenticatedNetworkEndpointConfig
{
}

public class PoolEndpointValidator : AbstractValidator<PoolEndpoint>
{
    public PoolEndpointValidator()
    {
        RuleFor(j => j.Difficulty)
            .GreaterThan(0)
            .WithMessage("Pool Endpoint: Difficulty missing or invalid");

        RuleFor(j => j.TlsPfxFile)
            .NotNull()
            .NotEmpty()
            .When(j => j.Tls)
            .WithMessage("Pool Endpoint: Tls enabled but neither TlsPemFile nor TlsPfxFile specified");

        RuleFor(j => j.TlsPfxFile)
            .Must(File.Exists)
            .When(j => j.Tls)
            .WithMessage(j => $"Pool Endpoint: {j.TlsPfxFile} does not exist");

        RuleFor(j => j.TlsPfxFile)
            .Must((j, h, c) =>
            {
                try
                {
                    var tlsCert = new X509Certificate2(h, j.TlsPfxPassword);
                    return tlsCert.HasPrivateKey;
                }
                catch
                {
                    return false;
                }
            })
            .When(j => j.Tls)
            .WithMessage(j => $"Pool Endpoint: {j.TlsPfxFile} is not valid or does not include the private key and cannot be used");
        RuleFor(j => j.VarDiff)
            .SetValidator(new VarDiffConfigValidator())
            .When(x => x.VarDiff != null);
    }
}

public class ApiConfigValidator : AbstractValidator<ApiConfig>
{
    public ApiConfigValidator()
    {
        RuleFor(j => j.ListenAddress)
            .NotNull()
            .NotEmpty()
            .WithMessage("API: listenAddress missing or empty");

        RuleFor(j => j.Port)
            .InclusiveBetween(1, 65535)
            .WithMessage("API: Invalid port number '{PropertyValue}'");
    }
}

public class VarDiffConfigValidator : AbstractValidator<VarDiffConfig>
{
    public VarDiffConfigValidator()
    {
        RuleFor(j => j.MaxDiff)
            .GreaterThanOrEqualTo(x => x.MinDiff)
            .When(x => x.MaxDiff.HasValue)
            .WithMessage("VarDiff: max value must be greater or equal min value");

        RuleFor(j => j.VariancePercent)
            .InclusiveBetween(1, 100)
            .WithMessage("VarDiff: variancePercent must be a percentage betwen 1 and 100");

        RuleFor(j => j.TargetTime)
            .GreaterThan(0)
            .WithMessage("VarDiff: targetTime invalid");

        RuleFor(j => j.RetargetTime)
            .GreaterThan(0)
            .WithMessage("VarDiff: retargetTime invalid");
    }
}

public class PoolConfigValidator : AbstractValidator<PoolConfig>
{
    public PoolConfigValidator()
    {
        RuleFor(j => j.Id)
            .NotNull()
            .NotEmpty()
            .WithMessage("Pool: id missing or empty");

        RuleFor(j => j.Coin)
            .NotNull()
            .WithMessage("Pool: Coin config missing or empty");

        RuleFor(j => j.Ports)
            .NotNull()
            .NotEmpty()
            .When(j => j.EnableInternalStratum == true)
            .WithMessage("Pool: Stratum port config missing or empty");

        RuleFor(j => j.Ports)
            .Must((pc, ports, ctx) =>
            {
                if(ports?.Keys.Any(port => port < 0) == true)
                {
                    ctx.MessageFormatter.AppendArgument("port", ports.Keys.First(port => port < 0));
                    return false;
                }

                return true;
            })
            .WithMessage("Pool: Invalid stratum port number {port}");

        RuleForEach(j => j.Ports.Values)
            .SetValidator(x => new PoolEndpointValidator())
            .When(x => x.Ports != null);

        RuleFor(j => j.Address)
            .NotNull()
            .NotEmpty()
            .WithMessage("Pool: Wallet address missing or empty");

        RuleFor(j => j.Daemons)
            .NotNull()
            .NotEmpty()
            .WithMessage("Pool: Daemons missing or empty");

        RuleForEach(j => j.Daemons)
            .SetValidator(new AuthenticatedNetworkEndpointConfigValidator<DaemonEndpointConfig>());

        // Validate auxChains don't include the pool's own coin
        RuleFor(j => j.Extra)
            .Must((pool, extra, ctx) =>
            {
                var bitcoinExtra = extra?.SafeExtensionDataAs<BitcoinPoolConfigExtra>();
                if(bitcoinExtra?.AuxChains != null)
                {
                    var selfAuxCoin = bitcoinExtra.AuxChains.FirstOrDefault(aux => aux.Id == pool.Coin);
                    if(selfAuxCoin != null)
                    {
                        ctx.MessageFormatter.AppendArgument("coinId", pool.Coin);
                        return false;
                    }
                }
                return true;
            })
            .WithMessage("Pool cannot have itself ({coinId}) as an auxiliary coin");

        // Validate each auxChain has required fields, unique ids, and valid chainId
        RuleFor(j => j.Extra)
            .Must((pool, extra, ctx) =>
            {
                var bitcoinExtra = extra?.SafeExtensionDataAs<BitcoinPoolConfigExtra>();
                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenChainIds = new Dictionary<int, string>();

                bool Fail(string issue)
                {
                    ctx.MessageFormatter.AppendArgument("issue", issue);
                    return false;
                }

                if(bitcoinExtra?.AuxChains != null)
                {
                    foreach(var aux in bitcoinExtra.AuxChains)
                    {
                        if(string.IsNullOrEmpty(aux.Id))
                            return Fail("auxChain entry is missing 'id'");
                        if(!seenIds.Add(aux.Id))
                            return Fail($"duplicate auxChain id '{aux.Id}'");
                        if(aux.Daemons == null || aux.Daemons.Length == 0)
                            return Fail($"auxChain '{aux.Id}' is missing daemons");
                        // the aux merkle tree slot is derived from chainId: two chains on one slot overwrite each other
                        if(seenChainIds.TryGetValue(aux.ChainId, out var other))
                            return Fail($"auxChain '{aux.Id}' and '{other}' share chainId {aux.ChainId}");
                        seenChainIds[aux.ChainId] = aux.Id;
                        if(aux.Decimals < 0 || aux.Decimals > 18)
                            return Fail($"auxChain '{aux.Id}' has an invalid 'decimals' value {aux.Decimals}");
                        if(!Miningcore.Blockchain.Bitcoin.DifficultyUnits.TryParseDiff1(aux.Diff1, out _))
                            return Fail($"auxChain '{aux.Id}' has an invalid 'diff1' value (expected plain hex, no 0x prefix)");
                    }
                }

                // RSK and Hathor are configured separately but must not collide with the aux chains either;
                // without daemons their managers throw at pool start instead of at config validation
                if(bitcoinExtra?.RskChain != null)
                {
                    var rsk = bitcoinExtra.RskChain;
                    if(rsk.Daemons == null || rsk.Daemons.Length == 0)
                        return Fail($"rskChain '{rsk.Id}' is missing daemons");
                    if(!seenIds.Add(rsk.Id ?? "rsk"))
                        return Fail($"rskChain id '{rsk.Id}' is also used by an auxChain");
                    if(seenChainIds.TryGetValue(rsk.ChainId, out var other))
                        return Fail($"rskChain and auxChain '{other}' share chainId {rsk.ChainId}");
                    seenChainIds[rsk.ChainId] = rsk.Id ?? "rsk";
                }

                if(bitcoinExtra?.HathorChain != null)
                {
                    var hathor = bitcoinExtra.HathorChain;
                    if(hathor.Daemons == null || hathor.Daemons.Length == 0)
                        return Fail($"hathorChain '{hathor.Id}' is missing daemons");
                    if(!seenIds.Add(hathor.Id ?? "hathor"))
                        return Fail($"hathorChain id '{hathor.Id}' is also used by another merged chain");
                    if(seenChainIds.TryGetValue(hathor.ChainId, out var other))
                        return Fail($"hathorChain and '{other}' share chainId {hathor.ChainId}");
                }

                return true;
            })
            .WithMessage("Merged mining config error: {issue}");
    }
}

public class ClusterConfigValidator : AbstractValidator<ClusterConfig>
{
    public ClusterConfigValidator()
    {
        RuleFor(j => j.PaymentProcessing)
            .NotNull();

        RuleFor(j => j.Persistence)
            .NotNull()
            .When(x => x.PaymentProcessing?.Enabled == true && x.ShareRelay == null);

        RuleFor(j => j.Pools)
            .NotNull()
            .NotEmpty();

        RuleFor(j => j.InstanceId)
            .GreaterThan((byte) 0)
            .When(x => x.InstanceId.HasValue)
            .WithMessage("instanceId must either be omitted or be non-zero");;

        // ensure pool ids are unique
        RuleFor(j => j.Pools)
            .Must((pc, pools, ctx) =>
            {
                var ids = pools
                    .GroupBy(x => x.Id)
                    .ToArray();

                if(ids.Any(id => id.Count() > 1))
                {
                    ctx.MessageFormatter.AppendArgument("poolId", ids.First(id => id.Count() > 1).Key);
                    return false;
                }

                return true;
            })
            .WithMessage("Duplicate pool id '{poolId}'");

        // ensure stratum ports are not assigned multiple times
        RuleFor(j => j.Pools)
            .Must((pc, pools, ctx) =>
            {
                var ports = pools.Where(x => x.Ports?.Any() == true).SelectMany(x => x.Ports.Select(y => y.Key))
                    .GroupBy(x => x)
                    .ToArray();

                foreach(var port in ports)
                {
                    if(port.Count() > 1)
                    {
                        ctx.MessageFormatter.AppendArgument("port", port.Key);
                        return false;
                    }
                }

                return true;
            })
            .WithMessage("Stratum port {port} assigned multiple times");

        RuleForEach(j => j.Pools)
            .SetValidator(new PoolConfigValidator());
    }
}

#endregion // Validators

public partial class ClusterLoggingConfig
{
}

public partial class VarDiffConfig
{
}

public partial class PoolShareBasedBanningConfig
{
}

public partial class PoolPaymentProcessingConfig
{
}

public partial class ClusterPaymentProcessingConfig
{
}

public partial class PersistenceConfig
{
}

public partial class NetworkEndpointConfig
{
}

public partial class AuthenticatedNetworkEndpointConfig
{
}

public partial class EmailSenderConfig
{
}

public partial class AdminNotifications
{
}

public partial class NotificationsConfig
{
}

public partial class ApiConfig
{
}

public partial class PoolConfig
{
}

public partial class ClusterConfig
{
    public void Validate()
    {
        var validator = new ClusterConfigValidator();
        var result = validator.Validate(this);

        if(!result.IsValid)
            throw new ValidationException(result.Errors);
    }
}
