using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Otc.Update.Core;

/// <summary>按版本、产品和灰度策略筛选客户端可用更新。</summary>
public static class UpdatePolicySelector
{
    /// <summary>从候选发布中选出当前客户端可用的最高版本。</summary>
    public static UpdateRelease? SelectLatest(
        IEnumerable<UpdateRelease> releases,
        string currentVersion,
        UpdateClientIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(identity);

        return releases
            .Where(release => IsEligible(release, currentVersion, identity))
            .OrderByDescending(release => release.Version, UpdateVersionComparer.Instance)
            .ThenByDescending(release => release.CreateAt)
            .ThenByDescending(release => release.Id)
            .FirstOrDefault();
    }

    /// <summary>判断一条发布记录是否适用于当前客户端。</summary>
    public static bool IsEligible(
        UpdateRelease release,
        string currentVersion,
        UpdateClientIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(identity);

        if (!release.ProductType.Equals(identity.ProductType, StringComparison.OrdinalIgnoreCase)
            || !release.ProductCode.Equals(identity.ProductCode, StringComparison.OrdinalIgnoreCase)
            || !UpdateVersionComparer.IsNewer(release.Version, currentVersion))
        {
            return false;
        }

        return release.TargetType.Trim().ToLowerInvariant() switch
        {
            UpdateTargetTypes.All => true,
            UpdateTargetTypes.Device => release.TargetValue.Equals(
                identity.DeviceId,
                StringComparison.OrdinalIgnoreCase),
            UpdateTargetTypes.Workshop => !string.IsNullOrWhiteSpace(identity.WorkshopCode)
                && release.TargetValue.Equals(
                    identity.WorkshopCode,
                    StringComparison.OrdinalIgnoreCase),
            UpdateTargetTypes.Rollout => IsInRollout(
                identity.DeviceId,
                release.Version,
                release.RolloutPercent),
            _ => false,
        };
    }

    /// <summary>
    /// 设备级稳定灰度：同一设备与版本始终落入同一百分比桶。
    /// </summary>
    public static bool IsInRollout(string deviceId, string version, int rolloutPercent)
    {
        var percent = Math.Clamp(rolloutPercent, 0, 100);

        if (percent <= 0)
        {
            return false;
        }

        if (percent >= 100)
        {
            return true;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{deviceId}|{version}"));
        var bucket = BitConverter.ToUInt32(hash, 0) % 100;

        return bucket < percent;
    }
}

/// <summary>版本号分段数值比较器。</summary>
public sealed class UpdateVersionComparer : IComparer<string>
{
    /// <summary>共享实例。</summary>
    public static readonly UpdateVersionComparer Instance = new();

    /// <summary>比较两个版本号。</summary>
    public int Compare(string? left, string? right)
    {
        var leftParts = Parse(left);
        var rightParts = Parse(right);
        var count = Math.Max(leftParts.Length, rightParts.Length);

        for (var index = 0; index < count; index++)
        {
            var leftValue = index < leftParts.Length ? leftParts[index] : 0;
            var rightValue = index < rightParts.Length ? rightParts[index] : 0;
            var comparison = leftValue.CompareTo(rightValue);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    /// <summary>判断候选版本是否高于当前版本。</summary>
    public static bool IsNewer(string candidate, string current)
        => Instance.Compare(candidate, current) > 0;

    private static int[] Parse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return new[] { 0 };
        }

        return version
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var value) ? Math.Max(value, 0) : 0)
            .ToArray();
    }
}
