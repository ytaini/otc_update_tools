using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Otc.Update.Core;

/// <summary>更新协议常量。</summary>
public static class UpdateConstants
{
    /// <summary>当前更新包清单协议版本。</summary>
    public const int SchemaVersion = 1;

    /// <summary>更新包扩展名。</summary>
    public const string PackageExtension = ".otcupdate";

    /// <summary>更新包内清单文件名。</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>更新包内负载目录名。</summary>
    public const string PayloadDirectoryName = "payload";
}

/// <summary>更新发布范围。</summary>
public static class UpdateTargetTypes
{
    /// <summary>全部客户端。</summary>
    public const string All = "all";

    /// <summary>指定设备。</summary>
    public const string Device = "device";

    /// <summary>指定车间。</summary>
    public const string Workshop = "workshop";

    /// <summary>按百分比灰度发布。</summary>
    public const string Rollout = "rollout";
}

/// <summary>更新包内单个文件的校验信息。</summary>
public sealed class UpdatePackageFile
{
    /// <summary>相对负载根目录的路径。</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>文件字节数。</summary>
    public long Size { get; set; }

    /// <summary>文件 SHA-256，小写十六进制。</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>更新包清单。</summary>
public sealed class UpdatePackageManifest
{
    /// <summary>清单协议版本。</summary>
    public int SchemaVersion { get; set; } = UpdateConstants.SchemaVersion;

    /// <summary>产品编码，例如 WCS_WPF / WMS_WPF。</summary>
    public string Product { get; set; } = string.Empty;

    /// <summary>产品类型，例如 WCS / WMS。</summary>
    public string ProductType { get; set; } = string.Empty;

    /// <summary>更新版本。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>发布 UTC 时间。</summary>
    public DateTime PublishedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>文件数量。</summary>
    public int FileCount { get; set; }

    /// <summary>负载总字节数。</summary>
    public long TotalSize { get; set; }

    /// <summary>负载文件校验清单。</summary>
    public List<UpdatePackageFile> Files { get; set; } = new();
}

/// <summary>数据库中的更新候选。</summary>
public sealed class UpdateRelease
{
    /// <summary>发布记录编号。</summary>
    public long Id { get; set; }

    /// <summary>版本号。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>系统类型。</summary>
    public string ProductType { get; set; } = string.Empty;

    /// <summary>产品编码。</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>包地址，支持受信 UNC 路径或 HTTPS。</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>完整包 SHA-256。</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>完整包字节数。</summary>
    public long FileSize { get; set; }

    /// <summary>清单 RSA 签名（Base64）。</summary>
    public string ManifestSignature { get; set; } = string.Empty;

    /// <summary>是否强制更新。</summary>
    public bool Mandatory { get; set; }

    /// <summary>发布范围：all / device / workshop / rollout。</summary>
    public string TargetType { get; set; } = UpdateTargetTypes.All;

    /// <summary>设备名或车间编码；全部发布时为空。</summary>
    public string TargetValue { get; set; } = string.Empty;

    /// <summary>灰度百分比，0-100。</summary>
    public int RolloutPercent { get; set; } = 100;

    /// <summary>发布说明。</summary>
    public string? Remark { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreateAt { get; set; }
}

/// <summary>当前客户端参与发布策略的身份信息。</summary>
public sealed class UpdateClientIdentity
{
    /// <summary>产品编码。</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>系统类型，例如 WCS / WMS / WCM。</summary>
    public string ProductType { get; set; } = string.Empty;

    /// <summary>设备唯一标识；一般使用计算机名。</summary>
    public string DeviceId { get; set; } = Environment.MachineName;

    /// <summary>车间编码。</summary>
    public string WorkshopCode { get; set; } = string.Empty;
}

/// <summary>下载进度快照。</summary>
public sealed class UpdateDownloadProgress
{
    /// <summary>已接收字节数。</summary>
    public long BytesReceived { get; init; }

    /// <summary>总字节数；未知时为 0。</summary>
    public long TotalBytes { get; init; }

    /// <summary>完成百分比；总大小未知时为 0。</summary>
    public double Percent => TotalBytes <= 0
        ? 0
        : Math.Clamp(BytesReceived * 100d / TotalBytes, 0, 100);
}

/// <summary>更新器启动参数。</summary>
public sealed class UpdateLaunchOptions
{
    /// <summary>产品编码。</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>系统类型，例如 WCS / WMS / WCM。</summary>
    public string ProductType { get; set; } = string.Empty;

    /// <summary>旧版本。</summary>
    public string FromVersion { get; set; } = string.Empty;

    /// <summary>目标版本。</summary>
    public string ToVersion { get; set; } = string.Empty;

    /// <summary>本地更新包。</summary>
    public string PackagePath { get; set; } = string.Empty;

    /// <summary>安装目录。</summary>
    public string InstallDirectory { get; set; } = string.Empty;

    /// <summary>主程序文件名。</summary>
    public string ApplicationExecutable { get; set; } = string.Empty;

    /// <summary>等待退出的父进程编号。</summary>
    public int ParentProcessId { get; set; }

    /// <summary>包 SHA-256。</summary>
    public string PackageSha256 { get; set; } = string.Empty;

    /// <summary>包字节数。</summary>
    public long PackageSize { get; set; }

    /// <summary>清单签名。</summary>
    public string ManifestSignature { get; set; } = string.Empty;

    /// <summary>签名公钥 PEM。</summary>
    public string PublicKeyPem { get; set; } = string.Empty;

    /// <summary>健康检查超时秒数。</summary>
    public int HealthTimeoutSeconds { get; set; } = 60;

    /// <summary>备份根目录。</summary>
    public string BackupRoot { get; set; } = string.Empty;

    /// <summary>更新健康标记与结果文件根目录。</summary>
    public string UpdateRoot { get; set; } = string.Empty;

    /// <summary>
    /// 更新器界面语言代码（zh-CN / en-US / ko-KR）。
    /// </summary>
    /// <remarks>
    /// 由主程序在启动更新器时带入，使更新窗口与主程序保持同一语言；
    /// 留空时更新器按系统界面语言显示。
    /// </remarks>
    public string Language { get; set; } = string.Empty;
}

/// <summary>更新执行结果。</summary>
public sealed class UpdateResult
{
    /// <summary>结果状态：success / rolled_back / failed。</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>产品编码。</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>升级前版本。</summary>
    public string FromVersion { get; set; } = string.Empty;

    /// <summary>目标版本。</summary>
    public string ToVersion { get; set; } = string.Empty;

    /// <summary>说明或失败原因。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>备份目录。</summary>
    public string BackupPath { get; set; } = string.Empty;

    /// <summary>完成时间 UTC。</summary>
    public DateTime FinishedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>是否成功运行并达到目标版本。</summary>
    [JsonIgnore]
    public bool Succeeded => string.Equals(State, "success", StringComparison.OrdinalIgnoreCase);
}

/// <summary>健康标记。</summary>
public sealed class UpdateHealthMarker
{
    /// <summary>产品编码。</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>实际运行版本。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>进程编号。</summary>
    public int ProcessId { get; set; }

    /// <summary>写入时间 UTC。</summary>
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
}
