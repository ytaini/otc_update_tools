using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Otc.Update.Core.Localization;

/// <summary>
/// XAML 本地化文本标记扩展。
/// </summary>
/// <remarks>
/// 用法：<c>Text="{loc:Loc Key=Publisher.Title}"</c>。
/// 返回绑定到本地化服务索引器的动态绑定，语言切换时由服务通知 WPF 刷新文本。
/// </remarks>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    /// <summary>
    /// 初始化本地化标记扩展。
    /// </summary>
    public LocExtension()
    {
    }

    /// <summary>
    /// 使用资源键初始化本地化标记扩展。
    /// </summary>
    /// <param name="key">本地化资源键。</param>
    public LocExtension(string key)
    {
        Key = key;
    }

    /// <summary>
    /// 本地化资源键。
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 生成绑定到当前语言词条的表达式。
    /// </summary>
    /// <param name="serviceProvider">XAML 服务提供者。</param>
    /// <returns>本地化文本绑定表达式。</returns>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // 设计器无法稳定解析动态 Binding 标记扩展，预览时直接返回当前词条文本。
        if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
        {
            return LocalizationService.Instance[Key];
        }

        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
        };

        return binding.ProvideValue(serviceProvider);
    }
}
