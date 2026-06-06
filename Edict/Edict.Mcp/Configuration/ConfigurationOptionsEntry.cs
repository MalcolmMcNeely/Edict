namespace Edict.Mcp.Configuration;

sealed record ConfigurationOptionsEntry(
    string OptionsTypeName,
    string ConfiguringExtension,
    IReadOnlyList<ConfigurationKnob> Knobs);
