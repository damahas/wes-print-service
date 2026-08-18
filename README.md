# Wes.PrintService

Windows 打印服务：将只能跑在 Windows 上的打印机驱动，封装为 **HTTP 管理后台 + RESTful API + MQ 消费**（端口 **8809**），让任意业务系统在网络可达时远程驱动本地硬件出纸。数据存本地 SQLite。

**适用场景**：打印机仅 USB/并口直连无法联网、业务系统（WMS/ERP）远端部署、或仅需单台 Windows 主机落地打印。

---

## 核心能力

- **打印引擎**：[SkiaSharp](https://github.com/mono/SkiaSharp) 渲染 JSON 模板，支持小标签（如 8×5cm）与 A4，纯 .NET 无 GDI+ 依赖。
- **三种提交方式**：① 管理后台直接提交；② 对外 API `/api/external/print`；③ MQ 消费。
- **消息队列**：RabbitMQ 与 Kafka **双通道独立**，可同时开启，各配独立连接与开关。
- **打印机设置**：在管理后台顶部常驻下拉「选择打印机」，独立于 MQ 配置。
- **打印记录**：每次任务落库 `PrintRecord`，超时自动清理（默认 30 天，`record.retention-days` 可配）。

---

## 项目结构

```
Wes.PrintService.slnx
├── Wes.Print.Core/                  # 类库：全部业务逻辑
│   ├── Storage/                     # SQLite（EF Core）：MqConfig / PrintRecord / AppSetting
│   ├── Api/                         # RESTful API + 管理后台前端(Admin/wwwroot)
│   │   └── Controllers/
│   │       ├── PrintServiceController.cs  # /api/mq/* /api/printers /api/records /api/printer/default /api/settings/* /health
│   │       └── ExternalApiController.cs   # /api/external/print
│   ├── Messaging/                   # MQ 抽象：IPrintMessageConsumer + ConsumerFactory
│   │   ├── RabbitMq/RabbitMqConsumer.cs   # 已实现
│   │   └── Kafka/KafkaConsumer.cs         # 已实现
│   └── Print/                       # SkiaPrintEngine / BarcodeRenderer / Template / PrintJobExecutor
├── Wes.PrintService/                # 宿主：Windows 服务 + Web 自托管（端口 8809）
└── Deploy/                          # 部署脚本(install/uninstall/service/backup/rollback/logs/package)
```

---

## 快速使用

1. **安装服务**：以**管理员身份**运行 `Deploy\install.bat`（右键 → 以管理员身份运行），服务启动后访问 `http://127.0.0.1:8809/`。
2. **选打印机**：管理后台顶部下拉选择本地已安装打印机（对外 API 默认取此项）。
3. **配模板**：JSON 模板放服务端 `PrintTemp` 目录，或请求时直接传入/传链接；支持 `{{字段}}` 占位、text/barcode(QR、CODE128)/line/image。
4. **提交打印**：
   - 外部系统：`POST /api/external/print`（详见 [对外 API 文档](Document/ExternalApi.md)）。
   - MQ：后台开启 RabbitMQ / Kafka 通道并填连接，业务系统投递 `PrintMessage`。
5. **看结果**：后台「打印记录」页查状态与错误。
6. **更新服务**：停服务（管理员运行 `Deploy\service.bat stop`，或直接 `net stop WesPrintService`）→ 重新发布（`Deploy\install.bat` 会自动先卸载再重装；或仅替换发布目录 exe 后 `Deploy\service.bat restart`）→ 数据存于 SQLite，更新不丢失，必要时用 `Deploy\rollback.bat` 回滚。

---

## 部署

`Deploy/` 目录下含 7 个脚本：`install.bat`（发布+注册服务）、`uninstall.bat`、`service.bat`（互动管理服务：start/stop/restart/status/logs）、`backup.bat`、`rollback.bat`、`logs.bat`（查看事件日志）、`package.bat`（打包发布产物为 zip）。

> **重要：所有 `.bat` 必须以管理员身份运行。**
> 以普通用户双击会失败（服务创建/删除、端口绑定、停止他人进程等操作都需要管理员权限）。
> 正确做法：右键脚本 → **“以管理员身份运行”**；或在已提升权限的终端中执行。install.bat 内部若检测到非管理员会明确提示并退出。
>
> 发布包（如 `publish-framework/`、`publish-standalone/`）里自带 `install.bat`、`uninstall.bat`、`service.bat` 三个脚本，部署到目标机器后同样需以管理员身份运行来安装/管理。

---

## 本地构建

```powershell
dotnet build Wes.PrintService.slnx -c Debug
cd Wes.PrintService/bin/Debug/net10.0-windows
dotnet Wes.PrintService.dll
# 管理后台：http://localhost:8809/   健康检查：http://localhost:8809/health
```

> 运行 WorkDir 固定为 `Wes.PrintService` 目录，确保读取正确的 `WesPrint.db`。
