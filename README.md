# OTC Update Tools

OTC 系列软件共用的更新基础设施，包含：

- `Otc.Update.Core`：更新包、签名、下载、备份、回滚与运行时文件协议，并承载 Publisher 与 Updater 共用的三语本地化服务。
- `Otc.Publisher`：独立发布工具，产品编码、系统类型和版本均通过参数提供。
- `Otc.Updater`：独立更新器，由目标程序带参数启动，不依赖具体产品程序集。
- `Otc.UpdateTools.SelfCheck`：独立自检控制台程序，守护三语词条一致性与更新参数协议。

WCS_WPF 与 WMS_WPF 各自保留客户端更新核心源码副本；本仓库独立维护 Publisher、Updater、三语词条与更新协议自检，部署时按发布流程提供对应工具。

## 构建

```powershell
dotnet build Otc.UpdateTools.slnx --no-incremental
```

## 自检

```powershell
dotnet run --project Otc.UpdateTools.SelfCheck
```

当前基线：**166 项通过、0 项失败**。

自检覆盖：三语词条键集合一致、无漏译、XAML 与源码引用的词条键全部存在、
语言切换与区域文化同步、更新器启动参数携带语言并原样解析回传、
发布命令行解析 `--language`。

## 三语支持

Publisher 与 Updater 均支持 **简体中文（zh-CN）/ English（en-US）/ 한국어（ko-KR）**。

### 词条来源

- 词条位于 `Otc.Update.Core/Localization/Resources/{语言}.json`，以**嵌入资源**随程序集发布。
  工具被复制到现场目录后仍能正确显示当前语言，不会因缺少词条文件回退成键名。
- 三份 JSON 的键集合必须完全一致；不一致由 `Otc.UpdateTools.SelfCheck` 拦截。
- 开发期可用环境变量 `OTC_UPDATE_LOCALIZATION_DIR` 指向外部目录覆盖词条。

### 语言选择

| 场景 | 优先级 |
| --- | --- |
| Publisher 图形界面 | 页头语言下拉框（选择结果写入 `%LOCALAPPDATA%\OTC\Otc.Publisher\publisher-language.json`）→ 本机语言偏好 → 系统界面语言 |
| Publisher 命令行 | `--language` → 环境变量 `OTC_PUBLISHER_LANGUAGE` → 系统界面语言 |
| Updater | 主程序传入的 `--language` → 系统界面语言 |

页头语言下拉框切换后，界面文本、状态栏与页脚提示即时刷新，无需重启。

### XAML 用法

跨程序集引用标记扩展必须使用 XAML 命名空间映射，不能写 `clr-namespace:`（只在当前程序集内查找）：

```xml
<Window xmlns:loc="http://schemas.otc.com/localization">
    <TextBlock Text="{loc:Loc Key=Publisher.HeaderTitle}" />
</Window>
```

代码内取词：

```csharp
LocalizationService.Instance["Publisher.Status.Success"];
LocalizationService.Instance.Format("Publisher.Log.Start", productCode, version, scope);
```

## 命令行发布

```powershell
dotnet run --project Otc.Publisher -- `
  --source <published-directory> `
  --updater-dir <updater-output-directory> `
  --product <product-code> `
  --type <WCS|WMS|WCM> `
  --version <version> `
  --share <UNC-root-or-publish-directory> `
  --connection <postgres-connection> `
  --private-key <private.pem> `
  --language <zh-CN|en-US|ko-KR>
```

`Otc.Publisher` 与 `Otc.Updater` 不再包含任何针对 WCS_WPF 或 WMS_WPF 的分支代码。

手工启动 Publisher 图形界面时，首次默认值可分别通过
`OTC_PUBLISHER_PRODUCT` 与 `OTC_PUBLISHER_TYPE` 环境变量提供；命令行模式则始终以
`--product` 和 `--type` 参数为准。

`publisher-cli-result.log` 是给发布脚本消费的机器可读结果文件，键名固定为英文
（`Published` / `Package:` / `SHA-256:` / `Size:`），不随界面语言变化。

## 与主程序的联动

主程序（WCS_WPF / WMS_WPF）启动更新器时，通过 `UpdateLaunchOptions.Language`
把当前界面语言带给更新器，更新窗口因此与主程序保持同一语言。
主程序侧的同名协议位于各自的 `Services/Update/Core` 独立副本，
改动更新参数协议时三处需要同步。
