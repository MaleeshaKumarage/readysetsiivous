using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Tenants;

/// <summary>
/// Records that a one-time startup data repair has completed for one tenant. A repair with a marker is skipped on
/// later starts, so it runs once. A repair that had failures writes no marker and is tried again on the next start.
/// </summary>
public class StartupRepairMarker : BaseDocument
{
    public const string BranchShiftMerge = "branch-shift-merge";
    public const string StaleAssignmentRelease = "stale-assignment-release";

    public string Repair { get; set; } = "";
    public DateTime CompletedUtc { get; set; }
    public string Summary { get; set; } = "";

    public static Guid IdFor(string repair) => new(MD5.HashData(Encoding.UTF8.GetBytes("startup-repair:" + repair)));
}
