module Candidate {
  newtype {:nativeType "long"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0
  type S000 = s: seq<I64> | |s| <= 4 witness []
  datatype R000 = C000(R000F000: I64, R000F001: S000)
  function StrictAnd(left: bool, right: bool): bool { left && right }
  function StrictOr(left: bool, right: bool): bool { left || right }
  function M000(p000: I64): R000
    requires (p000 <= 9223372036854775806)
  {
    C000(2, (var e002: S000 := ((var e000: S000 := ([] + [p000]); e000) + [(var e001: I64 := (p000 + 1); e001)]); e002))
  }
  method F000(p000: I64) returns (result: R000)
    requires (p000 <= 9223372036854775806)
    ensures result == M000(p000)
  {
    var v000: S000 := [];
    assert |v000| < 4;
    var v001: S000 := v000 + [p000];
    var v002: I64 := 1;
    var v003: I64 := p000 + v002;
    assert |v001| < 4;
    var v004: S000 := v001 + [v003];
    var v005: I64 := |v004| as I64;
    var v006: R000 := C000(v005, v004);
    result := v006;
  }
}
