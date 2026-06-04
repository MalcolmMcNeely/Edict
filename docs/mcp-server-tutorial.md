# How the Edict MCP server works (a tutorial for first-time MCP authors)

This walks through `Edict.Mcp` as a worked example. By the end you will understand
what MCP is, the wire protocol underneath it, how a tool is defined, and how the
server is packaged and wired into an agent. Every snippet is real code from
`Edict/Edict.Mcp`.

You do not need to know Edict to follow this. Edict is just the thing the server
reports on; the MCP mechanics are general.

## 1. What MCP actually is

The Model Context Protocol lets an AI agent (Claude Code, Cursor, etc.) call into
your code. You expose a set of **tools**. The agent can do two things:

- **list** the tools you offer (each has a name, a description, and a schema for its arguments)
- **call** a tool by name with arguments, and get text back

That is the whole model. A tool is a named function with a typed input and a
string output. The agent decides when to call it; you decide what it does.

The transport is **stdio**: the agent launches your server as a child process and
talks to it over stdin/stdout. The message format is **JSON-RPC 2.0**.

## 2. The wire, before any SDK

Start by looking at the protocol with nothing hiding it. `McpJsonRpcRouter` is a
hand-rolled router used in the tests. It is the clearest way to see the two methods
that matter.

A request the agent sends looks like this:

```json
{ "jsonrpc": "2.0", "id": 1, "method": "tools/list" }
```

The router dispatches on `method`:

```csharp
return method switch
{
    "tools/list" => ListToolsResponse(requestId),
    "tools/call" => await CallToolResponse(requestId, request["params"], cancellationToken),
    null => ErrorResponse(requestId, code: -32600, message: "Invalid Request: missing method"),
    _ => ErrorResponse(requestId, code: -32601, message: $"Method not found: {method}"),
};
```

`tools/list` answers with the catalogue: each tool's `name`, `description`, and
`inputSchema`.

```csharp
var tools = registry.Tools
    .Select(tool => new JsonObject
    {
        ["name"] = tool.Name,
        ["description"] = tool.Description,
        ["inputSchema"] = JsonNode.Parse(tool.InputSchema.GetRawText()),
    })
    .ToArray<JsonNode?>();
```

`tools/call` looks the tool up by name, hands it the arguments, and wraps the
returned text in a content block:

```csharp
var descriptor = registry.Find(toolName);
var arguments = ExtractArguments(parameters?["arguments"]);
var resultText = await descriptor.InvokeAsync(arguments, cancellationToken);
return Envelope(requestId, new JsonObject
{
    ["content"] = new JsonArray(
        new JsonObject { ["type"] = "text", ["text"] = resultText }),
    ["isError"] = false,
});
```

Every response is wrapped in the JSON-RPC envelope: `jsonrpc`, the `id` echoed
back, and either `result` or `error`. That is all the protocol is.

You would not normally write this router yourself. It exists here so the tests can
drive the protocol directly. Production uses the SDK, which does the same thing.

## 3. The same thing with the SDK

The `ModelContextProtocol` NuGet package gives you the transport and envelope for
free. `Program.cs` builds a generic host and registers two handlers, one per
method:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithListToolsHandler((request, cancellationToken) =>
        ValueTask.FromResult(BuildListToolsResult(registry)))
    .WithCallToolHandler((request, cancellationToken) =>
        BuildCallToolResultAsync(registry, request.Params, cancellationToken));

await builder.Build().RunAsync();
```

`WithListToolsHandler` is the SDK's `tools/list`. `WithCallToolHandler` is its
`tools/call`. Compare them line-for-line with the router above; the shapes are
identical, the SDK just owns the parsing and the envelope.

> **The one gotcha that bites everyone:** `builder.Logging.ClearProviders()`.
> stdout *is* the JSON-RPC channel. Anything you print to it that is not a valid
> JSON-RPC message corrupts the stream and the agent disconnects. Never
> `Console.WriteLine` in an stdio server. If you need to say something to a human,
> write to **stderr** (Edict uses it for version-drift warnings at startup).

## 4. What a tool is

Edict models a tool as a small record. Four fields: a name, a description, an
input schema, and the function to run.

```csharp
sealed record McpToolDescriptor(
    string Name,
    string Description,
    JsonElement InputSchema,
    Func<IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<string>> InvokeAsync);
```

The `Name` is what the agent calls. The `Description` is what the agent reads to
decide *whether* to call it, so it is prose, not a label; write it for an LLM
audience. `InputSchema` is JSON Schema describing the arguments. `InvokeAsync`
takes the parsed arguments and returns the text the agent receives.

## 5. A tool from end to end

Here is the simplest real tool: look up a glossary term. It shows the whole
contract: validate arguments in, return text out.

```csharp
public Task<string> InvokeAsync(IReadOnlyDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
{
    if (arguments is null || !arguments.TryGetValue("term", out var termElement) || termElement.ValueKind != JsonValueKind.String)
    {
        return Task.FromResult("Missing required argument 'term' (string).");
    }

    var term = termElement.GetString();
    var body = docs.LookupGlossaryTerm(term);
    return Task.FromResult(body ?? $"Glossary term '{term}' not found in CONTEXT.md.");
}
```

Note what it does *not* do: it does not throw on bad input, and it does not return
an error envelope. A not-found term is a normal, expected result the agent should
read, so it comes back as plain text. Reserve `isError` for genuine protocol
faults (unknown tool, malformed request).

Arguments arrive as `JsonElement`, so you check `ValueKind` and pull the value out
yourself. That manual extraction is the price of a permissive, schema-driven input.

## 6. Describing the input: JSON Schema

The schema is how the agent knows what to pass. For the glossary tool:

```json
{
  "type": "object",
  "properties": {
    "term": {
      "type": "string",
      "description": "Glossary term to look up. Case-insensitive; the optional 'Edict' prefix is elidable so 'Saga', 'saga', and 'EdictSaga' all resolve to the same entry."
    }
  },
  "required": ["term"]
}
```

The `description` on a property is not decoration. The agent uses it to fill the
argument correctly, so it is worth as much care as the tool description itself.

A tool that takes no arguments still needs a schema, an empty object:

```csharp
static readonly JsonElement EmptyInputSchema = ParseInputSchema(
    """{"type":"object","properties":{}}""");
```

## 7. Registering the tools

The registry is just a list of descriptors plus a name lookup. Each entry pairs the
metadata the agent sees with the function that runs:

```csharp
new McpToolDescriptor(
    Name: "edict_describe_glossary_term",
    Description: "Returns the Edict glossary entry for a term from CONTEXT.md, including its definition, the '_Avoid_' list, and any inline cross-references. Case-insensitive; the optional 'Edict' prefix on the query is elidable.",
    InputSchema: GlossaryTermInputSchema,
    InvokeAsync: describeGlossaryTerm.InvokeAsync),
```

```csharp
public McpToolDescriptor? Find(string name) =>
    Tools.FirstOrDefault(tool => tool.Name == name);
```

Both `tools/list` and `tools/call` go through this one list. List enumerates it;
call resolves a name against it. Add a tool by adding a descriptor.

## 8. Where the data comes from (Edict-specific, skip if you only want MCP)

A tool can return anything. Edict's tools fall into two kinds:

- **Embedded docs.** ADRs and the glossary are baked into the assembly as embedded
  resources at build time, so the server has them with no file access:

  ```xml
  <EmbeddedResource Include="..\..\CONTEXT.md" LogicalName="Edict.Mcp.Docs.CONTEXT.md" />
  <EmbeddedResource Include="..\..\docs\adr\*.md" LogicalName="Edict.Mcp.Docs.Adr.%(Filename)%(Extension)" />
  ```

- **Live solution analysis.** Tools like `edict_list_handlers` open the consumer's
  solution with Roslyn (`MSBuildWorkspace`) and walk the syntax to find handler
  classes. The solution is loaded once and cached behind a gate:

  ```csharp
  var solutionPath = ResolveSolutionPath();
  var workspace = MSBuildWorkspace.Create();
  loadedSolution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
  ```

The lesson that generalises: a tool's body is ordinary code. Read a file, query a
database, call an API. MCP only governs the name-in, text-out boundary.

## 9. Packaging and wiring

The server is shipped as a `dotnet` tool. The project file makes the executable
installable under a short command name:

```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>edict-mcp</ToolCommandName>
```

The agent is told how to launch it via `.mcp.json` at the repo root:

```json
{
  "mcpServers": {
    "edict": { "command": "dotnet", "args": ["edict-mcp"] }
  }
}
```

`command` is the process the agent spawns; it talks to that process over stdio.
That is the link between "I wrote a server" and "an agent can use it".

## 10. Try it yourself

You can drive the server by hand without an agent, since it is just JSON-RPC over
stdin. List the tools:

```powershell
'{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | dotnet run --project Edict/Edict.Mcp
```

Call one:

```powershell
'{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"edict_describe_glossary_term","arguments":{"term":"saga"}}}' | dotnet run --project Edict/Edict.Mcp
```

You will get a JSON-RPC envelope back with the glossary entry as the text content.
That round trip, list then call, is the entire protocol you just learned.

## Where to go next

- Read `McpJsonRpcRouter.cs` and its tests to see the protocol with no SDK.
- Read `Program.cs` to see the SDK doing the same job.
- Read one tool end to end (`DescribeGlossaryTermTool.cs` is the smallest).
- To add your own tool: write a class with an `InvokeAsync`, give it a schema, and
  add one `McpToolDescriptor` to the registry.
