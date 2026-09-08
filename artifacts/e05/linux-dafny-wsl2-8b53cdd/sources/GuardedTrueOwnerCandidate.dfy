module Candidate {
  newtype {:nativeType "long"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0
  function StrictAnd(left: bool, right: bool): bool { left && right }
  function StrictOr(left: bool, right: bool): bool { left || right }
  function M000(p000: I64): bool
    requires true
  {
    (if true then true else ((var e000: I64 := (p000 + 1); e000) == 0))
  }
  method F000(p000: I64) returns (result: bool)
    requires true
    ensures result == M000(p000)
  {
    var v000: bool := true;
    result := v000;
  }
}
