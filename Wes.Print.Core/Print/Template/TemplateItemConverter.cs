using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wes.Print.Core.Print.Template;

/// <summary>
/// 自定义多态转换器：依据元素中的 "type" 字段（text/barcode/line/image）实例化对应子类。
/// 避免 System.Text.Json 默认的 $type 鉴别器与业务模板字段冲突。
/// </summary>
public class TemplateItemConverter : JsonConverter<TemplateItem>
{
    public override TemplateItem? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("TemplateItem 期望一个 JSON 对象。");

        // 拷贝一份 reader 以窥探 type 字段
        var doc = JsonDocument.ParseValue(ref reader);
        if (!doc.RootElement.TryGetProperty("type", out var typeElem))
            throw new JsonException("TemplateItem 缺少 type 字段。");
        var type = typeElem.GetString()?.Trim().ToLowerInvariant();

        TemplateItem item = type switch
        {
            "text" => new TextItem(),
            "barcode" => new BarcodeItem(),
            "line" => new LineItem(),
            "image" => new ImageItem(),
            _ => new TextItem(), // 未知类型兜底为文本
        };

        // 用泛型反序列化填充子类字段（使用不含本转换器的 options 避免递归）
        var innerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var json = doc.RootElement.GetRawText();
        return JsonSerializer.Deserialize(json, item.GetType(), innerOptions) as TemplateItem;
    }

    public override void Write(Utf8JsonWriter writer, TemplateItem value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
