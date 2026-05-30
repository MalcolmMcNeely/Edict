namespace Edict.ClaudeSkills;

public abstract record McpJsonInspection
{
    McpJsonInspection() { }

    public sealed record FileMissing() : McpJsonInspection;

    public sealed record NoEdictEntry() : McpJsonInspection;

    public sealed record EntryMatchesMode(InstallMode Mode) : McpJsonInspection;

    public sealed record EntryMismatchesMode(InstallMode DetectedMode, InstallMode CurrentForm) : McpJsonInspection;
}
