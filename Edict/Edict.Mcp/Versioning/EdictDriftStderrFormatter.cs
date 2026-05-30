using System.Text;

namespace Edict.Mcp.Versioning;

static class EdictDriftStderrFormatter
{
    public static string? Format(EdictVersionReport report)
    {
        if (report.DriftStatus == "clean")
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append("[edict-mcp] Edict version check: ").AppendLine(DescribeDriftClass(report.DriftStatus));
        builder.Append("  Tool version: ").AppendLine(report.ToolVersion);
        builder.Append("  Library versions: ").AppendLine(DescribeLibraryVersions(report));
        builder.Append("  Remediation: ").AppendLine(DescribeRemediation(report.DriftStatus));
        return builder.ToString();
    }

    static string DescribeDriftClass(string driftStatus)
    {
        return driftStatus switch
        {
            "drifted" => "Drift (tool version differs from a library reference).",
            "no-edict-references" => "No Edict references found in the loaded solution.",
            "inconsistent-library-versions" => "Inconsistent library versions across projects (ADR-0043 lockstep violation).",
            _ => driftStatus,
        };
    }

    static string DescribeLibraryVersions(EdictVersionReport report)
    {
        if (report.References.Count == 0)
        {
            return "(none)";
        }
        var distinctVersions = report.References
            .Select(reference => reference.Version)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(version => version, StringComparer.Ordinal);
        return string.Join(", ", distinctVersions);
    }

    static string DescribeRemediation(string driftStatus)
    {
        return driftStatus switch
        {
            "drifted" => "dotnet tool update Edict.Mcp --prerelease",
            "no-edict-references" => "verify edict-mcp is pointed at the right solution.",
            "inconsistent-library-versions" => "align all Edict.* PackageReference versions across the solution (ADR-0043 lockstep).",
            _ => "no action.",
        };
    }
}
