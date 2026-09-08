module Candidate {
  newtype {:nativeType "long"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0
  type S000 = s: seq<I64> | |s| <= 4 witness []
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
  predicate Q000(p000: S000)
  {
    StrictAnd((forall q000: int {:trigger p000[q000]} :: 0 <= q000 < |p000| ==> (0 <= p000[q000])), (SumI64(p000) <= 9223372036854775807))
  }
  function P000(p000: S000, n: int): int
    requires Q000(p000) && 0 <= n <= |p000|
    decreases n
  {
    if n == 0 then (0 as int) else (P000(p000, n - 1) + (p000[n - 1] as int))
  }
  lemma E000(p000: S000, n: int)
    requires Q000(p000) && 0 <= n <= |p000|
    ensures P000(p000, n) == PrefixSumI64(p000, n)
    decreases n
  {
    if n > 0 {
      E000(p000, n - 1);
      PrefixSumStep(p000, n - 1);
    }
  }
  lemma G000(p000: S000, n: int)
    requires Q000(p000) && 0 <= n <= |p000|
    ensures -9223372036854775808 <= P000(p000, n) <= 9223372036854775807
  {
    E000(p000, n);
    PrefixSumBounds(p000, n);
  }
  lemma L000(p000: S000, n: int)
    requires Q000(p000) && 0 <= n <= |p000|
    ensures false
    decreases n
  {
    E000(p000, n);
    PrefixSumBounds(p000, n);
    G000(p000, n);
    if n == 0 {
      assert false;
    } else {
      L000(p000, n - 1);
      assert false;
    }
  }
  method F000(p000: S000) returns (result: I64)
    requires Q000(p000)
    ensures (result as int) == P000(p000, |p000|)
  {
    var v000: I64 := 0;
    assert p000 == p000;
    assert v000 == 0;
    var v001: I64 := v000;
    var i000: int := 0;
    L000(p000, 0);
    while i000 < |p000|
      invariant 0 <= i000 <= |p000|
      invariant p000 == p000 && v000 == 0
      invariant (v001 as int) == P000(p000, i000)
      invariant false
      decreases |p000| - i000
    {
      G000(p000, i000 + 1);
      assert -9223372036854775808 <= (v001 as int) + (p000[i000] as int) <= 9223372036854775807;
      var v002: I64 := v001 + p000[i000];
      v001 := v002;
      i000 := i000 + 1;
      L000(p000, i000);
      assert (v001 as int) == P000(p000, i000);
    }
    result := v001;
  }
}
