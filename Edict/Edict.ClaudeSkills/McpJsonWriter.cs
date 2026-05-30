using System.Text;
using System.Text.Json;

namespace Edict.ClaudeSkills;

public sealed class McpJsonWriter
{
    static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\n",
    };

    static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public void Write(string targetPath, InstallMode installMode)
    {
        var parentDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        using var memoryStream = new MemoryStream();
        using (var jsonWriter = new Utf8JsonWriter(memoryStream, WriterOptions))
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WritePropertyName("mcpServers");
            jsonWriter.WriteStartObject();
            jsonWriter.WritePropertyName("edict");
            WriteEdictEntry(jsonWriter, installMode);
            jsonWriter.WriteEndObject();
            jsonWriter.WriteEndObject();
        }

        File.WriteAllText(targetPath, Utf8NoBom.GetString(memoryStream.ToArray()), Utf8NoBom);
    }

    static void WriteEdictEntry(Utf8JsonWriter jsonWriter, InstallMode installMode)
    {
        jsonWriter.WriteStartObject();
        if (installMode == InstallMode.Manifest)
        {
            jsonWriter.WriteString("command", "dotnet");
            jsonWriter.WritePropertyName("args");
            jsonWriter.WriteStartArray();
            jsonWriter.WriteStringValue("edict-mcp");
            jsonWriter.WriteEndArray();
        }
        else
        {
            jsonWriter.WriteString("command", "edict-mcp");
        }
        jsonWriter.WriteEndObject();
    }
}
