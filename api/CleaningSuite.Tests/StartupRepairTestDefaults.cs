using System.Runtime.CompilerServices;

namespace CleaningSuite.Tests;

/// <summary>
/// Turns startup repairs off for every test host in this process. Repairs read the tenant registry at start, which
/// tests that assert registry access would otherwise see. Repair tests call the runners directly.
/// </summary>
internal static class StartupRepairTestDefaults
{
    [ModuleInitializer]
    internal static void Disable() => Environment.SetEnvironmentVariable("StartupRepairs__Enabled", "false");
}
