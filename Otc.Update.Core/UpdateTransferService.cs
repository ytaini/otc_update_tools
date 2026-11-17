using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Otc.Update.Core;

/// <summary>支持本地/UNC、HTTPS、断点续传和失败重试的更新包传输器。</summary>
public sealed class UpdateTransferService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    /// <summary>
    /// 下载或复制完整更新包到暂存目录。
    /// </summary>
    public async Task<string> PrepareAsync(
        string source,
        string destinationPath,
        long expectedSize,
        int retryCount,
        TimeSpan retryDelay,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var attempts = Math.Max(retryCount, 1);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    await DownloadHttpAsync(
                        uri,
                        destinationPath,
                        expectedSize,
                        progress,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await CopyLocalAsync(
                        source,
                        destinationPath,
                        expectedSize,
                        progress,
                        cancellationToken).ConfigureAwait(false);
                }

                return destinationPath;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;

                if (attempt < attempts)
                {
                    await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw new IOException($"更新包传输失败，已重试 {attempts} 次", lastError);
    }

    private static async Task DownloadHttpAsync(
        Uri source,
        string destinationPath,
        long expectedSize,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var partialPath = destinationPath + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;

        if (expectedSize > 0 && existingLength > expectedSize)
        {
            File.Delete(partialPath);
            existingLength = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source);

        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (existingLength > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            File.Delete(partialPath);
            existingLength = 0;
        }
        else if (existingLength > 0 && response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new IOException($"服务器不支持断点续传：HTTP {(int)response.StatusCode}");
        }
        else if (response.StatusCode is not HttpStatusCode.OK and not HttpStatusCode.PartialContent)
        {
            response.EnsureSuccessStatusCode();
        }

        var totalBytes = expectedSize > 0
            ? expectedSize
            : response.Content.Headers.ContentLength.GetValueOrDefault() + existingLength;

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(
            partialPath,
            existingLength > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        var buffer = new byte[81920];
        var received = existingLength;

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(new UpdateDownloadProgress
            {
                BytesReceived = received,
                TotalBytes = totalBytes,
            });
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Close();

        if (expectedSize > 0 && new FileInfo(partialPath).Length != expectedSize)
        {
            throw new IOException("下载后的更新包大小不正确");
        }

        File.Move(partialPath, destinationPath, true);
    }

    private static async Task CopyLocalAsync(
        string source,
        string destinationPath,
        long expectedSize,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var partialPath = destinationPath + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        var sourceInfo = new FileInfo(source);

        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException("更新包来源不存在", source);
        }

        var expected = expectedSize > 0 ? expectedSize : sourceInfo.Length;
        var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;

        if (existingLength > expected)
        {
            File.Delete(partialPath);
            existingLength = 0;
        }

        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        input.Seek(existingLength, SeekOrigin.Begin);

        await using var output = new FileStream(
            partialPath,
            existingLength > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        var buffer = new byte[81920];
        var received = existingLength;

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(new UpdateDownloadProgress
            {
                BytesReceived = received,
                TotalBytes = expected,
            });
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Close();

        if (new FileInfo(partialPath).Length != expected)
        {
            throw new IOException("复制后的更新包大小不正确");
        }

        File.Move(partialPath, destinationPath, true);
    }
}
