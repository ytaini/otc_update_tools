namespace Otc.Publisher;

/// <summary>发布参数。</summary>
public sealed class PublisherOptions
{
    /// <summary>应用发布目录。</summary>
    public string SourceDirectory { get; set; } = string.Empty;

    /// <summary>Updater 发布目录。</summary>
    public string UpdaterDirectory { get; set; } = string.Empty;

    /// <summary>产品编码。</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>系统类型。</summary>
    public string ProductType { get; set; } = string.Empty;

    /// <summary>版本号。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>共享目录或 HTTPS 发布根目录。</summary>
    public string ShareDirectory { get; set; } = string.Empty;

    /// <summary>PostgreSQL 连接串。</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>RSA 私钥路径。</summary>
    public string PrivateKeyPath { get; set; } = string.Empty;

    /// <summary>发布范围。</summary>
    public string TargetType { get; set; } = UpdateTargetTypes.All;

    /// <summary>目标设备或车间。</summary>
    public string TargetValue { get; set; } = string.Empty;

    /// <summary>灰度百分比。</summary>
    public int RolloutPercent { get; set; } = 100;

    /// <summary>是否强制更新。</summary>
    public bool Mandatory { get; set; }

    /// <summary>发布说明。</summary>
    public string Remark { get; set; } = string.Empty;

    /// <summary>
    /// 界面语言代码（zh-CN / en-US / ko-KR）。
    /// </summary>
    /// <remarks>
    /// 只用于命令行模式与首启默认值；图形界面里由页头语言下拉框直接切换。
    /// </remarks>
    public string Language { get; set; } = string.Empty;
}

/// <summary>发布结果。</summary>
public sealed class PublisherResult
{
    /// <summary>发布包路径。</summary>
    public string PackagePath { get; init; } = string.Empty;

    /// <summary>SHA-256。</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>包大小。</summary>
    public long FileSize { get; init; }
}
