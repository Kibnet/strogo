module Candidate {
  newtype {:nativeType "long"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0
  method F000(p000: bool, p001: I64, p002: I64) returns (result: I64)
  {
    var v000: I64;
    if p000 {
      var v001: I64 := 9223372036854775807;
      var v002: bool := p001 == v001;
      var v003: I64;
      if v002 {
        v003 := p001;
      } else {
        var v004: I64 := 1;
        var v005: I64 := p001 + v004;
        v003 := v005;
      }
      v000 := v003;
    } else {
      v000 := p002;
    }
    result := v000;
  }
}
