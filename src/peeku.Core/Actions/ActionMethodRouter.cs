namespace peeku;

internal static class ActionMethodRouter
{
  public static ActionMethod Route(
    ActionMethod requested,
    bool uiaSupported,
    bool inputSupported,
    out PeekuError? error)
  {
    error = null;

    if (!Enum.IsDefined(typeof(ActionMethod), requested))
    {
      error = PeekuErrors.Create(
        PeekuErrorCode.InvalidArgument,
        "Action method is invalid.",
        new { method = (int)requested });
      return ActionMethod.Auto;
    }

    if (requested == ActionMethod.Auto)
    {
      if (uiaSupported)
      {
        return ActionMethod.Uia;
      }

      if (inputSupported)
      {
        return ActionMethod.Input;
      }

      error = PeekuErrors.Create(PeekuErrorCode.NotSupported, "No supported action methods available.");
      return ActionMethod.Auto;
    }

    if (requested == ActionMethod.Uia)
    {
      if (uiaSupported)
      {
        return ActionMethod.Uia;
      }

      error = PeekuErrors.Create(PeekuErrorCode.NotSupported, "UIA method not supported.");
      return requested;
    }

    if (requested == ActionMethod.Input)
    {
      if (inputSupported)
      {
        return ActionMethod.Input;
      }

      error = PeekuErrors.Create(PeekuErrorCode.NotSupported, "Input method not supported.");
      return requested;
    }

    error = PeekuErrors.Create(PeekuErrorCode.NotSupported, "Action method not supported.");
    return requested;
  }
}

