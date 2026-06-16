using System.Globalization;

namespace peeku.Cli;

/// <summary>
/// Parses the <c>--timeout</c> value. A BARE INTEGER means MILLISECONDS (agents-first: an agent
/// writing <c>--timeout 5000</c> intends 5 seconds, not 5000 days — which is what the default
/// <see cref="System.TimeSpan"/> parser does with a bare integer). Also accepts unit suffixes
/// (<c>500ms</c>, <c>5s</c>, <c>2m</c>) and the classic <c>hh:mm:ss</c> TimeSpan form (back-compat).
/// </summary>
internal static class CliTimeout
{
  internal static bool TryParse(string? raw, out TimeSpan timeout, out string? error)
  {
    timeout = TimeSpan.Zero;
    error = null;

    if (string.IsNullOrWhiteSpace(raw))
    {
      error = "Timeout is required.";
      return false;
    }

    var s = raw.Trim();

    // Bare integer => milliseconds (the agents-first default; checked BEFORE TimeSpan.Parse, which
    // would otherwise read a bare integer as DAYS).
    if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
    {
      if (ms < 0)
      {
        error = "Timeout must be >= 0.";
        return false;
      }

      timeout = TimeSpan.FromMilliseconds(ms);
      return true;
    }

    // Unit suffixes. Check "ms" before "s"/"m" (it ends with both 's' and 'm' substrings).
    if (TryUnit(s, "ms", out var msVal) && msVal >= 0) { timeout = TimeSpan.FromMilliseconds(msVal); return true; }
    if (TryUnit(s, "s", out var sVal) && sVal >= 0) { timeout = TimeSpan.FromSeconds(sVal); return true; }
    if (TryUnit(s, "m", out var mVal) && mVal >= 0) { timeout = TimeSpan.FromMinutes(mVal); return true; }

    // Classic hh:mm:ss / d.hh:mm:ss (back-compat). NOT a bare integer (handled above).
    if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var ts) && ts >= TimeSpan.Zero)
    {
      timeout = ts;
      return true;
    }

    error = $"Invalid --timeout '{raw}'. Use milliseconds (e.g. 5000), a unit (500ms, 5s, 2m), or hh:mm:ss.";
    return false;
  }

  private static bool TryUnit(string s, string suffix, out double value)
  {
    value = 0;
    if (!s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    var num = s[..^suffix.Length];
    return double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
  }
}
