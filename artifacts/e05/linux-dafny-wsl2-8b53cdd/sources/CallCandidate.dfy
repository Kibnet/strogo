module Candidate {
  newtype {:nativeType "long"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0
  method F000(p000: I64) returns (result: I64)
  {
    result := p000;
  }
  method F001(p000: I64) returns (result: I64)
  {
    var v000: I64 := F000(p000);
    result := v000;
  }
}
