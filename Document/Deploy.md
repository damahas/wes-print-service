# Wes.PrintService 部署脚本

`Deploy/` 目录包含将 `Wes.PrintService` 安装/管理为 Windows 服务所需脚本（批处理 `.bat`，需以**管理员**身份运行）。

## 前置要求
- .NET 10 SDK（用于 `dotnet publish`）
- Windows 10 / Server 2016 及以上

## 脚本列表

| 脚本 | 说明 |
|------|------|
| `install.bat`     | 发布项目并注册为 Windows 服务，随后启动。若服务已存在会先卸载再重装。 |
| `uninstall.bat`   | 停止并删除 Windows 服务。支持 `silent` 参数（供 install 内部调用）。 |
| `start-stop.bat`  | 管理服务：`start` / `stop` / `restart` / `status`。 |
| `backup.bat`      | 备份当前发布目录到 `deploy\backups\`，保留最近 5 份。更新前建议先执行。 |
| `rollback.bat`    | 用指定/最新备份覆盖发布目录并重启服务，实现回滚。 |
| `view-logs.bat`   | 通过 `wevtutil` 查看 Wes.PrintService 事件日志，默认最近 50 条（可传参条数）。 |

## 常用命令

```bat
REM 安装并启动（首次或更新部署）
Deploy\install.bat

REM 启停 / 重启 / 查看状态
Deploy\start-stop.bat start
Deploy\start-stop.bat stop
Deploy\start-stop.bat restart
Deploy\start-stop.bat status

REM 卸载
Deploy\uninstall.bat

REM 更新前备份 / 回滚 / 查看日志
Deploy\backup.bat
Deploy\rollback.bat
Deploy\view-logs.bat 100
```

> 说明：以上命令需在**仓库根目录**执行（如 `d:\project\KP\Wes.PrintService`）。脚本均在 `Deploy/` 目录下。

## 配置说明（脚本顶部变量）
- `SERVICE_NAME` 服务名（默认 `WesPrintService`）
- `RUNTIME` / `CONFIGURATION` 发布目标，默认 `win-x64` / `Release`
- 事件日志来源在代码 `Program.cs` 中定义为 `Wes.PrintService`

## 查看日志
服务模式无控制台输出，日志写入 **Windows 事件查看器 → 应用程序**，来源为 `Wes.PrintService`。
