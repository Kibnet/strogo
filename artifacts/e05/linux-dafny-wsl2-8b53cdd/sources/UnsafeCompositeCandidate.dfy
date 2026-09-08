module Candidate {
  newtype {:nativeType "long"} I64 = x: int | -9223372036854775808 <= x <= 9223372036854775807 witness 0
  type S000 = s: seq<I64> | |s| <= 2 witness []
  datatype R000 = C000(R000F000: I64, R000F001: S000)
  method F000(p000: S000, p001: I64) returns (result: S000)
  {
    assert |p000| < 2;
    var v000: S000 := p000 + [p001];
    result := v000;
  }
  method F001(p000: S000, p001: I64) returns (result: I64)
  {
    assert 0 <= (p001 as int) < |p000|;
    var v000: I64 := p000[p001 as int];
    result := v000;
  }
  method F002(p000: I64) returns (result: R000)
  {
    var v000: S000 := [];
    assert |v000| < 2;
    var v001: S000 := v000 + [p000];
    var v002: R000 := C000(p000, v001);
    result := v002;
  }
  method F003(p000: R000) returns (result: I64)
  {
    var v000: I64 := p000.R000F000;
    result := v000;
  }
  method F004(p000: S000) returns (result: I64)
  {
    var v000: I64 := |p000| as I64;
    result := v000;
  }
}
