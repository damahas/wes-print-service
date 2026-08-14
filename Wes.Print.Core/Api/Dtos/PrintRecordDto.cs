namespace Wes.Print.Core.Api.Dtos;

public class PrintRecordDto
{
    public long Id { get; set; }
    public string Channel { get; set; } = "Api";
    public string Status { get; set; } = "Pending";
    public string? Message { get; set; }
    public string? TemplateKind { get; set; }
    public string? TemplateRef { get; set; }
    public string? PrinterName { get; set; }
    public string? SourceRef { get; set; }
    public string? Request { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PrintRecordQueryDto
{
    public string? Channel { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
