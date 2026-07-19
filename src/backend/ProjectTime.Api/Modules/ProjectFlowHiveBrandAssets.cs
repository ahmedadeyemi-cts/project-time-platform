using System.Security.Cryptography;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Exact embedded copy of the repository-owned US Signal JPEG logo at
/// src/frontend/project-time-web/brand/ussignal.jpg. Embedding keeps the API
/// container self-contained without introducing a deployment-file change.
/// </summary>
internal static class ProjectFlowHiveBrandAssets
{
    public const string LogoSha256 =
        "c4fc4b33f744d065deeec531f393aa39996273e51eb946a452b1319e6e529183";

    private const string LogoJpegBase64 =
        "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" +
        "tyheKjvdrh4GPhkr6dsptUlL2faDm7UtLQ3HXOSoJwnipqitvkNdUtpKd9tcyWeQ8oiBPUk46K1DjbOOXUSu68bZHVborFYx" +
        "LDn5JaiU80g8eRo2Hv8ARe7SnGOK53Wnt16toonzyCNlRDIXsDycAOaQC0HpnJxnfA3UDsFi1LarvI7SNZa7tOyM5fQ1sEvy" +
        "E9S1zgR3f7rMWpqu16m5NWaft80zZW9u19MGStJ/WDgcZ7wq0j8J2Z0Tc6+mtlvqK6uk7KngjMkjyOgCqCu43VctQ5tn08HR" +
        "A/K6eVznuHiWsG31KsnW1LR3TSNwpa6vhoqaohA+JmcGtYSQWk5xtnCo3Tdo1daa+qfpOW23J4YBK+grIKhuO7YuDh08FGNR" +
        "p2VJu+Fh6J4uQX+6wWq528UNRUHlhljk52Od/VIIBafDqvg3i1UnV8dhNki5HXFlH2/xJzgyBnNy8vnnGVC9N6qkt2roYtS2" +
        "K3Gb4oMmkbTBk0MhOObIOCc4WrZtxYg36X+LP/nar8at/onZlq624s0WnrjLbLfQur6uDaZzpezjjOM4zgknp3e6jL+Nt4hw" +
        "J9OQMLt288725HllqgN8raWHW1bcLfJHX0rbk6qjJbhsv6TnI9M5Ge8brfcQtfQ6zpKCjprS+mfBNz85PO4nlI5W4HTf8Aur" +
        "GucGzJweLNczSP5cmsUYe64NpGRdu4Ne0xOfzh3L4tx0WuqOObxRRmKxNbVuceYSVP6No7sHGSfYeqjt+tVytPCC2suzZInT" +
        "XsSwwSjDomGGTAI7skF2P2lKuBthtddYK+rraKGolknMRMrA7DcdBn3UpQSbZ25XRvuHvEyDVVa621dF8DXBpfGGyc7JAOuC" +
        "QCCPBa/W3FmSw3qaz22yvnqYNnS1LywOP7LQCXDfrkfTdVloENpOJNujjc4RxV0jBvvygPHvsAtzc9a6h1Lqx8VgpqWCoe8w" +
        "U5DGdoWNJ6vft4ldeNbf4NnRILJxtd8WyLUFnbBC52HTUr3Zj8yxw3HjvnwBXg4/yxz3SxTQuD45KWRzHDoQXNIKg+tIL1TX" +
        "dzNSSQyV/Zgu7KRjsDuzy9Ct/wATCTaNGEu5s2s7+7VcYKMotE7NplycLP8Al7Yv7MP4lStRThZ/y9sX9mH8SpUvLL2zZGUR" +
        "FwBERAEREAREQBRLiFoum1lQQxPm+Hq6ZxdTzgZ5c9Wkd4OB9FLUXU6doeyjKPgdW9o91bd6Zo5HBhhhOS7Hy5JPTOMhSPQ3" +
        "C+bT9dWyXOup62mq6UwPjZEWnB9SVZ+E5dupVPJJqidUUncOC1xp6x01hvTY4skx9oHNkYD3czSvXp7gzJHcGVeoboKlrXh7" +
        "oomkGQj+s4nOFcOB1TCeSR3VHiu9rpLva6i3V0fPTVEZY9o228vRU7VcFrvSVLpLNfWBvRj3B0cjR4EtO6vBYwM5XIzcfQaT" +
        "Km0jwfbbbnDcL3XNqnRP7RsMTSA537RJ33X5HCiv/PGO+uu1OYW3FlYYeydzENkD8Zz5dVbeAsOG2y75JX7Oao5i0++vk1XP" +
        "RcPqqakhrMth+ILd425IySDt4d/ipNeKXiXpW3vu1XcqbsYyO0dA2Jzhk43HZj+K91+4L1IrpKnTdwjZCXl8cMuWui3yA1w7" +
        "h3d+MLwS8Ltb3LEN0uokiB2NRWyTBv3SttounZFNHst9Vc+LmmpLRVS09LX2yriqHVPZkslY5krRsDs7OVYHDjSk+kLPNQVF" +
        "VHUufMZQ+NhaMEeazw90VT6Nt80bZzUVdSQ6ecjAOM8rQO4DJ+p9FK8DCxlL4vRaX5KnsHCi4WrV9Pe5LpTSRRVT5zEInAkO" +
        "5ts5/aXl1JwXfV3GWqslxjhgleZOwnYXchJyeVwPRXHhMJ5JHdUUm7gbOKBgjvMQrjITK90R5C3GwAznOc7k963GpeF1ferd" +
        "YqZl0pon22kMD3uicQ/JByBnborU5R3pgJ5JDVGn0faJLBpu3WmaVsz6WLs3SNGA7c9y3KwBhZUHQiIgCIiAIiIAiIgCIiAI" +
        "iIAiIgCIiAIiIDGEwiIBhZREAREQBERAEREAREQBERAf/9k=";

    private static readonly Lazy<byte[]> LogoBytes = new(() =>
    {
        var bytes = Convert.FromBase64String(LogoJpegBase64);
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!actual.Equals(LogoSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The embedded US Signal logo failed its governed checksum.");
        }
        return bytes;
    });

    public static byte[] LogoJpeg => LogoBytes.Value;
}
