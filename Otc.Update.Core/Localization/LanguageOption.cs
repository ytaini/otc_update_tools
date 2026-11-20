namespace Otc.Update.Core.Localization;

/// <summary>
/// 可选语言项。
/// </summary>
/// <param name="Code">语言区域代码，例如 zh-CN。</param>
/// <param name="DisplayName">语言自有名称，按语言显示时不做翻译。</param>
/// <param name="ShortName">语言短标识，用于紧凑位置展示。</param>
public sealed record LanguageOption(string Code, string DisplayName, string ShortName);
