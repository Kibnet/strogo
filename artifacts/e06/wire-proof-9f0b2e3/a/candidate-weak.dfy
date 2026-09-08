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
module PortableWrapper {
  import opened Candidate

  datatype WireValue = WI64(i64Value: int) | WBool(boolValue: bool) | WRecord(recordTypeId: string, recordFields: seq<WireField>) | WSeq(sequenceItems: seq<WireValue>)
  datatype WireField = WireField(fieldId: string, fieldValue: WireValue)
  datatype WireDetailValue = DText(text: string) | DNat(value: nat)
  datatype WireDetail = WireDetail(key: string, detailValue: WireDetailValue)
  datatype WireOutcome = Success(value: WireValue) | Refusal(code: string, locus: string, details: seq<WireDetail>)

  predicate IsI64(value: WireValue) {
    value.WI64? && -9223372036854775808 <= value.i64Value <= 9223372036854775807
  }

  function ToI64(value: WireValue): I64
    requires IsI64(value)
  {
    value.i64Value as I64
  }

  predicate IsI64Sequence(value: WireValue, capacity: nat) {
    value.WSeq? && |value.sequenceItems| <= capacity &&
      forall index: int :: 0 <= index < |value.sequenceItems| ==> IsI64(value.sequenceItems[index])
  }

  function ToI64Sequence(items: seq<WireValue>): seq<I64>
    requires forall index: int :: 0 <= index < |items| ==> IsI64(items[index])
    ensures |ToI64Sequence(items)| == |items|
    decreases |items|
  {
    if |items| == 0 then [] else [ToI64(items[0])] + ToI64Sequence(items[1..])
  }

  function FromI64Sequence(items: seq<I64>): WireValue {
    WSeq(seq(|items|, index requires 0 <= index < |items| => WI64(items[index] as int)))
  }

  function SequenceMismatchLocus(value: WireValue, capacity: nat, locus: string): string
    requires !IsI64Sequence(value, capacity)
  {
    if !value.WSeq? || |value.sequenceItems| > capacity then locus
    else if |value.sequenceItems| > 0 && !IsI64(value.sequenceItems[0]) then locus + "/items/0"
    else if |value.sequenceItems| > 1 && !IsI64(value.sequenceItems[1]) then locus + "/items/1"
    else if |value.sequenceItems| > 2 && !IsI64(value.sequenceItems[2]) then locus + "/items/2"
    else if |value.sequenceItems| > 3 && !IsI64(value.sequenceItems[3]) then locus + "/items/3"
    else if |value.sequenceItems| > 4 && !IsI64(value.sequenceItems[4]) then locus + "/items/4"
    else if |value.sequenceItems| > 5 && !IsI64(value.sequenceItems[5]) then locus + "/items/5"
    else if |value.sequenceItems| > 6 && !IsI64(value.sequenceItems[6]) then locus + "/items/6"
    else if |value.sequenceItems| > 7 && !IsI64(value.sequenceItems[7]) then locus + "/items/7"
    else locus
  }

  predicate IsSummary(value: WireValue) {
    value.WRecord? && value.recordTypeId == "Summary" && |value.recordFields| == 3 &&
      value.recordFields[0].fieldId == "echo" && IsI64Sequence(value.recordFields[0].fieldValue, 8) &&
      value.recordFields[1].fieldId == "negativeCount" && IsI64(value.recordFields[1].fieldValue) &&
      value.recordFields[2].fieldId == "sum" && IsI64(value.recordFields[2].fieldValue)
  }

  function SummaryMismatchLocus(value: WireValue, locus: string): string
    requires !IsSummary(value)
  {
    if !value.WRecord? || value.recordTypeId != "Summary" || |value.recordFields| != 3 then locus
    else if value.recordFields[0].fieldId != "echo" then locus
    else if !IsI64Sequence(value.recordFields[0].fieldValue, 8) then SequenceMismatchLocus(value.recordFields[0].fieldValue, 8, locus + "/fields/0/value")
    else if value.recordFields[1].fieldId != "negativeCount" then locus
    else if !IsI64(value.recordFields[1].fieldValue) then locus + "/fields/1/value"
    else if value.recordFields[2].fieldId != "sum" then locus
    else if !IsI64(value.recordFields[2].fieldValue) then locus + "/fields/2/value"
    else locus
  }

  function ToSummary(value: WireValue): R000
    requires IsSummary(value)
  {
    C000(
      ToI64Sequence(value.recordFields[0].fieldValue.sequenceItems),
      ToI64(value.recordFields[1].fieldValue),
      ToI64(value.recordFields[2].fieldValue))
  }

  function FromSummary(value: R000): WireValue {
    WRecord("Summary", [
      WireField("echo", FromI64Sequence(value.R000F000)),
      WireField("negativeCount", WI64(value.R000F001 as int)),
      WireField("sum", WI64(value.R000F002 as int))])
  }

  function ArityRefusal(expected: nat, actual: nat): WireOutcome {
    Refusal("ArityMismatch", "$/arguments", [WireDetail("expected", DNat(expected)), WireDetail("actual", DNat(actual))])
  }

  function TypeRefusal(locus: string): WireOutcome {
    Refusal("RuntimeTypeMismatch", locus, [])
  }

  method Invoke(functionId: string, arguments: seq<WireValue>) returns (outcome: WireOutcome)
    requires true
  {
    if functionId != "adjust" && functionId != "echoSummary" && functionId != "headOrZero" && functionId != "increment" && functionId != "summarize" {
      outcome := Refusal("UnknownFunction", "$/functionId", [WireDetail("functionId", DText(functionId))]);
    } else if functionId == "adjust" {
      if |arguments| != 2 {
        outcome := ArityRefusal(2, |arguments|);
      } else if !arguments[0].WBool? {
        outcome := TypeRefusal("$/arguments/0");
      } else if !IsI64(arguments[1]) {
        outcome := TypeRefusal("$/arguments/1");
      } else {
        var increase := arguments[0].boolValue;
        var x := ToI64(arguments[1]);
        if !Q000(increase, x) {
          outcome := Refusal("OwnerPreconditionFailed", "function/adjust/requires", []);
        } else {
          var result := F000(increase, x);
          assert result == M000(increase, x);
          outcome := Success(WI64(result as int));
        }
      }
    } else if functionId == "echoSummary" {
      if |arguments| != 1 {
        outcome := ArityRefusal(1, |arguments|);
      } else if !IsSummary(arguments[0]) {
        outcome := TypeRefusal(SummaryMismatchLocus(arguments[0], "$/arguments/0"));
      } else {
        var summary := ToSummary(arguments[0]);
        assert Q001(summary);
        var result := F001(summary);
        assert result == M001(summary);
        outcome := Success(FromSummary(result));
      }
    } else if functionId == "headOrZero" {
      if |arguments| != 1 {
        outcome := ArityRefusal(1, |arguments|);
      } else if !IsI64Sequence(arguments[0], 1) {
        outcome := TypeRefusal(SequenceMismatchLocus(arguments[0], 1, "$/arguments/0"));
      } else {
        var items: S000 := ToI64Sequence(arguments[0].sequenceItems);
        assert Q002(items);
        var result := F002(items);
        assert result == M002(items);
        outcome := Success(WI64(result as int));
      }
    } else if functionId == "increment" {
      if |arguments| != 1 {
        outcome := ArityRefusal(1, |arguments|);
      } else if !IsI64(arguments[0]) {
        outcome := TypeRefusal("$/arguments/0");
      } else {
        var x := ToI64(arguments[0]);
        if !Q003(x) {
          outcome := Refusal("OwnerPreconditionFailed", "function/increment/requires", []);
        } else {
          var result := F003(x);
          assert result == M003(x);
          outcome := Success(WI64(result as int));
        }
      }
    } else {
      if |arguments| != 1 {
        outcome := ArityRefusal(1, |arguments|);
      } else if !IsI64Sequence(arguments[0], 8) {
        outcome := TypeRefusal(SequenceMismatchLocus(arguments[0], 8, "$/arguments/0"));
      } else {
        var items: S001 := ToI64Sequence(arguments[0].sequenceItems);
        if !Q004(items) {
          outcome := Refusal("OwnerPreconditionFailed", "function/summarize/requires", []);
        } else {
          var result := F004(items);
          assert result == P004(items, |items|);
          outcome := Success(FromSummary(result));
        }
      }
    }
  }
}
