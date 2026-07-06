namespace Helm.Core.Data;

public class Company
{
    public required string Id { get; init; }          // "north" | "quantum" | "doyle" | "ntg" | licensee ids
    public required string Name { get; set; }
    public string? ParentCompanyId { get; set; }       // licensee hierarchy (ADR-003)
}
