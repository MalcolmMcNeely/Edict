using Edict.Mcp.Handlers;

namespace Edict.Mcp.Configuration;

sealed record ConfigurationCheckReport(
    SourceLocationInfo? ProgramSourceLocation,
    IReadOnlyList<ConfigurationFinding> Findings);
