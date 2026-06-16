using peeku;
using Xunit;

namespace peeku.Core.Tests;

public sealed class ObserveEventTokensTests
{
  public static IEnumerable<object[]> AllSubsets()
  {
    // 0..7 covers every combination of the three bits (None .. All).
    for (var bits = 0; bits <= (int)ObserveEventSet.All; bits++)
    {
      yield return new object[] { (ObserveEventSet)bits };
    }
  }

  [Theory]
  [MemberData(nameof(AllSubsets))]
  public void Decode_IsExactInverseOf_Encode(ObserveEventSet set)
  {
    var roundTripped = ObserveEventTokens.Decode(ObserveEventTokens.Encode(set));
    Assert.Equal(set, roundTripped);
  }

  [Fact]
  public void Encode_UsesCanonicalOrder()
  {
    Assert.Equal(new[] { "structure", "property", "focus" }, ObserveEventTokens.Encode(ObserveEventSet.All));
    Assert.Equal(new[] { "focus" }, ObserveEventTokens.Encode(ObserveEventSet.Focus));
    Assert.Empty(ObserveEventTokens.Encode(ObserveEventSet.None));
  }

  [Fact]
  public void Decode_Property_StaysProperty_NoCollapseToStructure()
  {
    // Regression: the old enum→array / array→enum pair was asymmetric and silently
    // collapsed Property into Structure. Property must round-trip as itself.
    Assert.Equal(ObserveEventSet.Property, ObserveEventTokens.Decode(new[] { "property" }));
    Assert.Equal(ObserveEventSet.Structure, ObserveEventTokens.Decode(new[] { "structure" }));
  }

  [Fact]
  public void Decode_IsCaseInsensitive_AndIgnoresUnknownAndWhitespace()
  {
    Assert.Equal(
      ObserveEventSet.Focus | ObserveEventSet.Structure,
      ObserveEventTokens.Decode(new[] { " FOCUS ", "Structure", "bogus", "" }));
  }

  [Fact]
  public void FlagsValues_ArePowersOfTwo_AndAllIsTheUnion()
  {
    Assert.Equal(0, (int)ObserveEventSet.None);
    Assert.Equal(1, (int)ObserveEventSet.Structure);
    Assert.Equal(2, (int)ObserveEventSet.Property);
    Assert.Equal(4, (int)ObserveEventSet.Focus);
    Assert.Equal(
      ObserveEventSet.Structure | ObserveEventSet.Property | ObserveEventSet.Focus,
      ObserveEventSet.All);
  }
}
