# Wes.PrintService 打印模板设计文档

本文件描述 **JSON 打印模板** 的结构。AI 或开发人员可直接按本文档生成模板 JSON，
经以下任一方式交付即可被服务端引擎（SkiaSharp 纯 .NET 渲染）正确解析并打印：

- **T 模式**：把 `.json` 文件放到服务端 `PrintTemp/` 目录，调用时传文件名。
- **TS 模式**：把本文档定义的 JSON 文本直接作为 `templateRef` 提交。
- **FL 模式**：把 `.json` 文件放到可访问的 http(s) 链接。

> 本文档字段名为**服务端权威字段名**（camelCase，与后端 `PrintTemplate` 模型一致）。
> 经管理后台「打印模板」设计器保存的草稿使用另一套字段名（见文末"字段兼容说明"），
> **推荐 AI 直接生成时采用本文档字段名**，可绕过设计器直接落地。

---

## 1. 顶层结构

```json
{
  "page": { /* 页面设置 */ },
  "items": [ /* 元素数组，按渲染顺序叠放 */ ]
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `page` | object | 是 | 纸张尺寸、单位、DPI、背景色 |
| `items` | array  | 是 | 元素列表，至少 0 个；常见 text / barcode / line / image |

---

## 2. page 页面设置

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|------|------|------|--------|------|
| `width`  | number | 是 | 80  | 纸张宽度（单位见 `unit`） |
| `height` | number | 是 | 50  | 纸张高度（单位见 `unit`） |
| `unit`   | string | 否 | `"mm"` | 单位：`mm` / `px` / `cm` |
| `dpi`    | number | 否 | 300 | 渲染 DPI，标签建议 203~300，保证条码/小字清晰 |
| `background` | string | 否 | `"#FFFFFF"` | 背景色（十六进制） |

常用尺寸：
- 小标签 `80×50`（mm），`210×297`（mm）即 A4。
- 单位 `mm` 下字体大小常见 `3~12`，线宽 `0.2~0.5`。

---

## 3. items 公共字段（所有元素共有）

每个元素对象**必须含 `type`**，定位与尺寸字段如下（单位与 `page.unit` 一致）：

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `type` | string | 是 | `text` / `barcode` / `line` / `image` |
| `x` | number | 是 | 左上角 X 坐标 |
| `y` | number | 是 | 左上角 Y 坐标 |
| `w` | number | 是 | 元素宽度（line 可省，由 `x2` 决定） |
| `h` | number | 是 | 元素高度（line 可省，由 `y2` 决定） |

---

## 4. 元素类型详解

### 4.1 text（文本）

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|------|------|------|--------|------|
| `text`     | string | 是 | `""` | 文本内容，支持 `{{field}}` 变量 |
| `font`     | number | 否 | 4   | 字体大小（单位同 page.unit） |
| `bold`     | bool   | 否 | false | 是否加粗 |
| `fontFamily` | string | 否 | `"Arial"` | 字体名 |
| `color`    | string | 否 | `"#000000"` | 文字颜色 |
| `align`    | string | 否 | `"left"` | 水平对齐：`left` / `center` / `right` |
| `valign`   | string | 否 | `"top"`  | 垂直对齐：`top` / `middle` / `bottom` |
| `wrap`     | bool   | 否 | true | 是否自动换行 |

```json
{ "type":"text", "x":5, "y":5, "w":70, "h":8,
  "text":"SKU: {{sku}}", "font":4, "bold":true,
  "fontFamily":"Arial", "color":"#000000", "align":"left", "valign":"top", "wrap":true }
```

### 4.2 barcode（条码）

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|------|------|------|--------|------|
| `value`      | string | 是 | `""` | 条码内容，支持 `{{field}}` |
| `symbology`  | string | 否 | `"QR"` | `QR` / `CODE128`（亦兼容 `CODE_128` / `C128`），未知按 QR 兜底 |
| `foreground` | string | 否 | `"#000000"` | 前景色 |
| `background` | string | 否 | `"#FFFFFF"` | 背景色 |
| `showText`   | bool   | 否 | false | 是否在条码下方显示可读文本 |

```json
{ "type":"barcode", "x":5, "y":20, "w":40, "h":15,
  "value":"{{code}}", "symbology":"CODE128",
  "foreground":"#000000", "background":"#FFFFFF", "showText":true }
```

### 4.3 line（直线）

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|------|------|------|--------|------|
| `x2`   | number | 否 | `x + w` | 终点 X（单位同 page.unit） |
| `y2`   | number | 否 | `y + h` | 终点 Y |
| `width`| number | 否 | 0.3 | 线宽 |
| `color`| string | 否 | `"#000000"` | 颜色 |

> 终点未填时，用 `x + w` / `y + h` 计算。

```json
{ "type":"line", "x":5, "y":40, "x2":75, "y2":40, "width":0.3, "color":"#000000" }
```

### 4.4 image（图片）

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|------|------|------|--------|------|
| `src` | string | 是 | `""` | 图片来源，支持 `{{field}}`；可为 http(s) URL 或 base64 data URI |

```json
{ "type":"image", "x":45, "y":5, "w":30, "h":30, "src":"{{logoUrl}}" }
```

---

## 5. 变量占位符

文本/条码/图片的"内容类"字段支持 `{{field}}`：
- `{{字段名}}` 在打印时由数据源 `fields` 中同名字段替换（大小写不敏感匹配）。
- 未匹配到时保留原样 `{{字段名}}` 输出（便于排查）。
- 数据源字段名即对接方 `fields` 中的 key。

---

## 6. 完整示例

### 6.1 小标签（80×50mm，含文本 + 条码）

```json
{
  "page": { "width": 80, "height": 50, "unit": "mm", "dpi": 300, "background": "#FFFFFF" },
  "items": [
    { "type":"text", "x":4, "y":3, "w":72, "h":6, "text":"品名: {{productName}}", "font":4, "bold":true },
    { "type":"text", "x":4, "y":11, "w":72, "h":5, "text":"编码: {{productCode}}", "font":3.5 },
    { "type":"text", "x":4, "y":17, "w":36, "h":5, "text":"数量: {{qty}}", "font":3.5 },
    { "type":"barcode", "x":4, "y":24, "w":72, "h":18, "value":"{{productCode}}", "symbology":"CODE128", "showText":true },
    { "type":"line", "x":4, "y":22, "x2":76, "y2":22, "width":0.3 }
  ]
}
```

### 6.2 A4（210×297mm，含标题 + 表格化文本 + 二维码）

```json
{
  "page": { "width": 210, "height": 297, "unit": "mm", "dpi": 300, "background": "#FFFFFF" },
  "items": [
    { "type":"text", "x":15, "y":15, "w":180, "h":12, "text":"出库单 {{orderNo}}", "font":10, "bold":true, "align":"center" },
    { "type":"line", "x":15, "y":32, "x2":195, "y2":32, "width":0.4 },
    { "type":"text", "x":15, "y":38, "w":90, "h":6, "text":"客户: {{customer}}", "font":4 },
    { "type":"text", "x":110, "y":38, "w":90, "h":6, "text":"仓库: {{warehouse}}", "font":4 },
    { "type":"text", "x":15, "y":48, "w":180, "h":120, "text":"{{detail}}", "font":3.5, "wrap":true },
    { "type":"barcode", "x":140, "y":235, "w":55, "h":55, "value":"{{orderNo}}", "symbology":"QR", "showText":false }
  ]
}
```

---

## 7. 坐标与单位约定

- 坐标原点 `(0,0)` 在纸张**左上角**，X 向右、Y 向下，单位与 `page.unit` 一致。
- 元素 `w`/`h` 为外框尺寸；`text` 文字在框内按 `align`/`valign` 对齐。
- 所有尺寸换算到像素时统一用 `page.dpi`：像素 = 物理量 × (dpi / 25.4)（mm）。
- 元素按 `items` 数组顺序**从下往上叠放**，靠后的元素覆盖在前。

---

## 8. 字段兼容说明（重要）

管理后台「打印模板」设计器在浏览器内使用一套**草稿字段名**（仅前端编辑态专用）：

| 服务端字段（本文档） | 设计器草稿字段 |
|----------------------|----------------|
| `w` / `h` | `width` / `height` |
| `font` | `fontSize` |
| `value`（条码） | `code` |
| `symbology` | `barcodeType` |
| `x2` / `y2`（直线） | `endX` / `endY` |
| `width`（直线线宽） | `weight` |
| `src`（图片） | `path` / `embedBase64` |
| `page.background` | `page.backgroundColor` |

**结论**：AI 直接生成模板时请严格使用本文档（服务端）字段名；若需经设计器编辑，请在保存时由后端/中间层完成草稿字段 → 服务端字段的映射。服务端引擎只识别本文档定义的字段名。
