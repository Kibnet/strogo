using System.Collections.Immutable;
using System.Text;
using Kernel.Core;

namespace Strogo.Modules;

public static partial class ModulesDafnyLowerer
{
    public const string PortableWireToolchainIdentity = "strogo.portable-wire-dafny-lowering.v0.1";
    public static string PortableWireToolchainDigest { get; } = CanonicalJson.RawDigest(Encoding.UTF8.GetBytes(PortableWireToolchainIdentity));

    public static DafnyFoldLoweringResult LowerPortableValidation(ModuleIr module, OwnerBundleV04 ownerBundle)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(ownerBundle);
        if (module.ModuleId != "portable.validation.v01" || ownerBundle.BundleId != "portable.validation.owner-v01")
            throw new InvalidOperationException("PortableWireProfileMismatch");
        var functionIds = module.Functions.Select(function => function.Id).Order(StringComparer.Ordinal).ToArray();
        if (!functionIds.SequenceEqual(["adjust", "echoSummary", "headOrZero", "increment", "summarize"], StringComparer.Ordinal))
            throw new InvalidOperationException("PortableWireFunctionSetMismatch");

        var candidate = Lower(module, ownerBundle);
        var wrapper = PortableWireSource.Trim().ReplaceLineEndings("\n") + "\n";
        var sourceText = candidate.Source.TrimEnd('\r', '\n') + "\n" + wrapper;
        var normalizedText = Encoding.UTF8.GetString(candidate.NormalizedSourceBytes).TrimEnd('\r', '\n') + "\n" + wrapper;
        var source = Encoding.UTF8.GetBytes(sourceText);
        var normalized = Encoding.UTF8.GetBytes(normalizedText);
        var map = candidate.SourceMap.ToBuilder();
        var obligations = candidate.Obligations.ToBuilder();

        AddWireEvidence("wire/wrapper/invoke", "Invoke", "  method Invoke(functionId: string", "total-wrapper");
        AddWireEvidence("wire/wrapper/adjust/success", "F000", "        assert result == M000(increase, x);", "wire-success");
        AddWireEvidence("wire/wrapper/echoSummary/success", "F001", "        assert result == M001(summary);", "wire-success");
        AddWireEvidence("wire/wrapper/headOrZero/success", "F002", "        assert result == M002(items);", "wire-success");
        AddWireEvidence("wire/wrapper/increment/success", "F003", "        assert result == M003(x);", "wire-success");
        AddWireEvidence("wire/wrapper/summarize/success", "F004", "        assert result == P004(items, |items|);", "wire-success");

        var identityPayload = Encoding.UTF8.GetBytes($"strogo.portable-wire-proof.v0.1\n{PortableWireToolchainDigest}\n{candidate.ProofIdentity}\n{module.SourceDigest}\n{ownerBundle.BundleDigest}");
        return new DafnyFoldLoweringResult(source, normalized, map.ToImmutable(), obligations.ToImmutable(), candidate.InvariantSpans, CanonicalJson.RawDigest(identityPayload));

        void AddWireEvidence(string entityId, string symbol, string needle, string kind)
        {
            var line = LineOf(sourceText, needle);
            map.Add(new DafnySourceMapEntry(entityId, symbol, line));
            obligations.Add(new DafnyProofObligation(entityId.Replace('/', '-'), entityId, kind, line));
        }
    }

    private static int LineOf(string source, string needle)
    {
        var index = source.IndexOf(needle, StringComparison.Ordinal);
        if (index < 0) throw new InvalidOperationException($"PortableWireMarkerMissing:{needle}");
        return source.AsSpan(0, index).Count('\n') + 1;
    }

    private const string PortableWireSource = """
module PortableWrapper {
  import opened Candidate

  datatype WireValue = WI64(i64Value: int) | WBool(boolValue: bool) | WRecord(recordTypeId: string, recordFields: seq<WireField>) | WSeq(sequenceItems: seq<WireValue>)
  datatype WireField = WireField(fieldId: string, fieldValue: WireValue)
  datatype WireDetailValue = DText(text: string) | DNat(value: nat)
  datatype WireDetail = WireDetail(key: string, detailValue: WireDetailValue)
  datatype WireOutcome = Success(value: WireValue) | Refusal(code: string, locus: string, details: seq<WireDetail>)

  predicate IsI64(value: WireValue) {
    value.WI64? && -9223372036854775808 <= value.i64Value <= 9223372036854775807
  }

  function ToI64(value: WireValue): I64
    requires IsI64(value)
  {
    value.i64Value as I64
  }

  predicate IsI64Sequence(value: WireValue, capacity: nat) {
    value.WSeq? && |value.sequenceItems| <= capacity &&
      forall index: int :: 0 <= index < |value.sequenceItems| ==> IsI64(value.sequenceItems[index])
  }

  function ToI64Sequence(items: seq<WireValue>): seq<I64>
    requires forall index: int :: 0 <= index < |items| ==> IsI64(items[index])
    ensures |ToI64Sequence(items)| == |items|
    decreases |items|
  {
    if |items| == 0 then [] else [ToI64(items[0])] + ToI64Sequence(items[1..])
  }

  function FromI64Sequence(items: seq<I64>): WireValue {
    WSeq(seq(|items|, index requires 0 <= index < |items| => WI64(items[index] as int)))
  }

  function SequenceMismatchLocus(value: WireValue, capacity: nat, locus: string): string
    requires !IsI64Sequence(value, capacity)
  {
    if !value.WSeq? || |value.sequenceItems| > capacity then locus
    else if |value.sequenceItems| > 0 && !IsI64(value.sequenceItems[0]) then locus + "/items/0"
    else if |value.sequenceItems| > 1 && !IsI64(value.sequenceItems[1]) then locus + "/items/1"
    else if |value.sequenceItems| > 2 && !IsI64(value.sequenceItems[2]) then locus + "/items/2"
    else if |value.sequenceItems| > 3 && !IsI64(value.sequenceItems[3]) then locus + "/items/3"
    else if |value.sequenceItems| > 4 && !IsI64(value.sequenceItems[4]) then locus + "/items/4"
    else if |value.sequenceItems| > 5 && !IsI64(value.sequenceItems[5]) then locus + "/items/5"
    else if |value.sequenceItems| > 6 && !IsI64(value.sequenceItems[6]) then locus + "/items/6"
    else if |value.sequenceItems| > 7 && !IsI64(value.sequenceItems[7]) then locus + "/items/7"
    else locus
  }

  predicate IsSummary(value: WireValue) {
    value.WRecord? && value.recordTypeId == "Summary" && |value.recordFields| == 3 &&
      value.recordFields[0].fieldId == "echo" && IsI64Sequence(value.recordFields[0].fieldValue, 8) &&
      value.recordFields[1].fieldId == "negativeCount" && IsI64(value.recordFields[1].fieldValue) &&
      value.recordFields[2].fieldId == "sum" && IsI64(value.recordFields[2].fieldValue)
  }

  function SummaryMismatchLocus(value: WireValue, locus: string): string
    requires !IsSummary(value)
  {
    if !value.WRecord? || value.recordTypeId != "Summary" || |value.recordFields| != 3 then locus
    else if value.recordFields[0].fieldId != "echo" then locus
    else if !IsI64Sequence(value.recordFields[0].fieldValue, 8) then SequenceMismatchLocus(value.recordFields[0].fieldValue, 8, locus + "/fields/0/value")
    else if value.recordFields[1].fieldId != "negativeCount" then locus
    else if !IsI64(value.recordFields[1].fieldValue) then locus + "/fields/1/value"
    else if value.recordFields[2].fieldId != "sum" then locus
    else if !IsI64(value.recordFields[2].fieldValue) then locus + "/fields/2/value"
    else locus
  }

  function ToSummary(value: WireValue): R000
    requires IsSummary(value)
  {
    C000(
      ToI64Sequence(value.recordFields[0].fieldValue.sequenceItems),
      ToI64(value.recordFields[1].fieldValue),
      ToI64(value.recordFields[2].fieldValue))
  }

  function FromSummary(value: R000): WireValue {
    WRecord("Summary", [
      WireField("echo", FromI64Sequence(value.R000F000)),
      WireField("negativeCount", WI64(value.R000F001 as int)),
      WireField("sum", WI64(value.R000F002 as int))])
  }

  function ArityRefusal(expected: nat, actual: nat): WireOutcome {
    Refusal("ArityMismatch", "$/arguments", [WireDetail("expected", DNat(expected)), WireDetail("actual", DNat(actual))])
  }

  function TypeRefusal(locus: string): WireOutcome {
    Refusal("RuntimeTypeMismatch", locus, [])
  }

  method Invoke(functionId: string, arguments: seq<WireValue>) returns (outcome: WireOutcome)
    requires true
  {
    if functionId != "adjust" && functionId != "echoSummary" && functionId != "headOrZero" && functionId != "increment" && functionId != "summarize" {
      outcome := Refusal("UnknownFunction", "$/functionId", [WireDetail("functionId", DText(functionId))]);
    } else if functionId == "adjust" {
      if |arguments| != 2 {
        outcome := ArityRefusal(2, |arguments|);
      } else if !arguments[0].WBool? {
        outcome := TypeRefusal("$/arguments/0");
      } else if !IsI64(arguments[1]) {
        outcome := TypeRefusal("$/arguments/1");
      } else {
        var increase := arguments[0].boolValue;
        var x := ToI64(arguments[1]);
        if !Q000(increase, x) {
          outcome := Refusal("OwnerPreconditionFailed", "function/adjust/requires", []);
        } else {
          var result := F000(increase, x);
          assert result == M000(increase, x);
          outcome := Success(WI64(result as int));
        }
      }
    } else if functionId == "echoSummary" {
      if |arguments| != 1 {
        outcome := ArityRefusal(1, |arguments|);
      } else if !IsSummary(arguments[0]) {
        outcome := TypeRefusal(SummaryMismatchLocus(arguments[0], "$/arguments/0"));
      } else {
        var summary := ToSummary(arguments[0]);
        assert Q001(summary);
        var result := F001(summary);
        assert result == M001(summary);
        outcome := Success(FromSummary(result));
      }
    } else if functionId == "headOrZero" {
      if |arguments| != 1 {
        outcome := ArityRefusal(1, |arguments|);
      } else if !IsI64Sequence(arguments[0], 1) {
        outcome := TypeRefusal(SequenceMismatchLocus(arguments[0], 1, "$/arguments/0"));
      } else {
        var items: S000 := ToI64Sequence(arguments[0].sequenceItems);
        assert Q002(items);
        var result := F002(items);
        assert result == M002(items);
        outcome := Success(WI64(result as int));
      }
    } else if functionId == "increment" {
      if |arguments| != 1 {
        outcome := ArityRefusal(1, |arguments|);
      } else if !IsI64(arguments[0]) {
        outcome := TypeRefusal("$/arguments/0");
      } else {
        var x := ToI64(arguments[0]);
        if !Q003(x) {
          outcome := Refusal("OwnerPreconditionFailed", "function/increment/requires", []);
        } else {
          var result := F003(x);
          assert result == M003(x);
          outcome := Success(WI64(result as int));
        }
      }
    } else {
      if |arguments| != 1 {
        outcome := ArityRefusal(1, |arguments|);
      } else if !IsI64Sequence(arguments[0], 8) {
        outcome := TypeRefusal(SequenceMismatchLocus(arguments[0], 8, "$/arguments/0"));
      } else {
        var items: S001 := ToI64Sequence(arguments[0].sequenceItems);
        if !Q004(items) {
          outcome := Refusal("OwnerPreconditionFailed", "function/summarize/requires", []);
        } else {
          var result := F004(items);
          assert result == P004(items, |items|);
          outcome := Success(FromSummary(result));
        }
      }
    }
  }
}
""";
}
