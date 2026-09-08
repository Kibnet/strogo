module Candidate {
  newtype {:nativeType "long"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0
  type S000 = s: seq<I64> | |s| <= 256 witness []
  datatype R000 = C000(R000F000: S000, R000F001: I64)
  function StrictAnd(left: bool, right: bool): bool { left && right }
  function StrictOr(left: bool, right: bool): bool { left || right }
  function SumI64(s: seq<I64>): int
    decreases |s|
  {
    if |s| == 0 then 0 else SumI64(s[..|s|-1]) + (s[|s|-1] as int)
  }
  function PrefixSumI64(s: seq<I64>, n: int): int
    requires 0 <= n <= |s|
  {
    SumI64(s[..n])
  }
  lemma PrefixSumWhole(s: seq<I64>)
    ensures PrefixSumI64(s, |s|) == SumI64(s)
  {
    assert s[..|s|] == s;
  }
  lemma PrefixSumStep(s: seq<I64>, n: int)
    requires 0 <= n < |s|
    ensures PrefixSumI64(s, n + 1) == PrefixSumI64(s, n) + (s[n] as int)
  {
    assert (s[..n+1])[..n] == s[..n];
  }
  lemma SumNonnegative(s: seq<I64>)
    requires forall j: int :: 0 <= j < |s| ==> 0 <= s[j]
    ensures 0 <= SumI64(s)
    decreases |s|
  {
    if |s| > 0 { SumNonnegative(s[..|s|-1]); }
  }
  lemma PrefixSumBounds(s: seq<I64>, n: int)
    requires 0 <= n <= |s|
    requires forall j: int :: 0 <= j < |s| ==> 0 <= s[j]
    ensures 0 <= PrefixSumI64(s, n) <= SumI64(s)
    decreases |s|
  {
    if |s| == 0 {
    } else if n == |s| {
      PrefixSumWhole(s);
      SumNonnegative(s);
    } else {
      PrefixSumBounds(s[..|s|-1], n);
      assert s[..n] == (s[..|s|-1])[..n];
    }
  }
  predicate Q000(p000: S000, p001: I64)
  {
    StrictAnd((0 <= p001), (forall q000: int {:trigger p000[q000]} :: 0 <= q000 < |p000| ==> (1 <= p000[q000])))
  }
  function P000(p000: S000, p001: I64, n: int): R000
    requires Q000(p000, p001) && 0 <= n <= |p000|
    ensures StrictAnd(((|P000(p000, p001, n).R000F000| as I64) == n as I64), StrictAnd((0 <= P000(p000, p001, n).R000F001), StrictAnd((P000(p000, p001, n).R000F001 <= C000([], p001).R000F001), (p001 == C000([], p001).R000F001))))
    decreases n
  {
    if n == 0 then C000([], p001) else C000(P000(p000, p001, n - 1).R000F000 + [(if (p000[n - 1] <= P000(p000, p001, n - 1).R000F001) then p000[n - 1] else 0)], (var e000: I64 := (P000(p000, p001, n - 1).R000F001 - (if (p000[n - 1] <= P000(p000, p001, n - 1).R000F001) then p000[n - 1] else 0)); e000))
  }
  lemma L000(p000: S000, p001: I64, n: int)
    requires Q000(p000, p001) && 0 <= n <= |p000|
    ensures StrictAnd(((|P000(p000, p001, n).R000F000| as I64) == n as I64), StrictAnd((0 <= P000(p000, p001, n).R000F001), StrictAnd((P000(p000, p001, n).R000F001 <= C000([], p001).R000F001), (p001 == C000([], p001).R000F001))))
    decreases n
  {
    if n == 0 {
      assert StrictAnd(((|P000(p000, p001, 0).R000F000| as I64) == 0 as I64), StrictAnd((0 <= P000(p000, p001, 0).R000F001), StrictAnd((P000(p000, p001, 0).R000F001 <= C000([], p001).R000F001), (p001 == C000([], p001).R000F001))));
    } else {
      L000(p000, p001, n - 1);
      assert StrictAnd(((|P000(p000, p001, n).R000F000| as I64) == n as I64), StrictAnd((0 <= P000(p000, p001, n).R000F001), StrictAnd((P000(p000, p001, n).R000F001 <= C000([], p001).R000F001), (p001 == C000([], p001).R000F001))));
    }
  }
  method F000(p000: S000, p001: I64) returns (result: R000)
    requires Q000(p000, p001)
    ensures result == P000(p000, p001, |p000|)
  {
    var v000: S000 := [];
    var v001: R000 := C000(v000, p001);
    assert p000 == p000;
    assert v001 == C000([], p001);
    assert p001 == p001;
    var v002: R000 := v001;
    var i000: int := 0;
    L000(p000, p001, 0);
    while i000 < |p000|
      invariant 0 <= i000 <= |p000|
      invariant p000 == p000 && v001 == C000([], p001)
      invariant p001 == p001
      invariant v002 == P000(p000, p001, i000)
      invariant StrictAnd(((|v002.R000F000| as I64) == i000 as I64), StrictAnd((0 <= v002.R000F001), StrictAnd((v002.R000F001 <= v001.R000F001), (p001 == v001.R000F001))))
      decreases |p000| - i000
    {
      var v003: S000 := v002.R000F000;
      var v004: I64 := v002.R000F001;
      var v005: bool := p000[i000] <= v004;
      var v006: I64 := 0;
      var v007: I64;
      if v005 {
        v007 := p000[i000];
      } else {
        v007 := v006;
      }
      assert |v003| < 256;
      var v008: S000 := v003 + [v007];
      assert -9223372036854775808 <= (v004 as int) - (v007 as int) <= 9223372036854775807;
      var v009: I64 := v004 - v007;
      var v010: R000 := C000(v008, v009);
      v002 := v010;
      i000 := i000 + 1;
      L000(p000, p001, i000);
      assert v002 == P000(p000, p001, i000);
    }
    result := v002;
  }
}
