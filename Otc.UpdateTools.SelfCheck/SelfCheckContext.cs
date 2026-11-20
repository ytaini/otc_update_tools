using System.IO;
using System.Text.RegularExpressions;

namespace Otc.UpdateTools.SelfCheck;

/// <summary>
/// 自检结果收集器。
/// </summary>
internal sealed class SelfCheckContext
{
    private readonly List<string> _failures = new();

    /// <summary>已通过的检查项数。</summary>
    internal int Passed { get; private set; }

    /// <summary>失败项列表。</summary>
    internal IReadOnlyList<string> Failures => _failures;

    /// <summary>
    /// 记录一项检查结果。
    /// </summary>
    /// <param name="name">检查项名称。</param>
    /// <param name="condition">检查是否通过。</param>
    /// <param name="detail">失败时的补充说明。</param>
    internal void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            Passed++;
            Console.WriteLine($"[通过] {name}");
            return;
        }

        _failures.Add(detail is null ? name : $"{name}：{detail}");
        Console.WriteLine($"[失败] {name}{(detail is null ? string.Empty : $"：{detail}")}");
    }

    /// <summary>
    /// 记录一项带值的检查结果。
    /// </summary>
    /// <param name="name">检查项名称。</param>
    /// <param name="condition">检查是否通过。</param>
    /// <param name="actual">实际值。</param>
    /// <param name="expected">期望值。</param>
    internal void CheckEqual(string name, string actual, string expected)
        => Check(name, string.Equals(actual, expected, StringComparison.Ordinal), $"实际 \"{actual}\"，期望 \"{expected}\"");

    /// <summary>
    /// 记录一项带数量的检查结果。
    /// </summary>
    /// <param name="name">检查项名称。</param>
    /// <param name="actual">实际值。</param>
    /// <param name="expected">期望值。</param>
    internal void CheckEqual(string name, int actual, int expected)
        => Check(name, actual == expected, $"实际 {actual}，期望 {expected}");
}

/// <summary>
/// 词条键提取工具。
/// </summary>
internal static class LocalizationKeyScanner
{
    /// <summary>匹配 XAML 中的 <c>{loc:Loc Key=xxx}</c> 写法。</summary>
    internal static readonly Regex XamlKeyPattern = new(
        @"loc:Loc\s+Key\s*=\s*""(?<key>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>匹配代码中的 <c>Loc["xxx"]</c> 写法。</summary>
    internal static readonly Regex IndexerPattern = new(
        @"Loc\s*\[\s*""(?<key>[^""]+)""\s*\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>匹配代码中的 <c>Loc.Format("xxx", ...)</c> 写法。</summary>
    internal static readonly Regex FormatPattern = new(
        @"Loc\.Format\s*\(\s*""(?<key>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
