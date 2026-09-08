module Candidate {
  newtype {:nativeType "long"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0
  type S000 = s: seq<I64> | |s| <= 1 witness []
  type S001 = s: seq<I64> | |s| <= 8 witness []
  datatype R000 = C000(R000F000: S001, R000F001: I64, R000F002: I64)
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
  predicate Q000(p000: bool, p001: I64)
  {
    StrictOr((!p000), (p001 <= 9223372036854775806))
  }
  function M000(p000: bool, p001: I64): I64
    requires Q000(p000, p001)
  {
    (if p000 then (var e000: I64 := (p001 + 1); e000) else p001)
  }
  predicate Q001(p000: R000)
  {
    true
  }
  function M001(p000: R000): R000
    requires Q001(p000)
  {
    p000
  }
  predicate Q002(p000: S000)
  {
    true
  }
  function M002(p000: S000): I64
    requires Q002(p000)
  {
    (if ((|p000| as I64) == 0) then 0 else p000[0 as int])
  }
  predicate Q003(p000: I64)
  {
    (p000 <= 9223372036854775806)
  }
  function M003(p000: I64): I64
    requires Q003(p000)
  {
    (var e000: I64 := (p000 + 1); e000)
  }
  predicate Q004(p000: S001)
  {
    (forall q000: int {:trigger p000[q000]} :: 0 <= q000 < |p000| ==> StrictAnd((-1000000 <= p000[q000]), (p000[q000] <= 1000000)))
  }
  function P004(p000: S001, n: int): R000
    requires Q004(p000) && 0 <= n <= |p000|
    ensures StrictAnd(((|P004(p000, n).R000F000| as I64) == n as I64), StrictAnd((-8000000 <= P004(p000, n).R000F002), StrictAnd((P004(p000, n).R000F002 <= 8000000), StrictAnd((0 <= P004(p000, n).R000F001), (P004(p000, n).R000F001 <= n as I64)))))
    decreases n
  {
    if n == 0 then C000([], 0, 0) else C000(P004(p000, n - 1).R000F000 + [p000[n - 1]], (var e001: I64 := (P004(p000, n - 1).R000F001 + (if (!(0 <= p000[n - 1])) then 1 else 0)); e001), (var e000: I64 := (P004(p000, n - 1).R000F002 + p000[n - 1]); e000))
  }
  lemma L004(p000: S001, n: int)
    requires Q004(p000) && 0 <= n <= |p000|
    ensures StrictAnd(((|P004(p000, n).R000F000| as I64) == n as I64), StrictAnd((-8000000 <= P004(p000, n).R000F002), StrictAnd((P004(p000, n).R000F002 <= 8000000), StrictAnd((0 <= P004(p000, n).R000F001), (P004(p000, n).R000F001 <= n as I64)))))
    decreases n
  {
    if n == 0 {
      assert StrictAnd(((|P004(p000, 0).R000F000| as I64) == 0 as I64), StrictAnd((-8000000 <= P004(p000, 0).R000F002), StrictAnd((P004(p000, 0).R000F002 <= 8000000), StrictAnd((0 <= P004(p000, 0).R000F001), (P004(p000, 0).R000F001 <= 0 as I64)))));
    } else {
      L004(p000, n - 1);
      assert StrictAnd(((|P004(p000, n).R000F000| as I64) == n as I64), StrictAnd((-8000000 <= P004(p000, n).R000F002), StrictAnd((P004(p000, n).R000F002 <= 8000000), StrictAnd((0 <= P004(p000, n).R000F001), (P004(p000, n).R000F001 <= n as I64)))));
    }
  }
  method F000(p000: bool, p001: I64) returns (result: I64)
    requires Q000(p000, p001)
    ensures result == M000(p000, p001)
  {
    var v000: I64;
    if p000 {
      var v001: I64 := F003(p001);
      v000 := v001;
    } else {
      v000 := p001;
    }
    result := v000;
  }
  method F001(p000: R000) returns (result: R000)
    requires Q001(p000)
    ensures result == M001(p000)
  {
    result := p000;
  }
  method F002(p000: S000) returns (result: I64)
    requires Q002(p000)
    ensures result == M002(p000)
  {
    var v000: I64 := |p000| as I64;
    var v001: I64 := 0;
    var v002: bool := v000 == v001;
    var v003: I64;
    if v002 {
      v003 := v001;
    } else {
      assert 0 <= (v001 as int) < |p000|;
      var v004: I64 := p000[v001 as int];
      v003 := v004;
    }
    result := v003;
  }
  method F003(p000: I64) returns (result: I64)
    requires Q003(p000)
    ensures result == M003(p000)
  {
    var v000: I64 := 1;
    assert -9223372036854775808 <= (p000 as int) + (v000 as int) <= 9223372036854775807;
    var v001: I64 := p000 + v000;
    result := v001;
  }
  method F004(p000: S001) returns (result: R000)
    requires Q004(p000)
    ensures result == P004(p000, |p000|)
  {
    var v000: S001 := [];
    var v001: I64 := 0;
    var v002: I64 := 0;
    var v003: R000 := C000(v000, v001, v002);
    assert p000 == p000;
    assert v003 == C000([], 0, 0);
    var v004: R000 := v003;
    var i004: int := 0;
    L004(p000, 0);
    while i004 < |p000|
      invariant 0 <= i004 <= |p000|
      invariant p000 == p000 && v003 == C000([], 0, 0)
      invariant v004 == P004(p000, i004)
      invariant StrictAnd(((|v004.R000F000| as I64) == i004 as I64), StrictAnd((-8000000 <= v004.R000F002), StrictAnd((v004.R000F002 <= 8000000), StrictAnd((0 <= v004.R000F001), (v004.R000F001 <= i004 as I64)))))
      decreases |p000| - i004
    {
      var v005: S001 := v004.R000F000;
      var v006: I64 := v004.R000F001;
      var v007: I64 := v004.R000F002;
      assert |v005| < 8;
      var v008: S001 := v005 + [p000[i004]];
      assert -9223372036854775808 <= (v007 as int) + (p000[i004] as int) <= 9223372036854775807;
      var v009: I64 := v007 + p000[i004];
      var v010: I64 := 0;
      var v011: bool := v010 <= p000[i004];
      var v012: bool := !v011;
      var v013: I64;
      if v012 {
        var v014: I64 := 1;
        assert -9223372036854775808 <= (v006 as int) + (v014 as int) <= 9223372036854775807;
        var v015: I64 := v006 + v014;
        v013 := v015;
      } else {
        v013 := v006;
      }
      var v016: R000 := C000(v008, v013, v009);
      v004 := v016;
      i004 := i004 + 1;
      L004(p000, i004);
      assert v004 == P004(p000, i004);
    }
    result := v004;
  }
}
