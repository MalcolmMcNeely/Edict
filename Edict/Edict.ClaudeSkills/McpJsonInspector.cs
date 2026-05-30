using System.Text.Json;

namespace Edict.ClaudeSkills;

public sealed class McpJsonInspector
{
    static readonly JsonDocumentOptions PermissiveJsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public McpJsonInspection Inspect(string mcpJsonPath, InstallMode detectedMode)
    {
        if (!File.Exists(mcpJsonPath))
        {
            return new McpJsonInspection.FileMissing();
        }

        using var stream = File.OpenRead(mcpJsonPath);
        using var document = JsonDocument.Parse(stream, PermissiveJsonOptions);
        if (!document.RootElement.TryGetProperty("mcpServers", out var mcpServers)
            || mcpServers.ValueKind != JsonValueKind.Object
            || !mcpServers.TryGetProperty("edict", out var edictEntry))
        {
            return new McpJsonInspection.NoEdictEntry();
        }

        var currentForm = ClassifyEntryForm(edictEntry);
        if (currentForm == detectedMode)
        {
            return new McpJsonInspection.EntryMatchesMode(detectedMode);
        }
        return new McpJsonInspection.EntryMismatchesMode(detectedMode, currentForm);
    }

    static InstallMode ClassifyEntryForm(JsonElement edictEntry)
    {
        if (!edictEntry.TryGetProperty("command", out var commandElement)
            || commandElement.ValueKind != JsonValueKind.String)
        {
            return InstallMode.Global;
        }
        var command = commandElement.GetString();
        if (string.Equals(command, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return InstallMode.Manifest;
        }
        return InstallMode.Global;
    }
}
