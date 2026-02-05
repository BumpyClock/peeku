using System.Text.Json;

namespace peeku.Mcp;

internal static class PeekuMcpToolDispatcher
{
  private static readonly JsonSerializerOptions DeserializeOptions = new()
  {
    PropertyNameCaseInsensitive = true,
  };

  internal static async Task<(bool Ok, ResultMeta Meta, object? Payload, PeekuError? Error)> DispatchAsync(
    IPeekuClient client,
    string toolName,
    JsonElement args,
    CancellationToken ct)
  {
    var scope = Results.Start();

    try
    {
      ct.ThrowIfCancellationRequested();

      if (client is null)
      {
        throw new ArgumentNullException(nameof(client));
      }

      if (string.IsNullOrWhiteSpace(toolName))
      {
        var meta = scope.Meta();
        return (false, meta, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Tool name is required."));
      }

      if (string.Equals(toolName, "peeku_batch", StringComparison.Ordinal))
      {
        var req = JsonSerializer.Deserialize<BatchRequest>(args, DeserializeOptions);
        if (req is null)
        {
          var meta = scope.Meta();
          return (false, meta, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Batch request is required."));
        }

        var res = await client.BatchAsync(req, ct).ConfigureAwait(false);
        return (res.Ok, res.Meta, res, res.Error);
      }

      if (string.Equals(toolName, "peeku_doctor", StringComparison.Ordinal))
      {
        var deep = ReadBool(args, "deep") ?? false;
        var res = await client.DoctorAsync(new DoctorRequest(Deep: deep), ct).ConfigureAwait(false);
        return (res.Ok, res.Meta, res, res.Error);
      }

      var batch = await client.BatchAsync(
        new BatchRequest(
          Ops: new[] { new BatchOp(toolName, args) },
          StopOnError: true),
        ct).ConfigureAwait(false);

      if (batch.Results.Count == 0)
      {
        return (
          Ok: false,
          Meta: batch.Meta,
          Payload: null,
          Error: PeekuErrors.Create(PeekuErrorCode.Internal, "Tool dispatch produced no results.", new { tool = toolName }));
      }

      var step = batch.Results[0];
      if (!step.Ok)
      {
        return (
          Ok: false,
          Meta: batch.Meta,
          Payload: null,
          Error: step.Error ?? batch.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Tool failed.", new { tool = toolName }));
      }

      if (step.Result is ResultBase rb)
      {
        return (rb.Ok, rb.Meta, rb, rb.Error);
      }

      return (true, batch.Meta, step.Result, null);
    }
    catch (NotImplementedException)
    {
      var meta = scope.Meta();
      return (false, meta, null, PeekuErrors.Create(PeekuErrorCode.NotSupported, "Not implemented.", new { tool = toolName }));
    }
    catch (JsonException ex)
    {
      var meta = scope.Meta();
      return (false, meta, null, PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Invalid JSON arguments.", new { tool = toolName, error = ex.Message }));
    }
    catch (OperationCanceledException)
    {
      var meta = scope.Meta();
      return (false, meta, null, PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      var meta = scope.Meta();
      return (false, meta, null, PeekuErrors.Create(PeekuErrorCode.Internal, "Tool dispatch failed.", new { tool = toolName, exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  private static bool? ReadBool(JsonElement obj, string name)
  {
    if (obj.ValueKind != JsonValueKind.Object)
    {
      return null;
    }

    foreach (var p in obj.EnumerateObject())
    {
      if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (p.Value.ValueKind == JsonValueKind.True) return true;
      if (p.Value.ValueKind == JsonValueKind.False) return false;
      return null;
    }

    return null;
  }
}
