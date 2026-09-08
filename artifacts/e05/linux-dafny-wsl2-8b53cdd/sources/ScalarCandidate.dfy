module Candidate {
  newtype {:nativeType "long"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0
  method F000(p000: bool, p001: bool) returns (result: bool)
  {
    var v000: bool := !p000;
    var v001: I64 := 1;
    var v002: bool := true;
    var v003: I64 := 0;
    var v004: I64 := v001 - v003;
    var v005: bool := v004 == v001;
    var v006: bool := v003 <= v004;
    var v007: bool := v006 && v005;
    var v008: bool := v007 && v002;
    var v009: bool := v008 && v000;
    var v010: bool := v009 || p001;
    result := v010;
  }
}
