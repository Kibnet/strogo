module Candidate {
  newtype {:nativeType "long"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0
  type S000 = s: seq<R000> | |s| <= 4 witness []
  datatype R000 = C000(R000F000: I64)
  function StrictAnd(left: bool, right: bool): bool { left && right }
  function StrictOr(left: bool, right: bool): bool { left || right }
  function M000(p000: S000): I64
    requires true
  {
    (|(var e000: S000 := (p000 + [C000(0)]); e000)| as I64)
  }
  method F000(p000: S000) returns (result: I64)
    requires true
    ensures result == M000(p000)
  {
    var v000: I64 := |p000| as I64;
    var v001: I64 := 1;
    var v002: I64 := v000 + v001;
    result := v002;
  }
}
