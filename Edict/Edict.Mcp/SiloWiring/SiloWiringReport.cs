using Edict.Mcp.Handlers;

namespace Edict.Mcp.SiloWiring;

sealed record SiloWiringReport(
    SourceLocationInfo? ProgramSourceLocation,
    IReadOnlyList<SiloWiringEntry> Wired,
    IReadOnlyList<SiloWiringEntry> Missing);
