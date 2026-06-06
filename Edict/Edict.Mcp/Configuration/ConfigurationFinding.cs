using Edict.Mcp.Handlers;

namespace Edict.Mcp.Configuration;

sealed record ConfigurationFinding(
    ConfigurationFindingSeverity Severity,
    ConfigurationFindingCategory Category,
    string OptionsType,
    string Knob,
    string Message,
    SourceLocationInfo? SourceLocation);
