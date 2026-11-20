using Otc.Update.Core.Localization;

namespace Otc.Publisher;

/// <summary>Publisher 命令行兼容模式。</summary>
public static class PublisherCommandLine
{
    /// <summary>解析参数；格式不完整时返回 null。</summary>
    public static PublisherOptions? Parse(string[] values)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index + 1 < values.Length; index += 2)
        {
            map[values[index]] = values[index + 1];
        }

        var connection = Get(map, "--connection");
        if (string.IsNullOrWhiteSpace(connection))
        {
            connection = Environment.GetEnvironmentVariable("OTC_UPDATE_DB_CONNECTION")
                ?? string.Empty;
        }

        var required = new[]
        {
            Get(map, "--source"),
            Get(map, "--product"),
            Get(map, "--type"),
            Get(map, "--version"),
            Get(map, "--share"),
            connection,
            Get(map, "--private-key"),
        };

        if (required.Any(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        return new PublisherOptions
        {
            SourceDirectory = Get(map, "--source"),
            UpdaterDirectory = Get(map, "--updater-dir"),
            ProductCode = Get(map, "--product"),
            ProductType = Get(map, "--type"),
            Version = Get(map, "--version"),
            ShareDirectory = Get(map, "--share"),
            ConnectionString = connection,
            PrivateKeyPath = Get(map, "--private-key"),
            TargetType = Get(map, "--target-type") is { Length: > 0 } target
                ? target
                : UpdateTargetTypes.All,
            TargetValue = Get(map, "--target-value"),
            RolloutPercent = int.TryParse(Get(map, "--rollout-percent"), out var rollout)
                ? Math.Clamp(rollout, 0, 100)
                : 100,
            Mandatory = bool.TryParse(Get(map, "--mandatory"), out var mandatory) && mandatory,
            Remark = Get(map, "--remark"),
            Language = Get(map, "--language"),
        };
    }

    /// <summary>输出命令行帮助。</summary>
    public static string Usage => LocalizationService.Instance["Publisher.Cli.Usage"];

    private static string Get(IReadOnlyDictionary<string, string> map, string key)
        => map.TryGetValue(key, out var value) ? value.Trim() : string.Empty;
}
