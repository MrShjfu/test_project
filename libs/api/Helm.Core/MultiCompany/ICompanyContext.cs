namespace Helm.Core.MultiCompany;

public interface ICompanyContext
{
    string CompanyId { get; }
    bool IsGroupAdmin { get; }   // company_id == "ntg" && module_roles contains "*:admin"
}
