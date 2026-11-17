using System.IO;
using System.Linq;

namespace Otc.Update.Core;

/// <summary>更新包来源白名单校验。</summary>
public static class UpdatePathPolicy
{
    /// <summary>
    /// 判断来源是否为受信 UNC、绝对本地目录或 HTTPS 地址。
    /// </summary>
    public static bool IsTrustedSource(string source, IEnumerable<string> trustedRoots)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var roots = trustedRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Environment.ExpandEnvironmentVariables)
            .ToArray();

        if (roots.Length == 0)
        {
            return false;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            return roots.Any(root =>
                Uri.TryCreate(root, UriKind.Absolute, out var rootUri)
                && rootUri.Scheme == Uri.UriSchemeHttps
                && uri.AbsoluteUri.StartsWith(
                    rootUri.AbsoluteUri.TrimEnd('/') + "/",
                    StringComparison.OrdinalIgnoreCase));
        }

        if (!Path.IsPathRooted(source)
            || (!source.StartsWith(@"\\", StringComparison.Ordinal)
                && Path.GetPathRoot(source)?.Contains(':') != true))
        {
            return false;
        }

        var normalizedSource = Path.GetFullPath(Environment.ExpandEnvironmentVariables(source));

        return roots.Any(root =>
        {
            if (!Path.IsPathRooted(root))
            {
                return false;
            }

            var normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return normalizedSource.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
                || normalizedSource.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        });
    }
}
