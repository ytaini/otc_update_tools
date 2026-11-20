using System.IO;

namespace Otc.UpdateTools.SelfCheck;

/// <summary>
/// Otc.UpdateTools 自检入口。
/// </summary>
/// <remarks>
/// 独立控制台程序：数据层自检与本地化守护。
/// 用法：dotnet run --project Otc.UpdateTools.SelfCheck
/// </remarks>
public static class Program
{
    /// <summary>
    /// 程序入口。
    /// </summary>
    /// <returns>检查全部通过返回 0，否则返回失败项数量。</returns>
    public static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var context = new SelfCheckContext();
        var repositoryRoot = LocateRepositoryRoot();

        if (repositoryRoot is null)
        {
            Console.Error.WriteLine("未找到 Otc.UpdateTools 仓库根目录，无法执行源码扫描检查。");
            return 1;
        }

        LocalizationSelfCheck.Run(context, repositoryRoot);
        UpdateProtocolSelfCheck.Run(context);

        Console.WriteLine();
        Console.WriteLine($"自检完成：通过 {context.Passed} 项，失败 {context.Failures.Count} 项。");

        if (context.Failures.Count == 0)
        {
            return 0;
        }

        Console.WriteLine();

        foreach (var failure in context.Failures)
        {
            Console.WriteLine($"  - {failure}");
        }

        return context.Failures.Count;
    }

    /// <summary>
    /// 从自检程序所在位置向上查找仓库根目录。
    /// </summary>
    /// <returns>仓库根目录；未找到时返回 null。</returns>
    private static string? LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Otc.UpdateTools.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
