using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FlaUI.Core.AutomationElements;

namespace peeku;

internal static class UiaRefId
{
  internal static string Create(AutomationElement element)
  {
    var pid = element.Properties.ProcessId.ValueOrDefault;
    var runtimeId = element.Properties.RuntimeId.ValueOrDefault;

    if (runtimeId is { Length: > 0 })
    {
      return $"uia:{pid}:{HashPidRuntimeId(pid, runtimeId)}";
    }

    var sb = new StringBuilder(capacity: 256);
    _ = sb.Append(pid);
    _ = sb.Append('|');
    _ = sb.Append(element.ControlType.ToString());
    _ = sb.Append('|');
    _ = sb.Append(element.AutomationId);
    _ = sb.Append('|');
    _ = sb.Append(element.ClassName);
    _ = sb.Append('|');
    _ = sb.Append(element.Name);
    _ = sb.Append('|');

    try
    {
      var r = element.BoundingRectangle;
      _ = sb.Append(r.Left);
      _ = sb.Append(',');
      _ = sb.Append(r.Top);
      _ = sb.Append(',');
      _ = sb.Append(r.Width);
      _ = sb.Append(',');
      _ = sb.Append(r.Height);
    }
    catch
    {
    }

    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    return $"uia:{pid}:{hash[..16].ToLowerInvariant()}";
  }

  private static string HashPidRuntimeId(int pid, int[] runtimeId)
  {
    var bytes = new byte[(runtimeId.Length + 1) * 4];

    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), pid);

    var offset = 4;
    foreach (var n in runtimeId)
    {
      BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), n);
      offset += 4;
    }

    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    return hash[..16].ToLowerInvariant();
  }
}

