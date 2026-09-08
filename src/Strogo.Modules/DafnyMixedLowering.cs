using System.Collections.Immutable;
using System.Text;
using Kernel.Core;

namespace Strogo.Modules;

public static partial class ModulesDafnyLowerer
{
    public const string MixedOwnerToolchainIdentity = "strogo.owner-dafny-lowering.v0.5";
    public static string MixedOwnerToolchainDigest { get; } = CanonicalJson.RawDigest(Encoding.UTF8.GetBytes(MixedOwnerToolchainIdentity));

    private static DafnyFoldLoweringResult LowerMixedOwnerBundle(ModuleIr module, OwnerBundleV04 ownerBundle, OwnerContractBindingV04 binding)
    {
        var typeSymbols = new TypeLoweringSymbols(module, null);
        var functions = module.Functions.OrderBy(function => function.Id, StringComparer.Ordinal).ToArray();
        var functionSymbols = functions.Select((function, index) => (function.Id, Name: $"F{index:D3}"))
            .ToImmutableDictionary(pair => pair.Id, pair => pair.Name, StringComparer.Ordinal);
        var writer = new SourceWriter();
        var map = ImmutableArray.CreateBuilder<DafnySourceMapEntry>();
        var obligations = ImmutableArray.CreateBuilder<DafnyProofObligation>();
        var invariantLines = ImmutableArray.CreateBuilder<int>();

        writer.Add("module Candidate {");
        writer.Add(I64Declaration);
        typeSymbols.EmitDeclarations(writer, map);
        EmitMixedPrelude(writer);

        var orderedEntries = binding.Entries.OrderBy(entry => entry.Function.Id, StringComparer.Ordinal).ToArray();
        for (var entryIndex = 0; entryIndex < orderedEntries.Length; entryIndex++)
        {
            var entry = orderedEntries[entryIndex];
            if (entry.CandidateFold is null)
                EmitOwnerScalar(entry, entryIndex, typeSymbols, writer, map);
            else
                EmitOwnerFold(entry, entryIndex, typeSymbols, writer, map, obligations, invariantLines);
        }

        for (var entryIndex = 0; entryIndex < orderedEntries.Length; entryIndex++)
        {
            var entry = orderedEntries[entryIndex];
            if (entry.CandidateFold is null)
                EmitCandidateScalar(entry, entryIndex, typeSymbols, functionSymbols, writer, map, obligations, invariantLines);
            else
                EmitCandidateFold(entry.Function, entry, entryIndex, typeSymbols, functionSymbols, writer, map, obligations, invariantLines);
        }

        writer.Add("}");
        var source = Encoding.UTF8.GetBytes(writer.Text);
        var spans = ComputeLineSpans(source, invariantLines.ToImmutable());
        var normalized = NormalizeInvariantSpans(source, spans);
        var identityPayload = Encoding.UTF8.GetBytes($"strogo.mixed-owner-proof.v0.1\n{MixedOwnerToolchainDigest}\n{module.SourceDigest}\n{ownerBundle.BundleDigest}");
        return new DafnyFoldLoweringResult(source, normalized, map.ToImmutable(), obligations.ToImmutable(), spans, CanonicalJson.RawDigest(identityPayload));
    }

    private static void EmitMixedPrelude(SourceWriter writer)
    {
        writer.Add("  function StrictAnd(left: bool, right: bool): bool { left && right }");
        writer.Add("  function StrictOr(left: bool, right: bool): bool { left || right }");
        writer.Add("  function SumI64(s: seq<I64>): int");
        writer.Add("    decreases |s|");
        writer.Add("  {");
        writer.Add("    if |s| == 0 then 0 else SumI64(s[..|s|-1]) + (s[|s|-1] as int)");
        writer.Add("  }");
        writer.Add("  function PrefixSumI64(s: seq<I64>, n: int): int");
        writer.Add("    requires 0 <= n <= |s|");
        writer.Add("  {");
        writer.Add("    SumI64(s[..n])");
        writer.Add("  }");
        writer.Add("  lemma PrefixSumWhole(s: seq<I64>)");
        writer.Add("    ensures PrefixSumI64(s, |s|) == SumI64(s)");
        writer.Add("  {");
        writer.Add("    assert s[..|s|] == s;");
        writer.Add("  }");
        writer.Add("  lemma PrefixSumStep(s: seq<I64>, n: int)");
        writer.Add("    requires 0 <= n < |s|");
        writer.Add("    ensures PrefixSumI64(s, n + 1) == PrefixSumI64(s, n) + (s[n] as int)");
        writer.Add("  {");
        writer.Add("    assert (s[..n+1])[..n] == s[..n];");
        writer.Add("  }");
        writer.Add("  lemma SumNonnegative(s: seq<I64>)");
        writer.Add("    requires forall j: int :: 0 <= j < |s| ==> 0 <= s[j]");
        writer.Add("    ensures 0 <= SumI64(s)");
        writer.Add("    decreases |s|");
        writer.Add("  {");
        writer.Add("    if |s| > 0 { SumNonnegative(s[..|s|-1]); }");
        writer.Add("  }");
        writer.Add("  lemma PrefixSumBounds(s: seq<I64>, n: int)");
        writer.Add("    requires 0 <= n <= |s|");
        writer.Add("    requires forall j: int :: 0 <= j < |s| ==> 0 <= s[j]");
        writer.Add("    ensures 0 <= PrefixSumI64(s, n) <= SumI64(s)");
        writer.Add("    decreases |s|");
        writer.Add("  {");
        writer.Add("    if |s| == 0 {");
        writer.Add("    } else if n == |s| {");
        writer.Add("      PrefixSumWhole(s);");
        writer.Add("      SumNonnegative(s);");
        writer.Add("    } else {");
        writer.Add("      PrefixSumBounds(s[..|s|-1], n);");
        writer.Add("      assert s[..n] == (s[..|s|-1])[..n];");
        writer.Add("    }");
        writer.Add("  }");
    }

    private static void EmitOwnerScalar(
        BoundOwnerEntryV04 entry,
        int entryIndex,
        TypeLoweringSymbols typeSymbols,
        SourceWriter writer,
        ImmutableArray<DafnySourceMapEntry>.Builder map)
    {
        var parameters = entry.Model.Parameters.Select((parameter, index) => $"p{index:D3}: {typeSymbols.DafnyType(parameter.Type)}").ToArray();
        var parameterSymbols = entry.Model.Parameters.Select((parameter, index) => (parameter.Id, Symbol: $"p{index:D3}"))
            .ToImmutableDictionary(pair => pair.Id, pair => pair.Symbol, StringComparer.Ordinal);
        var arguments = entry.Model.Parameters.Select((_, index) => $"p{index:D3}").ToArray();
        var requiresName = $"Q{entryIndex:D3}";
        var modelName = $"M{entryIndex:D3}";
        var line = writer.Add($"  predicate {requiresName}({string.Join(", ", parameters)})");
        map.Add(new DafnySourceMapEntry($"owner/contract/{entry.Contract.Id}/requires", requiresName, line));
        writer.Add("  {");
        writer.Add($"    {EmitProofExpression(entry.Contract.Requires, parameterSymbols, typeSymbols, null)}");
        writer.Add("  }");
        line = writer.Add($"  function {modelName}({string.Join(", ", parameters)}): {typeSymbols.DafnyType(entry.Model.ReturnType)}");
        map.Add(new DafnySourceMapEntry($"owner/model/{entry.Model.Id}", modelName, line));
        writer.Add($"    requires {requiresName}({string.Join(", ", arguments)})");
        writer.Add("  {");
        writer.Add($"    {EmitOwnerModelExpression(entry.Model.Body, parameterSymbols, typeSymbols)}");
        writer.Add("  }");
    }

    private static void EmitCandidateScalar(
        BoundOwnerEntryV04 entry,
        int entryIndex,
        TypeLoweringSymbols typeSymbols,
        ImmutableDictionary<string, string> functionSymbols,
        SourceWriter writer,
        ImmutableArray<DafnySourceMapEntry>.Builder map,
        ImmutableArray<DafnyProofObligation>.Builder obligations,
        ImmutableArray<int>.Builder invariantLines)
    {
        var function = entry.Function;
        var functionName = functionSymbols[function.Id];
        var parameters = function.Parameters.Select((parameter, index) => $"p{index:D3}: {typeSymbols.DafnyType(parameter.Type)}").ToArray();
        var arguments = function.Parameters.Select((_, index) => $"p{index:D3}").ToArray();
        var line = writer.Add($"  method {functionName}({string.Join(", ", parameters)}) returns (result: {typeSymbols.DafnyType(function.ReturnType)})");
        map.Add(new DafnySourceMapEntry($"function/{function.Id}", functionName, line));
        writer.Add($"    requires Q{entryIndex:D3}({string.Join(", ", arguments)})");
        var ensuresLine = writer.Add($"    ensures result == M{entryIndex:D3}({string.Join(", ", arguments)})");
        obligations.Add(new DafnyProofObligation($"candidate-postcondition-{function.Id}", $"function/{function.Id}/postcondition", "postcondition", ensuresLine));
        writer.Add("  {");
        var variableCounter = 0;
        var result = EmitFoldRegion(
            new RegionIr(function.Parameters, function.Instructions, function.ResultIndex),
            arguments,
            $"function/{function.Id}/body",
            "    ",
            ref variableCounter,
            writer,
            map,
            obligations,
            invariantLines,
            functionSymbols,
            typeSymbols,
            entry,
            entryIndex);
        writer.Add($"    result := {result};");
        writer.Add("  }");
    }
}
