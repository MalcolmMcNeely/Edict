namespace Edict.Mcp.SiloWiring;

sealed record SiloWiringEntry(
    string ExtensionName,
    string DeclaringAssembly,
    string Purpose);
