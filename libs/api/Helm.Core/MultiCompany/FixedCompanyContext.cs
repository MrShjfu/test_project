namespace Helm.Core.MultiCompany;

/// <summary>
/// ICompanyContext for background work with no ambient HTTP request (ADR-003: "background work
/// runs under a service identity with explicit company_id passed by the enqueuing code"). Never
/// group-admin — a background consumer acts on behalf of the ONE company whose event it is
/// processing, not across companies.
/// </summary>
public sealed class FixedCompanyContext(string companyId) : ICompanyContext
{
    public string CompanyId => companyId;
    public bool IsGroupAdmin => false;
}
