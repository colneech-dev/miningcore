namespace Miningcore.Blockchain.Bitcoin.DaemonResponses;

public class MiningInfo
{
    public int Blocks { get; set; }
    public int CurrentBlockSize { get; set; }
    public int CurrentBlockWeight { get; set; }
    public double Difficulty { get; set; }
    public double NetworkHashps { get; set; }

    /// <summary>
    /// Multi-algorithm chains (DigiByte) report one hashrate per algorithm here, while
    /// <see cref="NetworkHashps"/> only covers the default algorithm (sha256d).
    /// </summary>
    public Dictionary<string, double> NetworkHashesPs { get; set; }
    public string Chain { get; set; }
}
