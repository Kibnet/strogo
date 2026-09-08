module Candidate {
  newtype {:nativeType "long"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0
  function StrictAnd(left: bool, right: bool): bool { left && right }
  function StrictOr(left: bool, right: bool): bool { left || right }
  function M000(p000: I64): I64
    requires (p000 <= 9223372036854775806)
  {
    (var e000: I64 := (p000 + 1); e000)
  }
  method F000(p000: I64) returns (result: I64)
    requires (p000 <= 9223372036854775806)
    ensures result == M000(p000)
  {
    var v000: I64 := 1;
    var v001: I64 := p000 + v000;
    result := v001;
  }
}
