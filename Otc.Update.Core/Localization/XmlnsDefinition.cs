using System.Windows.Markup;

// 把本地化标记扩展所在的命名空间映射为稳定的 XAML 命名空间 URI。
// Publisher 与 Updater 都通过 ProjectReference 引用 Otc.Update.Core；
// 直接写 clr-namespace: 只会到当前程序集里查找，跨程序集必须声明映射关系。
// 属性参数必须是编译期常量，这里与 UpdateXmlns.Namespace 保持一致。
[assembly: XmlnsDefinition("http://schemas.otc.com/localization", "Otc.Update.Core.Localization")]
[assembly: XmlnsPrefix("http://schemas.otc.com/localization", "loc")]

namespace Otc.Update.Core.Localization;

/// <summary>
/// 本地化 XAML 命名空间常量。
/// </summary>
public static class UpdateXmlns
{
    /// <summary>本地化标记扩展的 XAML 命名空间 URI。</summary>
    public const string Namespace = "http://schemas.otc.com/localization";
}
