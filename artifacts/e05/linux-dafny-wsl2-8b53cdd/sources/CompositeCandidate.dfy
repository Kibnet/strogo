module Candidate {
  newtype {:nativeType "long"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0
  type S000 = s: seq<I64> | |s| <= 2 witness []
  datatype R000 = C000(R000F000: I64, R000F001: I64, R000F002: S000)
  method F000() returns (result: I64)
  {
    var v000: R000 := F001();
    var v001: I64 := v000.R000F001;
    result := v001;
  }
  method F001() returns (result: R000)
  {
    var v000: S000 := [];
    var v001: I64 := 1;
    assert |v000| < 2;
    var v002: S000 := v000 + [v001];
    var v003: I64 := 2;
    assert |v002| < 2;
    var v004: S000 := v002 + [v003];
    var v005: I64 := |v004| as I64;
    var v006: I64 := 0;
    assert 0 <= (v006 as int) < |v004|;
    var v007: I64 := v004[v006 as int];
    var v008: R000 := C000(v005, v007, v004);
    result := v008;
  }
}
