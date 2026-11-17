using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Linq;
using System.Text;

namespace Otc.Update.Core;

/// <summary>更新包摘要、清单签名与 Authenticode 校验。</summary>
public static class UpdateCryptography
{
    /// <summary>计算文件 SHA-256，输出小写十六进制。</summary>
    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>生成 RSA-3072 密钥对，返回私钥与公钥 PEM。</summary>
    public static (string PrivateKeyPem, string PublicKeyPem) GenerateRsaKeyPair()
    {
        using var rsa = RSA.Create(3072);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }

    /// <summary>使用 RSA 私钥签更新清单。</summary>
    public static string SignManifest(UpdatePackageManifest manifest, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(BuildCanonicalManifest(manifest)),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return Convert.ToBase64String(signature);
    }

    /// <summary>验证更新清单 RSA 签名。</summary>
    public static bool VerifyManifestSignature(
        UpdatePackageManifest manifest,
        string signatureBase64,
        string publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(signatureBase64)
            || string.IsNullOrWhiteSpace(publicKeyPem))
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);

            return rsa.VerifyData(
                Encoding.UTF8.GetBytes(BuildCanonicalManifest(manifest)),
                Convert.FromBase64String(signatureBase64),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// 验证目录内所有 EXE/DLL 的 Authenticode 签名。
    /// </summary>
    /// <param name="directory">待检查目录。</param>
    /// <param name="trustedThumbprints">允许的签名证书指纹；为空时只校验证书链。</param>
    /// <returns>全部文件签名有效时返回 true。</returns>
    public static bool VerifyAuthenticodeDirectory(
        string directory,
        IReadOnlySet<string>? trustedThumbprints = null)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);

            if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!VerifyAuthenticode(file, trustedThumbprints))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>验证单个文件的 Authenticode 签名和证书链。</summary>
    public static bool VerifyAuthenticode(
        string filePath,
        IReadOnlySet<string>? trustedThumbprints = null)
    {
        try
        {
            using var signedCertificate = X509Certificate2.CreateFromSignedFile(filePath);
            using var certificate = new X509Certificate2(signedCertificate);
            using var chain = new X509Chain
            {
                ChainPolicy =
                {
                    RevocationMode = X509RevocationMode.Online,
                    RevocationFlag = X509RevocationFlag.ExcludeRoot,
                    VerificationFlags = X509VerificationFlags.NoFlag,
                },
            };

            if (!chain.Build(certificate))
            {
                return false;
            }

            return trustedThumbprints is null
                || trustedThumbprints.Count == 0
                || trustedThumbprints.Contains(certificate.Thumbprint, StringComparer.OrdinalIgnoreCase);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// 构造确定性清单文本；签名两端使用同一规则，不能改变字段顺序。
    /// </summary>
    public static string BuildCanonicalManifest(UpdatePackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var builder = new StringBuilder();
        builder.Append("schema=").Append(manifest.SchemaVersion).Append('\n');
        builder.Append("product=").Append(manifest.Product).Append('\n');
        builder.Append("type=").Append(manifest.ProductType).Append('\n');
        builder.Append("version=").Append(manifest.Version).Append('\n');
        builder.Append("published=")
            .Append(manifest.PublishedAtUtc.ToUniversalTime().ToString("O"))
            .Append('\n');
        builder.Append("count=").Append(manifest.FileCount).Append('\n');
        builder.Append("size=").Append(manifest.TotalSize).Append('\n');

        foreach (var file in manifest.Files.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            builder.Append(file.Sha256).Append('|')
                .Append(file.Size).Append('|')
                .Append(file.RelativePath.Replace('\\', '/'))
                .Append('\n');
        }

        return builder.ToString();
    }
}
