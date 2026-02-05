using System.Text.Json;

namespace peeku;

internal static class BatchArgs
{
  internal static string? ReadString(JsonElement obj, string name)
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

      if (p.Value.ValueKind == JsonValueKind.String)
      {
        return p.Value.GetString();
      }

      return null;
    }

    return null;
  }

  internal static int? ReadInt(JsonElement obj, string name)
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

      if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var v))
      {
        return v;
      }

      return null;
    }

    return null;
  }

  internal static bool? ReadBool(JsonElement obj, string name)
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

  internal static bool TryReadSelector(JsonElement args, out Selector selector, out string? error)
  {
    selector = new Selector("");
    error = null;

    if (args.ValueKind != JsonValueKind.Object)
    {
      error = "Args must be an object.";
      return false;
    }

    if (!TryGet(args, "selector", out var selEl))
    {
      error = "Selector is required.";
      return false;
    }

    if (selEl.ValueKind == JsonValueKind.String)
    {
      var expr = selEl.GetString() ?? "";
      if (string.IsNullOrWhiteSpace(expr))
      {
        error = "Selector expr is required.";
        return false;
      }

      selector = new Selector(expr);
      return true;
    }

    if (selEl.ValueKind != JsonValueKind.Object || !TryGet(selEl, "expr", out var exprEl) || exprEl.ValueKind != JsonValueKind.String)
    {
      error = "Selector must be an object with expr.";
      return false;
    }

    var expr2 = exprEl.GetString() ?? "";
    if (string.IsNullOrWhiteSpace(expr2))
    {
      error = "Selector expr is required.";
      return false;
    }

    var preferCachedSnapshot = true;
    if (TryGet(selEl, "preferCachedSnapshot", out var preferEl))
    {
      if (preferEl.ValueKind == JsonValueKind.True)
      {
        preferCachedSnapshot = true;
      }
      else if (preferEl.ValueKind == JsonValueKind.False)
      {
        preferCachedSnapshot = false;
      }
    }

    selector = new Selector(expr2, preferCachedSnapshot);
    return true;
  }

  internal static bool TryReadElementRef(JsonElement args, string prop, out ElementRef element, out string? error)
  {
    element = new ElementRef("");
    error = null;

    if (args.ValueKind != JsonValueKind.Object)
    {
      error = "Args must be an object.";
      return false;
    }

    if (!TryGet(args, prop, out var el))
    {
      error = "ElementRef is required.";
      return false;
    }

    if (el.ValueKind == JsonValueKind.String)
    {
      var refId = el.GetString() ?? "";
      if (string.IsNullOrWhiteSpace(refId))
      {
        error = "ElementRef refId is required.";
        return false;
      }

      element = new ElementRef(refId);
      return true;
    }

    if (el.ValueKind != JsonValueKind.Object || !TryGet(el, "refId", out var refIdEl) || refIdEl.ValueKind != JsonValueKind.String)
    {
      error = "ElementRef must be an object with refId.";
      return false;
    }

    var refId2 = refIdEl.GetString() ?? "";
    if (string.IsNullOrWhiteSpace(refId2))
    {
      error = "ElementRef refId is required.";
      return false;
    }

    element = new ElementRef(refId2);
    return true;
  }

  internal static bool TryReadTarget(JsonElement args, out Target target, out string? error)
  {
    target = new Target.FocusedWindow();
    error = null;

    if (args.ValueKind != JsonValueKind.Object)
    {
      error = "Args must be an object.";
      return false;
    }

    if (!TryGet(args, "target", out var t))
    {
      error = "Target is required.";
      return false;
    }

    if (t.ValueKind == JsonValueKind.String)
    {
      return TryTargetFromKind(t.GetString() ?? "", out target, out error);
    }

    if (t.ValueKind != JsonValueKind.Object)
    {
      error = "Target must be an object.";
      return false;
    }

    if (TryGet(t, "kind", out var kindEl) && kindEl.ValueKind == JsonValueKind.String)
    {
      var kind = kindEl.GetString() ?? "";
      if (!TryTargetFromKind(kind, out target, out error, t))
      {
        return false;
      }

      return true;
    }

    // { desktop: {} } etc
    foreach (var p in t.EnumerateObject())
    {
      var key = p.Name.Trim();
      if (TryTargetFromKind(key, out target, out error, p.Value))
      {
        return true;
      }
    }

    error = "Unknown target shape.";
    return false;
  }

  internal static bool TryTargetFromKind(string kindRaw, out Target target, out string? error, JsonElement? obj = null)
  {
    target = new Target.FocusedWindow();
    error = null;

    var kind = (kindRaw ?? "").Trim().ToLowerInvariant();
    switch (kind)
    {
      case "desktop":
        target = new Target.Desktop();
        return true;

      case "focused":
      case "focusedwindow":
      case "focused_window":
      case "focused-window":
        target = new Target.FocusedWindow();
        return true;

      case "screen":
      case "display":
        if (obj is null || obj.Value.ValueKind != JsonValueKind.Object)
        {
          error = "Screen target requires screenIndex.";
          return false;
        }

        var idx = ReadInt(obj.Value, "screenIndex") ?? ReadInt(obj.Value, "index") ?? 0;
        target = new Target.Screen(idx);
        return true;

      case "hwnd":
      case "window_hwnd":
      case "window-hwnd":
      case "windowbyhwnd":
        if (obj is null || obj.Value.ValueKind != JsonValueKind.Object)
        {
          error = "Hwnd target requires hwndHex.";
          return false;
        }

        var hwnd = ReadString(obj.Value, "hwndHex") ?? ReadString(obj.Value, "hwnd") ?? "";
        if (string.IsNullOrWhiteSpace(hwnd))
        {
          error = "HwndHex is required.";
          return false;
        }

        target = new Target.WindowByHwnd(hwnd);
        return true;

      case "query":
      case "window_query":
      case "window-query":
      case "windowbyquery":
        if (obj is null || obj.Value.ValueKind != JsonValueKind.Object)
        {
          error = "Query target requires query object.";
          return false;
        }

        var qObj = obj.Value;
        if (TryGet(qObj, "query", out var inner) && inner.ValueKind == JsonValueKind.Object)
        {
          qObj = inner;
        }

        var title = ReadString(qObj, "titleContains");
        var proc = ReadString(qObj, "processName");
        var pid = ReadInt(qObj, "processId");

        target = new Target.WindowByQuery(new WindowQuery(title, proc, pid));
        return true;

      default:
        error = $"Unknown target kind: '{kindRaw}'.";
        return false;
    }
  }

  internal static bool TryGet(JsonElement obj, string name, out JsonElement value)
  {
    value = default;
    if (obj.ValueKind != JsonValueKind.Object)
    {
      return false;
    }

    foreach (var p in obj.EnumerateObject())
    {
      if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        value = p.Value;
        return true;
      }
    }

    return false;
  }

  internal static UiaPropertiesMode? ReadUiaPropertiesMode(JsonElement obj, string name)
  {
    var s = ReadString(obj, name);
    if (string.IsNullOrWhiteSpace(s))
    {
      return null;
    }

    return s.Trim().ToLowerInvariant() switch
    {
      "basic" => UiaPropertiesMode.Basic,
      "all" => UiaPropertiesMode.All,
      _ => null,
    };
  }

  internal static ActionMethod? ReadActionMethod(JsonElement obj, string name)
  {
    var s = ReadString(obj, name);
    if (string.IsNullOrWhiteSpace(s))
    {
      return null;
    }

    return s.Trim().ToLowerInvariant() switch
    {
      "auto" => ActionMethod.Auto,
      "uia" => ActionMethod.Uia,
      "input" => ActionMethod.Input,
      _ => null,
    };
  }

  internal static ScrollDirection? ReadScrollDirection(JsonElement obj, string name)
  {
    var s = ReadString(obj, name);
    if (string.IsNullOrWhiteSpace(s))
    {
      return null;
    }

    return s.Trim().ToLowerInvariant() switch
    {
      "vertical" => ScrollDirection.Vertical,
      "horizontal" => ScrollDirection.Horizontal,
      _ => null,
    };
  }

  internal static ObserveEventSet? ReadObserveEventSet(JsonElement obj, string name)
  {
    if (obj.ValueKind != JsonValueKind.Object)
    {
      return null;
    }

    if (!TryGet(obj, name, out var e))
    {
      return null;
    }

    if (e.ValueKind != JsonValueKind.Array)
    {
      return null;
    }

    var hasFocus = false;
    var hasOther = false;
    foreach (var item in e.EnumerateArray())
    {
      if (item.ValueKind != JsonValueKind.String)
      {
        continue;
      }

      var s = (item.GetString() ?? "").Trim().ToLowerInvariant();
      if (s == "focus") hasFocus = true;
      else if (s is "structure" or "property") hasOther = true;
    }

    if (hasOther && hasFocus) return ObserveEventSet.All;
    if (hasOther) return ObserveEventSet.Structure;
    if (hasFocus) return ObserveEventSet.Focus;
    return null;
  }

  internal static (bool Ok, ElementRef? Element, Selector? Selector, Target? Target, PeekuError? Error) ReadSelection(JsonElement args)
  {
    var hasElement = TryReadElementRef(args, "elementRef", out var element, out _);
    var hasSelector = TryReadSelector(args, out var selector, out _);

    if (hasElement == hasSelector)
    {
      return (
        Ok: false,
        Element: null,
        Selector: null,
        Target: null,
        Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Provide exactly one of elementRef or selector."));
    }

    var target = TryReadTarget(args, out var t, out _) ? t : null;
    return (true, hasElement ? element : null, hasSelector ? selector : null, target, null);
  }
}
