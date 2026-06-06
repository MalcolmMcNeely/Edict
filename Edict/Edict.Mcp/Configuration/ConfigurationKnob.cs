namespace Edict.Mcp.Configuration;

sealed record ConfigurationKnob(string Name, KnobRequirement Requirement, string? Footgun = null);
