# Wes.PrintService 部署脚本

`Deploy/` 目录包含将 `Wes.PrintService` 安装/管理为 Windows 服务所需脚本（批处理 `.bat`，需以**管理员**身份运行）。

> **为何必须管理员？** 这些脚本会执行 `sc create` / `sc delete`（注册或删除 Windows 服务）、`net start` / `net stop`（启停服务）、`wevtutil`（读取事件日志）等特权操作。普通用户权限不足以创建/控制服务，脚本会直接失败（install.bat 检测到非管理员会提示并退出）。请以**右键 → 以管理员身份运行**，或在已提升权限的终端中执行。

## 前置要求
- .NET 10 SDK（用于 `dotnet publish`）
- Windows 10 / Server 2016 及以上

## 脚本列表

| 脚本 | 说明 |
|------|------|
| `install.bat`     | 发布项目并注册为 Windows 服务，随后启动。若服务已存在会先卸载再重装。 |
| `uninstall.bat`   | 停止并删除 Windows 服务。支持 `silent` 参数（供 install 内部调用）。 |
| `service.bat`      | 管理服务：`start` / `stop` / `restart` / `status` / `logs`，双击无参数则进入交互菜单（含查看日志）。 |
| `backup.bat`      | 备份当前发布目录到 `Deploy\backups\`，保留最近 5 份。更新前建议先执行。 |
| `rollback.bat`    | 用指定/最新备份覆盖发布目录并重启服务，实现回滚。 |
| `package.bat`     | 将已发布的 `publish-framework` / `publish-standalone` 打包为 `Deploy\publish\WesPrint-framework-{version}.zip`（依赖框架，需 .NET 10）与 `WesPrint-standalone-{version}.zip`（自包含，含运行环境）。版本号从 `Wes.PrintService.csproj` 的 `<FileVersion>` 读取。 |

## 常用命令

```bat
REM 安装并启动（首次或更新部署）
Deploy\install.bat

REM 启停 / 重启 / 查看状态 / 查看日志
Deploy\service.bat start
Deploy\service.bat stop
Deploy\service.bat restart
Deploy\service.bat status
Deploy\service.bat logs 100

REM 卸载
Deploy\uninstall.bat

REM 更新前备份 / 回滚（查看日志用 service.bat logs）
Deploy\backup.bat
Deploy\rollback.bat

REM 打包发布产物（需先完成对应发布）
Deploy\package.bat
```

打包产物位于 `Deploy\publish\`：
- `WesPrint-framework-{version}.zip`：框架依赖版，目标机需已安装 .NET 10 运行时。
- `WesPrint-standalone-{version}.zip`：自包含版，内含运行环境，可直接解压到无 .NET 的机器使用。

二者解压后均含 `Wes.PrintService.exe`、`wwwroot/`、`appsettings.json` 以及 `install.bat` / `uninstall.bat` / `service.bat`（`package.bat` 打包时自动复制进去）。
看日志直接用解压目录下的 `service.bat logs`（`logs.bat` 已移除，功能与 `service.bat logs` 重复）。
以管理员身份运行解压目录下的 `install.bat` 即可安装为服务：`install.bat` 会自动识别同目录的 exe 走「部署包模式」，不会再触发 `dotnet publish`；在仓库 `Deploy\` 下运行时仍保持「先发布再安装」。

> 说明：以上命令需在**仓库根目录**执行（如 `d:\project\KP\Wes.PrintService`）。脚本均在 `Deploy/` 目录下。

## 最小体积发布（含运行环境）

发布配置：`Wes.PrintService/Properties/PublishProfiles/Standalone-Min-win-x64.pubxml`
（自包含 win-x64 + 单文件 + 单文件压缩 + 剔除 PDB + 卫星资源只留英文）

```bat
REM 自包含最小包：含 .NET 10 运行时，产物只有一个 exe + wwwroot（约 63MB）
dotnet publish Wes.PrintService -p:PublishProfile=Standalone-Min-win-x64

REM 框架依赖版：不含运行时，目标机需装 .NET 10（约 30MB）
dotnet publish Wes.PrintService -p:PublishProfile=Standalone-Min-win-x64 -p:SelfContained=false -p:EnableCompressionInSingleFile=false

REM 打 zip
Deploy\package.bat
```

实测体积（win-x64 / Release）：

| 发布方式 | 体积 | 备注 |
|----------|------|------|
| 默认自包含（多文件，含 PDB） | 215 MB | 其中 `libSkiaSharp.pdb` 就占 80MB |
| 去 PDB（多文件） | 135 MB | |
| **本配置（单文件 + 压缩）** | **63 MB** | 单个 `Wes.PrintService.exe`，已冒烟验证可用 |
| 框架依赖（不含运行时） | 30 MB | 目标机须预装 .NET 10 运行时 |
| 再加 `PublishTrimmed`（裁剪） | 26~30 MB | **禁止**：EF Core/MVC 依赖反射，实测启动即 `MissingMethodException`，`TrimMode=partial` 时接口全 500 |

> 注意：`EnableCompressionInSingleFile`（压缩）与 `PublishTrimmed`（裁剪）都只对**自包含**生效，
> 框架依赖发布时必须加 `-p:SelfContained=false -p:EnableCompressionInSingleFile=false -p:PublishTrimmed=false`。
> 压缩与 `PublishReadyToRun` 互斥，本配置已显式关闭 R2R。

## 配置说明（脚本顶部变量）
- `SERVICE_NAME` 服务名（默认 `WesPrintService`）
- `RUNTIME` / `CONFIGURATION` 发布目标，默认 `win-x64` / `Release`
- 事件日志来源在代码 `Program.cs` 中定义为 `Wes.PrintService`

## 查看日志
服务模式无控制台输出，日志写入 **Windows 事件查看器 → 应用程序**，来源为 `Wes.PrintService`。
