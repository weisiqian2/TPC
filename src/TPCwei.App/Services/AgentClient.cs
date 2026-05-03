using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace TPC.App.Services;

public sealed record AgentRpcRequest(string Method, JsonElement? Params);

public sealed record AgentRpcResponse(bool Ok, JsonElement? Result, string? Error);

public sealed class AgentClient
{
    public const string PipeName = "TPC.Agent";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AgentRpcResponse> CallAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1200, cancellationToken).ConfigureAwait(false);

        var requestJson = JsonSerializer.Serialize(new
        {
            method,
            @params = parameters
        }, JsonOptions);

        var requestBytes = Encoding.UTF8.GetBytes(requestJson + "\n");
        await pipe.WriteAsync(requestBytes, cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);

        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
        {
            return new AgentRpcResponse(false, null, "后台 Agent 没有返回数据");
        }

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement.Clone();
        var ok = root.TryGetProperty("ok", out var okElement) && okElement.GetBoolean();
        JsonElement? result = root.TryGetProperty("result", out var resultElement) ? resultElement.Clone() : null;
        var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
        return new AgentRpcResponse(ok, result, error);
    }
}
