using System.ComponentModel;
using System.Runtime.InteropServices;

namespace peeku;

internal static class HotkeyInputInjector
{
  internal readonly record struct KeySpec(ushort Vk, bool Extended);

  internal sealed record HotkeyChord(
    IReadOnlyList<KeySpec> Modifiers,
    IReadOnlyList<KeySpec> Keys);

  public static bool TryParse(string keys, out HotkeyChord chord, out string? error)
  {
    chord = new HotkeyChord(Array.Empty<KeySpec>(), Array.Empty<KeySpec>());
    error = null;

    if (string.IsNullOrWhiteSpace(keys))
    {
      error = "Keys is required.";
      return false;
    }

    var parts = keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length == 0)
    {
      error = "Keys is required.";
      return false;
    }

    var modifiers = new List<KeySpec>(capacity: 4);
    var chordKeys = new List<KeySpec>(capacity: 2);

    foreach (var raw in parts)
    {
      var token = Canon(raw);
      if (token.Length == 0)
      {
        continue;
      }

      if (TryGetModifier(token, out var mod))
      {
        modifiers.Add(mod);
        continue;
      }

      if (!TryGetKey(token, out var key))
      {
        error = $"Unknown key token: '{raw}'.";
        return false;
      }

      chordKeys.Add(key);
    }

    if (chordKeys.Count == 0)
    {
      error = "Hotkey must include a non-modifier key (e.g. CTRL+SHIFT+S).";
      return false;
    }

    chord = new HotkeyChord(modifiers, chordKeys);
    return true;
  }

  /// <summary>
  /// Resolves a single named key token (e.g. <c>enter</c>, <c>tab</c>, <c>f5</c>, <c>a</c>, <c>5</c>,
  /// <c>down</c>) to its virtual-key spec, REUSING the same VK table / extended-key handling the
  /// hotkey chord parser uses (no duplicate mapping). Modifier-only tokens (<c>ctrl</c>/<c>shift</c>/…)
  /// are rejected here: <c>press</c> is for discrete keys, not chords — use <c>hotkey</c> for those.
  /// </summary>
  public static bool TryResolveKey(string name, out KeySpec key, out string? error)
  {
    key = default;
    error = null;

    if (string.IsNullOrWhiteSpace(name))
    {
      error = "Key name is required.";
      return false;
    }

    var token = Canon(name);
    if (token.Length == 0)
    {
      error = "Key name is required.";
      return false;
    }

    if (TryGetModifier(token, out _))
    {
      error = $"'{name}' is a modifier; use hotkey for chords (e.g. CTRL+S). press takes discrete keys.";
      return false;
    }

    if (!TryGetKey(token, out key))
    {
      error = $"Unknown key name: '{name}'.";
      return false;
    }

    return true;
  }

  /// <summary>
  /// Sends a single key as a keydown then keyup. When <paramref name="holdMs"/> &gt; 0 the key is held
  /// down for that interval before release. Foreground/global: keys land on whatever window has focus.
  /// </summary>
  public static async Task SendKeyAsync(KeySpec key, int holdMs, CancellationToken ct)
  {
    SendInputs(new[] { KeyDown(key) });

    if (holdMs > 0)
    {
      await Task.Delay(holdMs, ct).ConfigureAwait(false);
    }

    SendInputs(new[] { KeyUp(key) });
  }

  public static void Send(HotkeyChord chord)
  {
    var inputs = BuildInputs(chord);
    SendInputs(inputs);
  }

  private static void SendInputs(INPUT[] inputs)
  {
    if (inputs.Length == 0)
    {
      return;
    }

    var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    if (sent == (uint)inputs.Length)
    {
      return;
    }

    var err = Marshal.GetLastWin32Error();
    throw new Win32Exception(err, $"SendInput failed: sent={sent} expected={inputs.Length} win32={err}.");
  }

  internal static INPUT[] BuildInputs(HotkeyChord chord)
  {
    if (chord.Keys.Count == 0)
    {
      return Array.Empty<INPUT>();
    }

    var total =
      chord.Modifiers.Count +
      (chord.Keys.Count * 2) +
      chord.Modifiers.Count;

    var inputs = new INPUT[total];
    var i = 0;

    foreach (var mod in chord.Modifiers)
    {
      inputs[i++] = KeyDown(mod);
    }

    foreach (var key in chord.Keys)
    {
      inputs[i++] = KeyDown(key);
      inputs[i++] = KeyUp(key);
    }

    for (var j = chord.Modifiers.Count - 1; j >= 0; j--)
    {
      inputs[i++] = KeyUp(chord.Modifiers[j]);
    }

    return inputs;
  }

  // ── Mouse INPUT builders ─────────────────────────────────────────────────────

  /// <summary>
  /// Absolute-position move event. <paramref name="ax"/>/<paramref name="ay"/> are in the
  /// 0..65535 range produced by <see cref="Win32Screen.ToAbsolute"/>. Use together with
  /// <see cref="MOUSEEVENTF_ABSOLUTE"/> | <see cref="MOUSEEVENTF_VIRTUALDESK"/>.
  /// </summary>
  internal static INPUT MouseMove(int ax, int ay)
    => new()
    {
      type = INPUT_MOUSE,
      U = new InputUnion
      {
        mi = new MOUSEINPUT
        {
          dx = ax,
          dy = ay,
          mouseData = 0,
          dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
          time = 0,
          dwExtraInfo = 0,
        },
      },
    };

  /// <summary>Builds a mouse button-down INPUT. Coordinates are ignored (cursor already moved).</summary>
  internal static INPUT MouseButtonDown(uint downFlag)
    => new()
    {
      type = INPUT_MOUSE,
      U = new InputUnion
      {
        mi = new MOUSEINPUT
        {
          dx = 0,
          dy = 0,
          mouseData = 0,
          dwFlags = downFlag,
          time = 0,
          dwExtraInfo = 0,
        },
      },
    };

  /// <summary>Builds a mouse button-up INPUT. Coordinates are ignored (cursor already moved).</summary>
  internal static INPUT MouseButtonUp(uint upFlag)
    => new()
    {
      type = INPUT_MOUSE,
      U = new InputUnion
      {
        mi = new MOUSEINPUT
        {
          dx = 0,
          dy = 0,
          mouseData = 0,
          dwFlags = upFlag,
          time = 0,
          dwExtraInfo = 0,
        },
      },
    };

  /// <summary>
  /// Like <see cref="SendInputs"/> but returns the raw <c>SendInput</c> count instead of throwing.
  /// Used by the mouse path so callers can distinguish partial delivery from total failure (UIPI).
  /// The keyboard path keeps the throwing <see cref="SendInputs"/> unchanged.
  /// </summary>
  internal static uint SendInputsCounted(INPUT[] inputs)
  {
    if (inputs.Length == 0)
    {
      return 0;
    }

    return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
  }

  /// <summary>
  /// Builds a UNICODE key-down + key-up pair for a single UTF-16 code unit.
  /// Use <c>KEYEVENTF_UNICODE</c> with <c>wVk=0</c> and <c>wScan=codeUnit</c>.
  /// Surrogate halves are accepted — <c>KEYEVENTF_UNICODE</c> delivers WM_CHAR with the raw code unit.
  /// </summary>
  internal static INPUT[] BuildUnicodeChar(ushort codeUnit)
    =>
    [
      new INPUT
      {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
          ki = new KEYBDINPUT
          {
            wVk = 0,
            wScan = codeUnit,
            dwFlags = KEYEVENTF_UNICODE,
            time = 0,
            dwExtraInfo = 0,
          },
        },
      },
      new INPUT
      {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
          ki = new KEYBDINPUT
          {
            wVk = 0,
            wScan = codeUnit,
            dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
            time = 0,
            dwExtraInfo = 0,
          },
        },
      },
    ];

  private static INPUT KeyDown(KeySpec key)
    => new()
    {
      type = INPUT_KEYBOARD,
      U = new InputUnion
      {
        ki = new KEYBDINPUT
        {
          wVk = key.Vk,
          wScan = 0,
          dwFlags = key.Extended ? KEYEVENTF_EXTENDEDKEY : 0,
          time = 0,
          dwExtraInfo = 0,
        },
      },
    };

  private static INPUT KeyUp(KeySpec key)
    => new()
    {
      type = INPUT_KEYBOARD,
      U = new InputUnion
      {
        ki = new KEYBDINPUT
        {
          wVk = key.Vk,
          wScan = 0,
          dwFlags = (key.Extended ? KEYEVENTF_EXTENDEDKEY : 0) | KEYEVENTF_KEYUP,
          time = 0,
          dwExtraInfo = 0,
        },
      },
    };

  private static bool TryGetModifier(string token, out KeySpec modifier)
  {
    modifier = default;

    // left by default; allow L/R variants
    if (token is "CTRL" or "CONTROL" or "CTL" or "LCTRL" or "LCONTROL")
    {
      modifier = new KeySpec(VK_LCONTROL, Extended: false);
      return true;
    }

    if (token is "RCTRL" or "RCONTROL")
    {
      modifier = new KeySpec(VK_RCONTROL, Extended: true);
      return true;
    }

    if (token is "SHIFT" or "LSHIFT")
    {
      modifier = new KeySpec(VK_LSHIFT, Extended: false);
      return true;
    }

    if (token is "RSHIFT")
    {
      modifier = new KeySpec(VK_RSHIFT, Extended: false);
      return true;
    }

    if (token is "ALT" or "OPTION" or "LMENU" or "LALT")
    {
      modifier = new KeySpec(VK_LMENU, Extended: false);
      return true;
    }

    if (token is "RALT" or "RMENU")
    {
      modifier = new KeySpec(VK_RMENU, Extended: true);
      return true;
    }

    if (token is "WIN" or "WINDOWS" or "CMD" or "COMMAND" or "META" or "SUPER" or "LWIN")
    {
      modifier = new KeySpec(VK_LWIN, Extended: true);
      return true;
    }

    if (token is "RWIN")
    {
      modifier = new KeySpec(VK_RWIN, Extended: true);
      return true;
    }

    return false;
  }

  private static bool TryGetKey(string token, out KeySpec key)
  {
    key = default;

    if (token.Length == 1)
    {
      var c = token[0];
      if (c is >= 'A' and <= 'Z')
      {
        key = new KeySpec((ushort)c, Extended: false);
        return true;
      }

      if (c is >= '0' and <= '9')
      {
        key = new KeySpec((ushort)c, Extended: false);
        return true;
      }
    }

    if (TryParseFunctionKey(token, out var fKeyVk))
    {
      key = new KeySpec(fKeyVk, Extended: false);
      return true;
    }

    if (TryGetNamedKeyVk(token, out var vk))
    {
      key = new KeySpec(vk, Extended: IsExtendedKey(vk));
      return true;
    }

    if (TryParseNumpadKey(token, out var numVk))
    {
      key = new KeySpec(numVk, Extended: IsExtendedKey(numVk));
      return true;
    }

    return false;
  }

  private static bool TryParseFunctionKey(string token, out ushort vk)
  {
    vk = 0;

    if (token.Length < 2 || token[0] != 'F')
    {
      return false;
    }

    if (!int.TryParse(token.AsSpan(1), out var n))
    {
      return false;
    }

    if (n is < 1 or > 24)
    {
      return false;
    }

    vk = (ushort)(VK_F1 + (n - 1));
    return true;
  }

  private static bool TryParseNumpadKey(string token, out ushort vk)
  {
    vk = 0;

    if (token.StartsWith("NUMPAD", StringComparison.Ordinal))
    {
      var rest = token.AsSpan("NUMPAD".Length);
      if (rest.Length == 1 && rest[0] is >= '0' and <= '9')
      {
        vk = (ushort)(VK_NUMPAD0 + (rest[0] - '0'));
        return true;
      }

      return token switch
      {
        "NUMPADADD" => (vk = VK_ADD) != 0,
        "NUMPADSUBTRACT" => (vk = VK_SUBTRACT) != 0,
        "NUMPADMULTIPLY" => (vk = VK_MULTIPLY) != 0,
        "NUMPADDIVIDE" => (vk = VK_DIVIDE) != 0,
        "NUMPADDECIMAL" => (vk = VK_DECIMAL) != 0,
        _ => false,
      };
    }

    if (token.StartsWith("NUM", StringComparison.Ordinal) && token.Length == 4 && token[3] is >= '0' and <= '9')
    {
      vk = (ushort)(VK_NUMPAD0 + (token[3] - '0'));
      return true;
    }

    return false;
  }

  private static bool TryGetNamedKeyVk(string token, out ushort vk)
  {
    vk = token switch
    {
      "ENTER" or "RETURN" => VK_RETURN,
      "ESC" or "ESCAPE" => VK_ESCAPE,
      "TAB" => VK_TAB,
      "SPACE" => VK_SPACE,
      "BACKSPACE" or "BKSP" => VK_BACK,
      "DELETE" or "DEL" => VK_DELETE,
      "INSERT" or "INS" => VK_INSERT,
      "HOME" => VK_HOME,
      "END" => VK_END,
      "PAGEUP" or "PGUP" or "PRIOR" => VK_PRIOR,
      "PAGEDOWN" or "PGDN" or "NEXT" => VK_NEXT,
      "LEFT" => VK_LEFT,
      "RIGHT" => VK_RIGHT,
      "UP" => VK_UP,
      "DOWN" => VK_DOWN,
      "PRINTSCREEN" or "PRTSC" or "SNAPSHOT" => VK_SNAPSHOT,
      "CAPSLOCK" => VK_CAPITAL,
      "NUMLOCK" => VK_NUMLOCK,
      "SCROLLLOCK" => VK_SCROLL,
      "APPS" or "MENU" => VK_APPS,
      "PAUSE" => VK_PAUSE,

      "PLUS" or "EQUALS" or "OEMPLUS" => VK_OEM_PLUS,
      "MINUS" or "OEMMINUS" => VK_OEM_MINUS,
      "COMMA" => VK_OEM_COMMA,
      "PERIOD" or "DOT" => VK_OEM_PERIOD,
      "SLASH" or "FORWARDSLASH" => VK_OEM_2,
      "BACKSLASH" => VK_OEM_5,
      "SEMICOLON" => VK_OEM_1,
      "QUOTE" or "APOSTROPHE" => VK_OEM_7,
      "LBRACKET" or "LEFTBRACKET" => VK_OEM_4,
      "RBRACKET" or "RIGHTBRACKET" => VK_OEM_6,
      "GRAVE" or "BACKTICK" or "TILDE" => VK_OEM_3,

      _ => 0,
    };

    return vk != 0;
  }

  private static bool IsExtendedKey(ushort vk)
    => vk is
      VK_RCONTROL or VK_RMENU or VK_LWIN or VK_RWIN or VK_APPS or
      VK_INSERT or VK_DELETE or VK_HOME or VK_END or VK_PRIOR or VK_NEXT or
      VK_LEFT or VK_RIGHT or VK_UP or VK_DOWN or
      VK_DIVIDE or VK_SNAPSHOT;

  private static string Canon(string raw)
    => raw
      .Trim()
      .Replace(" ", "", StringComparison.Ordinal)
      .Replace("_", "", StringComparison.Ordinal)
      .Replace("-", "", StringComparison.Ordinal)
      .ToUpperInvariant();

  private const uint INPUT_MOUSE    = 0;
  private const uint INPUT_KEYBOARD = 1;

  private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
  private const uint KEYEVENTF_KEYUP       = 0x0002;
  private const uint KEYEVENTF_UNICODE     = 0x0004;

  internal const uint MOUSEEVENTF_MOVE       = 0x0001;
  internal const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
  internal const uint MOUSEEVENTF_LEFTUP     = 0x0004;
  internal const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
  internal const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
  internal const uint MOUSEEVENTF_WHEEL      = 0x0800;
  internal const uint MOUSEEVENTF_ABSOLUTE   = 0x8000;
  internal const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

  private const ushort VK_BACK = 0x08;
  private const ushort VK_TAB = 0x09;
  private const ushort VK_RETURN = 0x0D;
  private const ushort VK_SHIFT = 0x10;
  private const ushort VK_CONTROL = 0x11;
  private const ushort VK_MENU = 0x12;
  private const ushort VK_PAUSE = 0x13;
  private const ushort VK_CAPITAL = 0x14;
  private const ushort VK_ESCAPE = 0x1B;
  private const ushort VK_SPACE = 0x20;
  private const ushort VK_PRIOR = 0x21;
  private const ushort VK_NEXT = 0x22;
  private const ushort VK_END = 0x23;
  private const ushort VK_HOME = 0x24;
  private const ushort VK_LEFT = 0x25;
  private const ushort VK_UP = 0x26;
  private const ushort VK_RIGHT = 0x27;
  private const ushort VK_DOWN = 0x28;
  private const ushort VK_SNAPSHOT = 0x2C;
  private const ushort VK_INSERT = 0x2D;
  private const ushort VK_DELETE = 0x2E;

  private const ushort VK_LWIN = 0x5B;
  private const ushort VK_RWIN = 0x5C;
  private const ushort VK_APPS = 0x5D;

  private const ushort VK_NUMPAD0 = 0x60;
  private const ushort VK_MULTIPLY = 0x6A;
  private const ushort VK_ADD = 0x6B;
  private const ushort VK_SUBTRACT = 0x6D;
  private const ushort VK_DECIMAL = 0x6E;
  private const ushort VK_DIVIDE = 0x6F;
  private const ushort VK_F1 = 0x70;

  private const ushort VK_NUMLOCK = 0x90;
  private const ushort VK_SCROLL = 0x91;

  private const ushort VK_LSHIFT = 0xA0;
  private const ushort VK_RSHIFT = 0xA1;
  private const ushort VK_LCONTROL = 0xA2;
  private const ushort VK_RCONTROL = 0xA3;
  private const ushort VK_LMENU = 0xA4;
  private const ushort VK_RMENU = 0xA5;

  private const ushort VK_OEM_1 = 0xBA;
  private const ushort VK_OEM_PLUS = 0xBB;
  private const ushort VK_OEM_COMMA = 0xBC;
  private const ushort VK_OEM_MINUS = 0xBD;
  private const ushort VK_OEM_PERIOD = 0xBE;
  private const ushort VK_OEM_2 = 0xBF;
  private const ushort VK_OEM_3 = 0xC0;
  private const ushort VK_OEM_4 = 0xDB;
  private const ushort VK_OEM_5 = 0xDC;
  private const ushort VK_OEM_6 = 0xDD;
  private const ushort VK_OEM_7 = 0xDE;

  [DllImport("user32.dll", SetLastError = true)]
  private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

  [StructLayout(LayoutKind.Sequential)]
  internal struct INPUT
  {
    public uint type;
    public InputUnion U;
  }

  [StructLayout(LayoutKind.Explicit)]
  internal struct InputUnion
  {
    [FieldOffset(0)]
    public MOUSEINPUT mi;

    [FieldOffset(0)]
    public KEYBDINPUT ki;

    [FieldOffset(0)]
    public HARDWAREINPUT hi;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct MOUSEINPUT
  {
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public nuint dwExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct KEYBDINPUT
  {
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public nuint dwExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct HARDWAREINPUT
  {
    public uint uMsg;
    public ushort wParamL;
    public ushort wParamH;
  }
}
