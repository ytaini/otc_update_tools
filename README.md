# OTC Update Tools

OTC 系列软件共用的更新基础设施，包含：

- `Otc.Update.Core`：更新包、签名、下载、备份、回滚与运行时文件协议。
- `Otc.Publisher`：独立发布工具，产品编码、系统类型和版本均通过参数提供。
- `Otc.Updater`：独立更新器，由目标程序带参数启动，不依赖具体产品程序集。

CC_WPF 与 WMS_WPF 通过 Git 子模块引用本仓库，不再保存 Publisher、Updater 或 Update Core 的副本。

## 构建

```powershell
dotnet build Otc.UpdateTools.slnx --no-incremental
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
  --private-key <private.pem>
```

`Otc.Publisher` 与 `Otc.Updater` 不再包含任何针对 CC_WPF 或 WMS_WPF 的分支代码。

手工启动 Publisher 图形界面时，首次默认值可分别通过
`OTC_PUBLISHER_PRODUCT` 与 `OTC_PUBLISHER_TYPE` 环境变量提供；命令行模式则始终以
`--product` 和 `--type` 参数为准。
