# Wes.PrintService 对外 API 文档

本服务对外暴露一组 RESTful 接口，供外部系统（如 WMS / ERP）**直接提交打印任务**，并查询任务结果。
所有接口基于 **HTTP + JSON**，默认监听端口 **`8809`**，基础地址 `http://<host>:8809`。

> 当前对外接口**未启用鉴权**。若需安全访问，请在前置网关或后续中间件中加 API Key / Token 校验。

---

## 0. 通用约定

| 项 | 说明 |
|----|------|
| 协议 | HTTP/1.1，Content-Type 必须为 `application/json` |
| 字符集 | UTF-8 |
| 请求体 | JSON（成员命名采用 camelCase，与 .NET 默认序列化一致） |
| 成功响应 | `200 OK`，Body 为 JSON |
| 客户端错误 | `400 Bad Request`（参数缺失/非法）/ `404 Not Found`（记录不存在） |
| 时间字段 | UTC 时间，ISO-8601 格式（如 `2026-08-14T06:12:33.123Z`） |

## 1. 对外打印接口总览（`ExternalApiController`，前缀 `/api/external`）

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/external/print` | 提交一次打印任务（同步执行 + 落库），返回记录 Id 与状态 |
| GET  | `/api/external/print/{id:long}` | 按记录 Id 查询打印任务结果 |

---

## 2. 提交打印任务

提交后立即**同步执行打印**并写入打印记录，返回本次记录 Id 与执行结果。打印机由服务端从 MQ 配置的"目标打印机"中取，**不接收外部传入的打印机名**。

### 2.1 请求

```
POST /api/external/print
Content-Type: application/json
```

**请求体（SubmitPrintJobDto）字段：**

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|------|------|------|--------|------|
| `templateKind` | string | 否 | `"T"` | 模板来源类型：`T`=服务端模板名 / `TS`=模板内容 / `FL`=文件下载链接 |
| `templateRef` | string | **是** | — | 与 `templateKind` 对应的模板引用/内容（见下表） |
| `fields` | `List<Dictionary<string,string>>` | 否 | 空列表 | 打印数据源（key=value 字段字典的列表，可多行） |
| `sourceRef` | string | 否 | — | 调用方业务单号等，用于记录溯源（为空则用消息标识） |

**`templateRef` 与 `templateKind` 的对应关系：**

| templateKind | templateRef 含义 | 示例 |
|--------------|------------------|------|
| `T`  | 服务端 `PrintTemp` 目录下的 `.frx` 模板文件名（可不含扩展名） | `"label_product"` |
| `TS` | FastReport `.frx` 模板内容原文（文本） | `"<Report>...</Report>"` |
| `FL` | 模板文件下载链接（http/https，当前支持 `.frx`） | `"https://host/tpl/label.frx"` |

### 2.2 响应

**成功** `200 OK`：

```json
{
  "recordId": 1024,
  "status": "Success",
  "message": "打印完成",
  "printerName": "ZD888"
}
```

**失败** `400 Bad Request`（打印执行异常或参数错误）：

```json
{
  "recordId": 1025,
  "status": "Failed",
  "message": "打印机 ZD888 不存在",
  "printerName": "ZD888"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `recordId` | long | 本次落库的打印记录 Id |
| `status` | string | `Success` / `Failed` |
| `message` | string | 结果或错误信息 |
| `printerName` | string | 实际使用的打印机名称 |

> 注意：`templateRef` 为空会直接返回 `400 { "error": "TemplateRef 不能为空（模板名 / 模板内容 / 文件链接）" }`；请求体为空返回 `400 { "error": "请求体不能为空" }`。

### 2.3 示例

```bash
curl -X POST http://localhost:8809/api/external/print \
  -H "Content-Type: application/json" \
  -d '{
    "templateKind": "T",
    "templateRef": "label_product",
    "fields": [
      { "productCode": "P-1001", "productName": "示例产品", "qty": "12" }
    ],
    "sourceRef": "WO-20260814-001"
  }'
```

---

## 3. 查询打印任务结果

按记录 Id 查询某次提交的结果（状态/信息/打印机）。

### 3.1 请求

```
GET /api/external/print/{id:long}
```

### 3.2 响应

**成功** `200 OK`：

```json
{
  "recordId": 1024,
  "status": "Success",
  "message": "打印完成",
  "printerName": "ZD888"
}
```

**不存在** `404 Not Found`：

```json
{ "error": "未找到该打印记录" }
```

---

## 4. 统一打印消息结构（与 MQ 消息一致）

对外 API 的请求字段与内部 `PrintMessage` 结构保持一致，因此对接方也可直接复用同一套报文结构投递 MQ。

```
PrintMessage {
  templateKind : string            // T / TS / FL
  templateRef  : string            // 模板名 / 模板内容 / 文件链接
  fields       : List<Dictionary<string,string>>  // 数据源（注册为报表数据源 "PrintData"）
  messageId    : string?           // 来源标识（对外 API 中由 sourceRef 映射）
}
```

- `fields` 在引擎中会被合并为一张 `DataTable`，并以名称 **`PrintData`** 注册为报表数据源，供 `.frx` 模板绑定字段。
- 多行数据：列表长度 > 1 时，报表可按多行/多页渲染。

---

## 5. 关联说明

- **打印机选择**：对外 API 的打印机完全由服务端 MQ 配置（`default` 配置项中的"目标打印机"）决定，调用方无需也无法指定。要修改目标打印机，请通过管理后台 API 更新 MQ 配置。
- **记录保留**：所有打印记录默认保留 **30 天**，超过保留天数的记录由后台定时任务（每 24h）自动清理。保留天数可通过 `record.retention-days` 设置项调整。
- **记录查看**：每次请求的 `fields` 参数会以 JSON 形式存入记录的 `request` 字段，可通过管理后台「打印记录」页查看（或通过 `GET /api/records/{id}` 获取）。

---

## 6. 常见错误码

| HTTP 状态 | 含义 | 处理建议 |
|-----------|------|----------|
| 400 | 参数缺失/非法，或打印执行失败 | 检查 `templateRef` 是否非空；查看 `message` 中的具体错误（如打印机不存在、模板不存在） |
| 404 | 记录不存在 | 确认 `id` 是否正确、记录是否已被保留策略清理 |
| 500 | 服务端未预期异常 | 查看服务端日志 / `WesPrint.db` 状态 |

---

## 7. 对接检查清单

1. 服务已启动且可访问 `GET /health`（返回 `{"status":"ok"}`）。
2. 管理后台已将 MQ 配置中的「目标打印机」设为期望打印机（对外 API 依赖此配置）。
3. 模板文件已放置于服务端 `PrintTemp` 目录（当 `templateKind=T`），或提供可访问的 `FL` 下载链接 / 直接传 `TS` 内容。
4. `fields` 字段名与 `.frx` 模板中绑定的数据列名一致（数据源名为 `PrintData`）。
