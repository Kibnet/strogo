module Candidate {
  newtype {:nativeType "long"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0
  method F000(p000: I64) returns (result: I64)
  {
    var v000: I64 := 1;
    var v001: I64 := p000 + v000;
    result := v001;
  }
}
