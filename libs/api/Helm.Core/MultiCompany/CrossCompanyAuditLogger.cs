using Microsoft.Extensions.Logging;

namespace Helm.Core.MultiCompany;

public static class CrossCompanyAuditLogger
{
    // Structured audit event — App Insights picks this up; a core.audit_log table is a later hardening step.
    public static void LogBypass(ILogger logger, string companyId) =>
        logger.LogWarning("AUDIT cross-company access granted to group admin of {CompanyId}", companyId);
}
