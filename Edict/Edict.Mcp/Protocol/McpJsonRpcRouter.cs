using System.Text.Json;
using System.Text.Json.Nodes;

using Edict.Mcp.Tools;

namespace Edict.Mcp.Protocol;

sealed class McpJsonRpcRouter
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    readonly McpToolRegistry registry;

    public McpJsonRpcRouter(McpToolRegistry registry)
    {
        this.registry = registry;
    }

    public async Task<string> RouteAsync(string requestJson, CancellationToken cancellationToken)
    {
        JsonNode? request;
        try
        {
            request = JsonNode.Parse(requestJson);
        }
        catch (JsonException exception)
        {
            return ErrorResponse(requestId: null, code: -32700, message: $"Parse error: {exception.Message}");
        }

        if (request is null)
        {
            return ErrorResponse(requestId: null, code: -32600, message: "Invalid Request: empty body");
        }

        var requestId = request["id"];
        var method = request["method"]?.GetValue<string>();

        return method switch
        {
            "tools/list" => ListToolsResponse(requestId),
            "tools/call" => await CallToolResponse(requestId, request["params"], cancellationToken),
            null => ErrorResponse(requestId, code: -32600, message: "Invalid Request: missing method"),
            _ => ErrorResponse(requestId, code: -32601, message: $"Method not found: {method}"),
        };
    }

    string ListToolsResponse(JsonNode? requestId)
    {
        var tools = registry.Tools
            .Select(tool => new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.InputSchema.GetRawText()),
            })
            .ToArray<JsonNode?>();

        return Envelope(requestId, new JsonObject
        {
            ["tools"] = new JsonArray(tools),
        });
    }

    async Task<string> CallToolResponse(JsonNode? requestId, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var toolName = parameters?["name"]?.GetValue<string>();
        if (toolName is null)
        {
            return ErrorResponse(requestId, code: -32602, message: "Invalid params: missing name");
        }

        var descriptor = registry.Find(toolName);
        if (descriptor is null)
        {
            return ErrorResponse(requestId, code: -32602, message: $"Unknown tool: {toolName}");
        }

        var arguments = ExtractArguments(parameters?["arguments"]);
        var resultText = await descriptor.InvokeAsync(arguments, cancellationToken);
        return Envelope(requestId, new JsonObject
        {
            ["content"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = resultText,
                }),
            ["isError"] = false,
        });
    }

    static IReadOnlyDictionary<string, JsonElement>? ExtractArguments(JsonNode? argumentsNode)
    {
        if (argumentsNode is not JsonObject argumentsObject)
        {
            return null;
        }

        var dictionary = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in argumentsObject)
        {
            if (value is null)
            {
                continue;
            }
            var element = JsonSerializer.Deserialize<JsonElement>(value.ToJsonString());
            dictionary[key] = element;
        }
        return dictionary;
    }

    static string Envelope(JsonNode? requestId, JsonObject result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId?.DeepClone(),
            ["result"] = result,
        }.ToJsonString(JsonOptions);
    }

    static string ErrorResponse(JsonNode? requestId, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        }.ToJsonString(JsonOptions);
    }
}
