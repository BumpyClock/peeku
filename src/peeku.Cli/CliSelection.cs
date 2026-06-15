using System.CommandLine;
using System.CommandLine.Parsing;
using peeku;

namespace peeku.Cli;

/// <summary>
/// Shared element-selection options (<c>--ref</c>/<c>--snapshotId</c>/<c>--selector</c>/<c>--live</c>)
/// plus an optional positional <c>QUERY</c> argument so the common path doesn't require
/// <c>--selector</c> (e.g. <c>peeku click "OK"</c>). Precedence: <c>--ref</c> &gt; <c>--selector</c> &gt; positional.
/// </summary>
internal sealed record SelectionOptions(
  Option<string?> Ref,
  Option<string?> SnapshotId,
  Option<string?> Selector,
  Option<bool> Live,
  Argument<string?>? Query);

internal static class CliSelection
{
  /// <summary>
  /// Adds the selection options to <paramref name="cmd"/>. When <paramref name="withPositionalQuery"/>
  /// is true, also adds an optional positional <c>query</c> argument (arity ZeroOrOne) that, when
  /// provided and neither <c>--ref</c> nor <c>--selector</c> is given, becomes the selector expression.
  /// </summary>
  internal static SelectionOptions AddTo(Command cmd, bool requireExactlyOne = true, bool withPositionalQuery = true)
  {
    var refOpt = new Option<string?>("--ref") { Description = "Element refId" };
    var snapshotIdOpt = new Option<string?>("--snapshotId") { Description = "Optional snapshotId for elementRef" };
    var selectorOpt = new Option<string?>("--selector") { Description = "Selector expression" };
    var liveOpt = new Option<bool>("--live") { Description = "Use live UIA evaluation for selector" };
    liveOpt.DefaultValueFactory = _ => false;

    Argument<string?>? queryArg = null;
    if (withPositionalQuery)
    {
      queryArg = new Argument<string?>("query")
      {
        Description = "Selector expression (positional shorthand for --selector)",
        Arity = ArgumentArity.ZeroOrOne,
      };
    }

    cmd.Add(refOpt);
    cmd.Add(snapshotIdOpt);
    cmd.Add(selectorOpt);
    cmd.Add(liveOpt);
    if (queryArg is not null)
    {
      cmd.Add(queryArg);
    }

    return new SelectionOptions(refOpt, snapshotIdOpt, selectorOpt, liveOpt, queryArg);
  }

  /// <summary>
  /// Resolves the selection into an <see cref="ElementRef"/> or <see cref="Selector"/>.
  /// Precedence: <c>--ref</c> &gt; <c>--selector</c> &gt; positional <c>query</c>.
  /// Returns false (with a message in <paramref name="error"/>) on conflicting/empty input.
  /// </summary>
  internal static bool TryParse(
    ParseResult parse,
    SelectionOptions o,
    out ElementRef? elementRef,
    out Selector? selector,
    out string? error)
  {
    elementRef = null;
    selector = null;
    error = null;

    var refId = parse.GetValue(o.Ref);
    var snapshotId = parse.GetValue(o.SnapshotId);
    var selectorExpr = parse.GetValue(o.Selector);
    var positional = o.Query is null ? null : parse.GetValue(o.Query);
    var live = parse.GetValue(o.Live);

    var hasRef = !string.IsNullOrWhiteSpace(refId);
    var hasSelector = !string.IsNullOrWhiteSpace(selectorExpr);
    var hasPositional = !string.IsNullOrWhiteSpace(positional);

    if (hasPositional && hasSelector)
    {
      error = "Provide a positional query or --selector, not both.";
      return false;
    }

    // Positional is selector shorthand only when no explicit --selector was given.
    var effectiveSelector = hasSelector ? selectorExpr : (hasPositional ? positional : null);
    var hasEffectiveSelector = !string.IsNullOrWhiteSpace(effectiveSelector);

    if (hasRef == hasEffectiveSelector)
    {
      error = "Provide exactly one of --ref, --selector, or a positional query.";
      return false;
    }

    if (!string.IsNullOrWhiteSpace(snapshotId) && !hasRef)
    {
      error = "--snapshotId requires --ref.";
      return false;
    }

    if (hasRef)
    {
      elementRef = new ElementRef(refId!.Trim(), string.IsNullOrWhiteSpace(snapshotId) ? null : snapshotId!.Trim());
      return true;
    }

    selector = new Selector((effectiveSelector ?? "").Trim(), PreferCachedSnapshot: !live);
    return true;
  }

  /// <summary>
  /// Resolves an OPTIONAL element selection: both <c>--ref</c> and <c>--selector</c> may be null,
  /// in which case the command falls back to its target (e.g. <c>--app</c>). Used by <c>type</c>,
  /// whose positional is TEXT (not a selector), so it must not require a ref/selector. Errors only
  /// when both <c>--ref</c> and <c>--selector</c> are given, or <c>--snapshotId</c> is given without
  /// <c>--ref</c>.
  /// </summary>
  internal static bool TryParseOptionalElement(
    ParseResult parse,
    SelectionOptions o,
    out ElementRef? elementRef,
    out Selector? selector,
    out string? error)
  {
    elementRef = null;
    selector = null;
    error = null;

    var refId = parse.GetValue(o.Ref);
    var snapshotId = parse.GetValue(o.SnapshotId);
    var selectorExpr = parse.GetValue(o.Selector);
    var live = parse.GetValue(o.Live);

    var hasRef = !string.IsNullOrWhiteSpace(refId);
    var hasSelector = !string.IsNullOrWhiteSpace(selectorExpr);

    if (hasRef && hasSelector)
    {
      error = "Provide --ref or --selector, not both.";
      return false;
    }

    if (!string.IsNullOrWhiteSpace(snapshotId) && !hasRef)
    {
      error = "--snapshotId requires --ref.";
      return false;
    }

    if (hasRef)
    {
      elementRef = new ElementRef(refId!.Trim(), string.IsNullOrWhiteSpace(snapshotId) ? null : snapshotId!.Trim());
      return true;
    }

    if (hasSelector)
    {
      selector = new Selector(selectorExpr!.Trim(), PreferCachedSnapshot: !live);
    }

    return true;
  }

  /// <summary>
  /// Resolves an OPTIONAL selection with positional support: precedence <c>--ref</c> &gt;
  /// <c>--selector</c> &gt; positional <c>query</c> (maps to selector). When none are given, returns
  /// both <c>elementRef</c> and <c>selector</c> as null so the command falls back to its target
  /// (e.g. <c>--app</c>) and Core targets the window's focused element. Errors only on the
  /// positional+<c>--selector</c> conflict or <c>--snapshotId</c> without <c>--ref</c>.
  /// </summary>
  internal static bool TryParseOptional(
    ParseResult parse,
    SelectionOptions o,
    out ElementRef? elementRef,
    out Selector? selector,
    out string? error)
  {
    elementRef = null;
    selector = null;
    error = null;

    var refId = parse.GetValue(o.Ref);
    var snapshotId = parse.GetValue(o.SnapshotId);
    var selectorExpr = parse.GetValue(o.Selector);
    var positional = o.Query is null ? null : parse.GetValue(o.Query);
    var live = parse.GetValue(o.Live);

    var hasRef = !string.IsNullOrWhiteSpace(refId);
    var hasSelector = !string.IsNullOrWhiteSpace(selectorExpr);
    var hasPositional = !string.IsNullOrWhiteSpace(positional);

    if (hasPositional && hasSelector)
    {
      error = "Provide a positional query or --selector, not both.";
      return false;
    }

    // Positional is selector shorthand only when no explicit --selector was given.
    var effectiveSelector = hasSelector ? selectorExpr : (hasPositional ? positional : null);
    var hasEffectiveSelector = !string.IsNullOrWhiteSpace(effectiveSelector);

    if (hasRef && hasEffectiveSelector)
    {
      error = "Provide --ref or --selector, not both.";
      return false;
    }

    if (!string.IsNullOrWhiteSpace(snapshotId) && !hasRef)
    {
      error = "--snapshotId requires --ref.";
      return false;
    }

    if (hasRef)
    {
      elementRef = new ElementRef(refId!.Trim(), string.IsNullOrWhiteSpace(snapshotId) ? null : snapshotId!.Trim());
      return true;
    }

    if (hasEffectiveSelector)
    {
      selector = new Selector(effectiveSelector!.Trim(), PreferCachedSnapshot: !live);
    }

    return true;
  }

  /// <summary>
  /// Resolves a selector-only selection (no <c>--ref</c>): explicit <c>--selector</c> wins, else the
  /// positional <c>query</c>. Used by <c>find</c>/<c>wait</c>/<c>watch</c> which take a selector but no ref.
  /// </summary>
  internal static bool TryParseSelectorOnly(
    ParseResult parse,
    Option<string?> selectorOpt,
    Argument<string?>? queryArg,
    Option<bool> liveOpt,
    out Selector? selector,
    out string? error)
  {
    selector = null;
    error = null;

    var selectorExpr = parse.GetValue(selectorOpt);
    var positional = queryArg is null ? null : parse.GetValue(queryArg);
    var live = parse.GetValue(liveOpt);

    var hasSelector = !string.IsNullOrWhiteSpace(selectorExpr);
    var hasPositional = !string.IsNullOrWhiteSpace(positional);

    if (hasSelector && hasPositional)
    {
      error = "Provide a positional query or --selector, not both.";
      return false;
    }

    var effective = hasSelector ? selectorExpr : (hasPositional ? positional : null);
    if (string.IsNullOrWhiteSpace(effective))
    {
      error = "Provide a selector via positional query or --selector.";
      return false;
    }

    selector = new Selector(effective!.Trim(), PreferCachedSnapshot: !live);
    return true;
  }
}
