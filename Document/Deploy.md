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
| `logs.bat`        | 通过 `wevtutil` 查看 Wes.PrintService 事件日志，默认最近 50 条（可传参条数）。也可直接用 `service.bat logs`。 |
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

REM 更新前备份 / 回滚（查看日志也可用 logs.bat）
Deploy\backup.bat
Deploy\rollback.bat
Deploy\logs.bat 100

REM 打包发布产物（需先完成对应发布）
Deploy\package.bat
```

打包产物位于 `Deploy\publish\`：
- `WesPrint-framework-{version}.zip`：框架依赖版，目标机需已安装 .NET 10 运行时。
- `WesPrint-standalone-{version}.zip`：自包含版，内含运行环境，可直接解压到无 .NET 的机器使用。

二者解压后均含 `install.bat` / `uninstall.bat` / `service.bat`，以管理员身份运行 `install.bat` 即可安装为服务。

> 说明：以上命令需在**仓库根目录**执行（如 `d:\project\KP\Wes.PrintService`）。脚本均在 `Deploy/` 目录下。

## 配置说明（脚本顶部变量）
- `SERVICE_NAME` 服务名（默认 `WesPrintService`）
- `RUNTIME` / `CONFIGURATION` 发布目标，默认 `win-x64` / `Release`
- 事件日志来源在代码 `Program.cs` 中定义为 `Wes.PrintService`

## 查看日志
服务模式无控制台输出，日志写入 **Windows 事件查看器 → 应用程序**，来源为 `Wes.PrintService`。
