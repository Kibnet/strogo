module Candidate {
  newtype {:nativeType "long"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0
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
  predicate Q000(p000: I64)
  {
    (p000 <= 9223372036854775806)
  }
  function M000(p000: I64): I64
    requires Q000(p000)
  {
    (var e000: I64 := (p000 + 1); e000)
  }
  predicate Q001(p000: bool, p001: I64, p002: I64)
  {
    StrictAnd((p001 <= 9223372036854775806), (p002 <= 9223372036854775806))
  }
  function M001(p000: bool, p001: I64, p002: I64): I64
    requires Q001(p000, p001, p002)
  {
    (if p000 then (var e000: I64 := (p001 + 1); e000) else (var e001: I64 := (p002 + 1); e001))
  }
  method F000(p000: I64) returns (result: I64)
    requires Q000(p000)
    ensures result == M000(p000)
  {
    var v000: I64 := 1;
    assert -9223372036854775808 <= (p000 as int) + (v000 as int) <= 9223372036854775807;
    var v001: I64 := p000 + v000;
    result := v001;
  }
  method F001(p000: bool, p001: I64, p002: I64) returns (result: I64)
    requires Q001(p000, p001, p002)
    ensures result == M001(p000, p001, p002)
  {
    var v000: bool := false;
    var v001: bool := p001 == p001;
    var v002: bool := p000 && v001;
    var v003: bool := !v002;
    var v004: bool := !v003;
    var v005: bool := v004 || v000;
    var v006: I64;
    if v005 {
      var v007: I64 := F000(p002);
      v006 := v007;
    } else {
      var v008: I64 := F000(p001);
      v006 := v008;
    }
    result := v006;
  }
}
