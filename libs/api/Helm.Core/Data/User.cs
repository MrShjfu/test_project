namespace Helm.Core.Data;

public class User
{
    public Guid Id { get; init; }
    public required string Email { get; set; }
    public required string CompanyId { get; set; }
    public string[] ModuleRoles { get; set; } = [];    // e.g. "cpq:editor", "*:admin"
}
