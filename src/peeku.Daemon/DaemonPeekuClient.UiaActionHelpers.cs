using System.Text.Json;
using FlaUI.Core.AutomationElements;
using peeku;

namespace peeku.Daemon;

public sealed partial class DaemonPeekuClient
{
  private static bool TryClickViaUiaPatterns(AutomationElement element, out PeekuError error)
  {
    error = PeekuErrors.Create(PeekuErrorCode.NotSupported, "Element does not support click via UIA patterns.");

    try
    {
      if (element.Patterns.Invoke.IsSupported)
      {
        element.Patterns.Invoke.Pattern.Invoke();
        return true;
      }
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA invoke failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }

    try
    {
      if (element.Patterns.Toggle.IsSupported)
      {
        element.Patterns.Toggle.Pattern.Toggle();
        return true;
      }
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA toggle failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }

    try
    {
      if (element.Patterns.SelectionItem.IsSupported)
      {
        element.Patterns.SelectionItem.Pattern.Select();
        return true;
      }
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA select failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }

    return false;
  }

  private static bool TryInvoke(AutomationElement element, out PeekuError error)
  {
    error = PeekuErrors.Create(PeekuErrorCode.NotSupported, "Element does not support invoke pattern.");

    if (!element.Patterns.Invoke.IsSupported)
    {
      return false;
    }

    try
    {
      element.Patterns.Invoke.Pattern.Invoke();
      return true;
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA invoke failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }
  }

  private static bool TrySetValue(AutomationElement element, string value, out PeekuError error)
  {
    error = PeekuErrors.Create(PeekuErrorCode.NotSupported, "Element does not support value pattern.");

    if (!element.Patterns.Value.IsSupported)
    {
      return false;
    }

    try
    {
      element.Patterns.Value.Pattern.SetValue(value ?? "");
      return true;
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA set value failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }
  }

  private static bool TryReadValue(AutomationElement element, out string? value, out PeekuError error)
  {
    error = PeekuErrors.Create(PeekuErrorCode.NotSupported, "Element does not support value pattern.");
    value = null;

    if (!element.Patterns.Value.IsSupported)
    {
      return false;
    }

    try
    {
      value = element.Patterns.Value.Pattern.Value;
      return true;
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA read value failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      value = null;
      return false;
    }
  }

  private static bool TryIsValuePatternSupported(AutomationElement element, out bool isSupported, out PeekuError? error)
  {
    isSupported = false;
    error = null;
    try
    {
      isSupported = element.Patterns.Value.IsSupported;
      return true;
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA value pattern probe failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }
  }

  internal static ValueVerificationOutcome EvaluateValueVerification(
    string operation,
    bool valuePatternSupported,
    string expectedValue,
    string? actualValue)
  {
    var expected = expectedValue ?? "";

    if (!valuePatternSupported)
    {
      const string warning = "ValuePattern is not supported; verification skipped.";
      return new(
        Ok: true,
        Evidence: CreateValueActionEvidence(
          operation,
          valuePatternSupported: false,
          expectedValue: expected,
          actualValue: actualValue,
          status: "unsupported",
          verificationPerformed: false,
          verificationMatched: null),
        Warning: warning);
    }

    var actual = actualValue ?? "";
    if (string.Equals(expected, actual, StringComparison.Ordinal))
    {
      return new(
        Ok: true,
        Evidence: CreateValueActionEvidence(
          operation,
          valuePatternSupported: true,
          expectedValue: expected,
          actualValue: actual,
          status: "matched",
          verificationPerformed: true,
          verificationMatched: true));
    }

    return new(
      Ok: false,
      Evidence: CreateValueActionEvidence(
        operation,
        valuePatternSupported: true,
        expectedValue: expected,
        actualValue: actual,
        status: "mismatch",
        verificationPerformed: true,
        verificationMatched: false),
      Error: CreateValueMismatchError(operation, expected, actual));
  }

  internal static JsonElement CreateValueActionEvidence(
    string operation,
    bool valuePatternSupported,
    string expectedValue,
    string? actualValue,
    string status,
    bool verificationPerformed,
    bool? verificationMatched)
    => JsonSerializer.SerializeToElement(new
    {
      operation = string.IsNullOrWhiteSpace(operation) ? "value-write" : operation,
      status,
      valuePatternSupported,
      expectedValue = expectedValue ?? "",
      actualValue,
      verificationPerformed,
      verificationMatched,
    });

  private static PeekuError CreateValueMismatchError(string operation, string expectedValue, string actualValue)
    => PeekuErrors.Create(
      PeekuErrorCode.Internal,
      $"{operation} verification failed: expected '{expectedValue}', actual '{actualValue}'.",
      new
      {
        operation,
        expectedValue,
        actualValue,
      });

  internal readonly record struct ValueVerificationOutcome(
    bool Ok,
    JsonElement Evidence,
    PeekuError? Error = null,
    string? Warning = null);
}
