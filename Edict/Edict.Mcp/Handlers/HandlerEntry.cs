namespace Edict.Mcp.Handlers;

sealed record HandlerEntry(
    string DeclaringTypeName,
    HandlerRole Role,
    IReadOnlyList<BoundContractInfo> BoundContracts,
    string DeclaringAssembly,
    SourceLocationInfo? SourceLocation);
