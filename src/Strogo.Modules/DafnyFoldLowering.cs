using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Kernel.Core;

namespace Strogo.Modules;

public sealed record DafnyProofObligation(string Id, string EntityId, string Kind, int Line);
public sealed record DafnyInvariantSpan(int StartByte, int Length, int Line);

public sealed class DafnyFoldLoweringResult
{
    private readonly byte[] sourceBytes;
    private readonly byte[] normalizedSourceBytes;

    internal DafnyFoldLoweringResult(
        byte[] sourceBytes,
        byte[] normalizedSourceBytes,
        ImmutableArray<DafnySourceMapEntry> sourceMap,
        ImmutableArray<DafnyProofObligation> obligations,
        ImmutableArray<DafnyInvariantSpan> invariantSpans,
        string proofIdentity)
    {
        this.sourceBytes = sourceBytes.ToArray();
        this.normalizedSourceBytes = normalizedSourceBytes.ToArray();
        SourceMap = sourceMap;
        Obligations = obligations;
        InvariantSpans = invariantSpans;
        SourceDigest = CanonicalJson.RawDigest(this.sourceBytes);
        NormalizedSourceDigest = CanonicalJson.RawDigest(this.normalizedSourceBytes);
        ProofIdentity = proofIdentity;
    }

    public byte[] SourceBytes => sourceBytes.ToArray();
    public string Source => Encoding.UTF8.GetString(sourceBytes);
    public byte[] NormalizedSourceBytes => normalizedSourceBytes.ToArray();
    public string SourceDigest { get; }
    public string NormalizedSourceDigest { get; }
    public string ProofIdentity { get; }
    public ImmutableArray<DafnySourceMapEntry> SourceMap { get; }
    public ImmutableArray<DafnyProofObligation> Obligations { get; }
    public ImmutableArray<DafnyInvariantSpan> InvariantSpans { get; }
}

public static partial class ModulesDafnyLowerer
{
    public const string FoldToolchainIdentity = "strogo.fold-dafny-lowering.v0.4";
    public static string FoldToolchainDigest { get; } = CanonicalJson.RawDigest(Encoding.UTF8.GetBytes(FoldToolchainIdentity));

    public static DafnyFoldLoweringResult Lower(ModuleIr module, OwnerBundleV04 ownerBundle)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(ownerBundle);
        var binding = OwnerContractBinderV04.Bind(module, ownerBundle);
        if (binding.Entries.Any(entry => entry.CandidateFold is null))
            throw ModulesExceptionFactory.Error("lowering", "FoldOwnerRequired");

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

        var entryIndex = 0;
        foreach (var entry in binding.Entries.OrderBy(entry => entry.Function.Id, StringComparer.Ordinal))
        {
            EmitOwnerFold(entry, entryIndex, typeSymbols, writer, map, obligations, invariantLines);
            entryIndex++;
        }

        entryIndex = 0;
        foreach (var function in functions)
        {
            var entry = binding.Entries.Single(item => item.Function.Id == function.Id);
            EmitCandidateFold(function, entry, entryIndex, typeSymbols, functionSymbols, writer, map, obligations, invariantLines);
            entryIndex++;
        }
        writer.Add("}");

        var source = Encoding.UTF8.GetBytes(writer.Text);
        var spans = ComputeLineSpans(source, invariantLines.ToImmutable());
        var normalized = NormalizeInvariantSpans(source, spans);
        var identityPayload = Encoding.UTF8.GetBytes($"strogo.fold-proof.v0.1\n{FoldToolchainDigest}\n{module.SourceDigest}\n{ownerBundle.BundleDigest}");
        return new DafnyFoldLoweringResult(source, normalized, map.ToImmutable(), obligations.ToImmutable(), spans, CanonicalJson.RawDigest(identityPayload));
    }

    private static void EmitOwnerFold(
        BoundOwnerEntryV04 entry,
        int entryIndex,
        TypeLoweringSymbols typeSymbols,
        SourceWriter writer,
        ImmutableArray<DafnySourceMapEntry>.Builder map,
        ImmutableArray<DafnyProofObligation>.Builder obligations,
        ImmutableArray<int>.Builder invariantLines)
    {
        var model = entry.Model;
        var fold = model.Body;
        if (fold.Op != "fold" || fold.FoldStep is null)
            throw ModulesExceptionFactory.Error("lowering", "OwnerFoldPairingMismatch", entry.Function.Id);
        var prefixName = $"P{entryIndex:D3}";
        var lemmaName = $"L{entryIndex:D3}";
        var requiresName = $"Q{entryIndex:D3}";
        var parameters = model.Parameters.Select((parameter, index) => $"p{index:D3}: {typeSymbols.DafnyType(parameter.Type)}").ToArray();
        var parameterSymbols = model.Parameters.Select((parameter, index) => (parameter.Id, Symbol: $"p{index:D3}"))
            .ToImmutableDictionary(pair => pair.Id, pair => pair.Symbol, StringComparer.Ordinal);
        var parameterArguments = model.Parameters.Select((_, index) => $"p{index:D3}").ToArray();
        var requires = EmitProofExpression(entry.Contract.Requires, parameterSymbols, typeSymbols, null);
        var sequence = EmitOwnerModelExpression(fold.Args[0], parameterSymbols, typeSymbols);
        var initial = EmitOwnerModelExpression(fold.Args[1], parameterSymbols, typeSymbols);
        var environment = fold.Args.Skip(2).Select(argument => EmitOwnerModelExpression(argument, parameterSymbols, typeSymbols)).ToArray();
        var prefixCall = $"{prefixName}({string.Join(", ", parameterArguments)}, n)";
        var foldLocus = $"function/{entry.Function.Id}/fold/{entry.CandidateFold!.OriginNodeId}";
        var invariant = EmitProofExpression(entry.CandidateFold!.Fold!.Invariant, parameterSymbols, typeSymbols,
            new FoldProofSymbols("n as I64", sequence, initial, prefixCall, environment));
        var useSumProof = UsesCanonicalI64Sum(fold, model.ReturnType);

        var line = writer.Add($"  predicate {requiresName}({string.Join(", ", parameters)})");
        map.Add(new DafnySourceMapEntry($"owner/contract/{entry.Contract.Id}/requires", requiresName, line));
        writer.Add("  {");
        writer.Add($"    {requires}");
        writer.Add("  }");

        line = writer.Add($"  function {prefixName}({JoinParameters(parameters, "n: int")}): {(useSumProof ? "int" : typeSymbols.DafnyType(model.ReturnType))}");
        map.Add(new DafnySourceMapEntry($"owner/model/{model.Id}/prefix", prefixName, line));
        writer.Add($"    requires {requiresName}({string.Join(", ", parameterArguments)}) && 0 <= n <= |{sequence}|");
        if (!useSumProof)
        {
            var prefixInvariantLine = writer.Add($"    ensures {invariant}");
            invariantLines.Add(prefixInvariantLine);
            obligations.Add(new DafnyProofObligation("owner-prefix-invariant", $"{foldLocus}/invariant-definedness", "postcondition", prefixInvariantLine));
        }
        writer.Add("    decreases n");
        writer.Add("  {");
        var previousPrefix = $"{prefixName}({string.Join(", ", parameterArguments)}, n - 1)";
        var stepSymbols = fold.FoldStep.Parameters.Select((parameter, index) => (parameter.Id, Symbol: index switch
        {
            0 => useSumProof ? "n - 1" : "(n - 1) as I64",
            1 => useSumProof ? $"({sequence}[n - 1] as int)" : $"{sequence}[n - 1]",
            2 => previousPrefix,
            _ => useSumProof ? $"({environment[index - 3]} as int)" : environment[index - 3]
        })).ToImmutableDictionary(pair => pair.Id, pair => pair.Symbol, StringComparer.Ordinal);
        var step = useSumProof
            ? EmitOwnerPrefixStep(fold.FoldStep.Body, stepSymbols)
            : EmitOwnerModelExpression(fold.FoldStep.Body, stepSymbols, typeSymbols);
        writer.Add($"    if n == 0 then {(useSumProof ? $"({initial} as int)" : initial)} else {step}");
        writer.Add("  }");

        string? equalityName = null;
        string? rangeName = null;
        if (useSumProof)
        {
            equalityName = $"E{entryIndex:D3}";
            line = writer.Add($"  lemma {equalityName}({JoinParameters(parameters, "n: int")})");
            map.Add(new DafnySourceMapEntry($"owner/model/{model.Id}/prefix-equality", equalityName, line));
            writer.Add($"    requires {requiresName}({string.Join(", ", parameterArguments)}) && 0 <= n <= |{sequence}|");
            writer.Add($"    ensures {prefixName}({string.Join(", ", parameterArguments)}, n) == PrefixSumI64({sequence}, n)");
            writer.Add("    decreases n");
            writer.Add("  {");
            writer.Add("    if n > 0 {");
            writer.Add($"      {equalityName}({string.Join(", ", parameterArguments)}, n - 1);");
            writer.Add($"      PrefixSumStep({sequence}, n - 1);");
            writer.Add("    }");
            writer.Add("  }");

            rangeName = $"G{entryIndex:D3}";
            line = writer.Add($"  lemma {rangeName}({JoinParameters(parameters, "n: int")})");
            map.Add(new DafnySourceMapEntry($"owner/model/{model.Id}/prefix-range", rangeName, line));
            writer.Add($"    requires {requiresName}({string.Join(", ", parameterArguments)}) && 0 <= n <= |{sequence}|");
            writer.Add($"    ensures -9223372036854775808 <= {prefixName}({string.Join(", ", parameterArguments)}, n) <= 9223372036854775807");
            writer.Add("  {");
            writer.Add($"    {equalityName}({string.Join(", ", parameterArguments)}, n);");
            writer.Add($"    PrefixSumBounds({sequence}, n);");
            writer.Add("  }");
        }

        line = writer.Add($"  lemma {lemmaName}({JoinParameters(parameters, "n: int")})");
        map.Add(new DafnySourceMapEntry($"owner/model/{model.Id}/prefix-invariant", lemmaName, line));
        writer.Add($"    requires {requiresName}({string.Join(", ", parameterArguments)}) && 0 <= n <= |{sequence}|");
        var ensuresLine = writer.Add($"    ensures {invariant}");
        invariantLines.Add(ensuresLine);
        writer.Add("    decreases n");
        writer.Add("  {");
        if (useSumProof)
        {
            writer.Add($"    {equalityName}({string.Join(", ", parameterArguments)}, n);");
            writer.Add($"    PrefixSumBounds({sequence}, n);");
            writer.Add($"    {rangeName}({string.Join(", ", parameterArguments)}, n);");
        }
        writer.Add("    if n == 0 {");
        var initialInvariant = EmitProofExpression(entry.CandidateFold.Fold.Invariant, parameterSymbols, typeSymbols,
            new FoldProofSymbols("0 as I64", sequence, initial, $"{prefixName}({string.Join(", ", parameterArguments)}, 0)", environment));
        var initialLine = writer.Add($"      assert {initialInvariant};");
        invariantLines.Add(initialLine);
        obligations.Add(new DafnyProofObligation("owner-prefix-initial", $"{foldLocus}/initial", "assertion", initialLine));
        writer.Add("    } else {");
        writer.Add($"      {lemmaName}({string.Join(", ", parameterArguments)}, n - 1);");
        var preservationLine = writer.Add($"      assert {invariant};");
        invariantLines.Add(preservationLine);
        obligations.Add(new DafnyProofObligation("owner-prefix-preservation", $"{foldLocus}/owner-preservation", "assertion", preservationLine));
        writer.Add("    }");
        writer.Add("  }");
    }

    private static void EmitCandidateFold(
        FunctionIr function,
        BoundOwnerEntryV04 entry,
        int entryIndex,
        TypeLoweringSymbols typeSymbols,
        ImmutableDictionary<string, string> functionSymbols,
        SourceWriter writer,
        ImmutableArray<DafnySourceMapEntry>.Builder map,
        ImmutableArray<DafnyProofObligation>.Builder obligations,
        ImmutableArray<int>.Builder invariantLines)
    {
        var functionName = functionSymbols[function.Id];
        var prefixName = $"P{entryIndex:D3}";
        var lemmaName = $"L{entryIndex:D3}";
        var requiresName = $"Q{entryIndex:D3}";
        var parameters = function.Parameters.Select((parameter, index) => $"p{index:D3}: {typeSymbols.DafnyType(parameter.Type)}").ToArray();
        var parameterExpressions = function.Parameters.Select((_, index) => $"p{index:D3}").ToArray();
        var line = writer.Add($"  method {functionName}({string.Join(", ", parameters)}) returns (result: {typeSymbols.DafnyType(function.ReturnType)})");
        map.Add(new DafnySourceMapEntry($"function/{function.Id}", functionName, line));
        writer.Add($"    requires {requiresName}({string.Join(", ", parameterExpressions)})");
        var ownerFold = entry.Model.Body;
        var useSumProof = UsesCanonicalI64Sum(ownerFold, entry.Model.ReturnType);
        var ownerParameterSymbols = entry.Model.Parameters.Select((parameter, index) => (parameter.Id, Symbol: $"p{index:D3}"))
            .ToImmutableDictionary(pair => pair.Id, pair => pair.Symbol, StringComparer.Ordinal);
        var ownerSequence = EmitOwnerModelExpression(ownerFold.Args[0], ownerParameterSymbols, typeSymbols);
        var postLine = writer.Add($"    ensures {(useSumProof ? "(result as int)" : "result")} == {prefixName}({string.Join(", ", parameterExpressions)}, |{ownerSequence}|)");
        obligations.Add(new DafnyProofObligation("candidate-postcondition", $"function/{function.Id}/fold/{entry.CandidateFold!.OriginNodeId}/final", "postcondition", postLine));
        writer.Add("  {");
        var variableCounter = 0;
        var resultExpression = EmitFoldRegion(
            new RegionIr(function.Parameters, function.Instructions, function.ResultIndex),
            parameterExpressions,
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
        writer.Add($"    result := {resultExpression};");
        writer.Add("  }");
    }

    private static string EmitFoldRegion(
        RegionIr region,
        IReadOnlyList<string> parameters,
        string regionLocus,
        string indent,
        ref int variableCounter,
        SourceWriter writer,
        ImmutableArray<DafnySourceMapEntry>.Builder map,
        ImmutableArray<DafnyProofObligation>.Builder obligations,
        ImmutableArray<int>.Builder invariantLines,
        ImmutableDictionary<string, string> functionSymbols,
        TypeLoweringSymbols typeSymbols,
        BoundOwnerEntryV04 entry,
        int entryIndex)
    {
        var values = new string[region.Instructions.Length];
        foreach (var instruction in region.Instructions)
        {
            var locus = $"{regionLocus}/node/{instruction.OriginNodeId}";
            var operands = instruction.OperandIndices.Select(index => Resolve(index, parameters, values, locus)).ToArray();
            var operandTypes = instruction.OperandIndices.Select(index => ResolveType(index, region, locus)).ToArray();
            var variable = $"v{variableCounter++:D3}";
            var line = writer.Line + 1;
            if (instruction.Op == "fold")
            {
                EmitFoldInstruction(instruction, operands, variable, locus, indent, ref variableCounter, writer, map, obligations, invariantLines, functionSymbols, typeSymbols, entry, entryIndex);
            }
            else if (instruction.Op == "if")
            {
                writer.Add($"{indent}var {variable}: {typeSymbols.DafnyType(instruction.Type)};");
                map.Add(new DafnySourceMapEntry(locus, variable, line));
                writer.Add($"{indent}if {operands[0]} {{");
                var thenResult = EmitFoldRegion(instruction.ThenRegion!, operands[1..], $"{locus}/then", indent + "  ", ref variableCounter, writer, map, obligations, invariantLines, functionSymbols, typeSymbols, entry, entryIndex);
                writer.Add($"{indent}  {variable} := {thenResult};");
                writer.Add($"{indent}}} else {{");
                var elseResult = EmitFoldRegion(instruction.ElseRegion!, operands[1..], $"{locus}/else", indent + "  ", ref variableCounter, writer, map, obligations, invariantLines, functionSymbols, typeSymbols, entry, entryIndex);
                writer.Add($"{indent}  {variable} := {elseResult};");
                writer.Add($"{indent}}}");
            }
            else
            {
                if (instruction.Op is "i64.add" or "i64.sub")
                {
                    var operation = instruction.Op == "i64.add" ? "+" : "-";
                    var rangeLine = writer.Add($"{indent}assert -9223372036854775808 <= ({operands[0]} as int) {operation} ({operands[1]} as int) <= 9223372036854775807;");
                    obligations.Add(new DafnyProofObligation($"range-{entry.Function.Id}-{instruction.OriginNodeId}", FoldRangeLocus(locus, instruction.OriginNodeId), "assertion", rangeLine));
                }
                if (instruction.Op == "seq.get")
                {
                    var rangeLine = writer.Add($"{indent}assert 0 <= ({operands[1]} as int) < |{operands[0]}|;");
                    obligations.Add(new DafnyProofObligation($"range-{entry.Function.Id}-{instruction.OriginNodeId}", FoldRangeLocus(locus, instruction.OriginNodeId), "assertion", rangeLine));
                }
                if (instruction.Op == "seq.append")
                {
                    var rangeLine = writer.Add($"{indent}assert |{operands[0]}| < {instruction.Type.Capacity!.Value};");
                    obligations.Add(new DafnyProofObligation($"range-{entry.Function.Id}-{instruction.OriginNodeId}", FoldRangeLocus(locus, instruction.OriginNodeId), "assertion", rangeLine));
                }
                writer.Add($"{indent}var {variable}: {typeSymbols.DafnyType(instruction.Type)} := {Expression(instruction, operands, operandTypes, locus, functionSymbols, typeSymbols)};");
                map.Add(new DafnySourceMapEntry(locus, variable, line));
            }
            values[instruction.DestinationIndex] = variable;
        }
        return Resolve(region.ResultIndex, parameters, values, regionLocus);
    }

    private static void EmitFoldInstruction(
        IrInstruction instruction,
        string[] operands,
        string accumulator,
        string locus,
        string indent,
        ref int variableCounter,
        SourceWriter writer,
        ImmutableArray<DafnySourceMapEntry>.Builder map,
        ImmutableArray<DafnyProofObligation>.Builder obligations,
        ImmutableArray<int>.Builder invariantLines,
        ImmutableDictionary<string, string> functionSymbols,
        TypeLoweringSymbols typeSymbols,
        BoundOwnerEntryV04 entry,
        int entryIndex)
    {
        if (instruction.Fold is null) throw ModulesExceptionFactory.Error("lowering", "InternalInvariantViolation", locus);
        var sequence = operands[0];
        var initial = operands[1];
        var environment = operands.Skip(2).ToArray();
        var ownerSymbols = entry.Model.Parameters.Select((parameter, index) => (parameter.Id, Symbol: $"p{index:D3}"))
            .ToImmutableDictionary(pair => pair.Id, pair => pair.Symbol, StringComparer.Ordinal);
        var ownerSequence = EmitOwnerModelExpression(entry.Model.Body.Args[0], ownerSymbols, typeSymbols);
        var ownerInitial = EmitOwnerModelExpression(entry.Model.Body.Args[1], ownerSymbols, typeSymbols);
        var ownerEnvironment = entry.Model.Body.Args.Skip(2).Select(argument => EmitOwnerModelExpression(argument, ownerSymbols, typeSymbols)).ToArray();
        var foldLocus = $"function/{entry.Function.Id}/fold/{instruction.OriginNodeId}";
        var inputLine = writer.Add($"{indent}assert {sequence} == {ownerSequence};");
        obligations.Add(new DafnyProofObligation("input-equivalence-0", $"{foldLocus}/input-equivalence/0", "assertion", inputLine));
        inputLine = writer.Add($"{indent}assert {initial} == {ownerInitial};");
        obligations.Add(new DafnyProofObligation("input-equivalence-1", $"{foldLocus}/input-equivalence/1", "assertion", inputLine));
        for (var index = 0; index < environment.Length; index++)
        {
            inputLine = writer.Add($"{indent}assert {environment[index]} == {ownerEnvironment[index]};");
            obligations.Add(new DafnyProofObligation($"input-equivalence-{index + 2}", $"{foldLocus}/input-equivalence/{index + 2}", "assertion", inputLine));
        }

        var indexVariable = $"i{entryIndex:D3}";
        writer.Add($"{indent}var {accumulator}: {typeSymbols.DafnyType(instruction.Type)} := {initial};");
        map.Add(new DafnySourceMapEntry(locus, accumulator, writer.Line));
        writer.Add($"{indent}var {indexVariable}: int := 0;");
        var prefixCall = $"P{entryIndex:D3}({string.Join(", ", entry.Function.Parameters.Select((_, index) => $"p{index:D3}"))}, {indexVariable})";
        var useSumProof = UsesCanonicalI64Sum(entry.Model.Body, entry.Model.ReturnType);
        var agentInvariant = EmitProofExpression(instruction.Fold.Invariant, ownerSymbols, typeSymbols,
            new FoldProofSymbols($"{indexVariable} as I64", sequence, initial, accumulator, environment));
        writer.Add($"{indent}L{entryIndex:D3}({string.Join(", ", entry.Function.Parameters.Select((_, index) => $"p{index:D3}"))}, 0);");
        var loopLine = writer.Add($"{indent}while {indexVariable} < |{sequence}|");
        map.Add(new DafnySourceMapEntry($"{locus}/loop", indexVariable, loopLine));
        writer.Add($"{indent}  invariant 0 <= {indexVariable} <= |{sequence}|");
        writer.Add($"{indent}  invariant {sequence} == {ownerSequence} && {initial} == {ownerInitial}");
        if (environment.Length > 0) writer.Add($"{indent}  invariant {string.Join(" && ", environment.Select((value, index) => $"{value} == {ownerEnvironment[index]}"))}");
        writer.Add($"{indent}  invariant {(useSumProof ? $"({accumulator} as int)" : accumulator)} == {prefixCall}");
        var invariantLine = writer.Add($"{indent}  invariant {agentInvariant}");
        invariantLines.Add(invariantLine);
        obligations.Add(new DafnyProofObligation("candidate-loop-invariant", $"{foldLocus}/invariant-definedness", "loop-invariant", invariantLine));
        var terminationLine = writer.Add($"{indent}  decreases |{sequence}| - {indexVariable}");
        obligations.Add(new DafnyProofObligation("candidate-loop-termination", $"{foldLocus}/termination", "decreases", terminationLine));
        writer.Add($"{indent}{{");
        if (useSumProof)
            writer.Add($"{indent}  G{entryIndex:D3}({string.Join(", ", entry.Function.Parameters.Select((_, index) => $"p{index:D3}"))}, {indexVariable} + 1);");
        var stepParameters = new[] { $"{indexVariable} as I64", $"{sequence}[{indexVariable}]", accumulator }.Concat(environment).ToArray();
        var stepResult = EmitFoldRegion(instruction.Fold.Step, stepParameters, $"{locus}/step", indent + "  ", ref variableCounter, writer, map, obligations, invariantLines, functionSymbols, typeSymbols, entry, entryIndex);
        writer.Add($"{indent}  {accumulator} := {stepResult};");
        writer.Add($"{indent}  {indexVariable} := {indexVariable} + 1;");
        writer.Add($"{indent}  L{entryIndex:D3}({string.Join(", ", entry.Function.Parameters.Select((_, index) => $"p{index:D3}"))}, {indexVariable});");
        var preservationLine = writer.Add($"{indent}  assert {(useSumProof ? $"({accumulator} as int)" : accumulator)} == P{entryIndex:D3}({string.Join(", ", entry.Function.Parameters.Select((_, index) => $"p{index:D3}"))}, {indexVariable});");
        obligations.Add(new DafnyProofObligation("candidate-loop-preservation", $"{foldLocus}/candidate-preservation", "assertion", preservationLine));
        writer.Add($"{indent}}}");
    }

    private static bool UsesCanonicalI64Sum(OwnerExpression fold, TypeRef returnType)
    {
        if (returnType.Kind != "I64" || fold.FoldStep is null || fold.Args.Length != 2
            || fold.Args[0].Type.Element?.Kind != "I64"
            || fold.Args[1] is not { Op: "i64.const", I64Value: 0 })
            return false;

        var parameters = fold.FoldStep.Parameters;
        var body = fold.FoldStep.Body;
        return parameters.Length == 3
            && body is { Op: "i64.add", Args.Length: 2 }
            && body.Args[0] is { Op: "param", ReferenceId: var accumulatorId }
            && body.Args[1] is { Op: "param", ReferenceId: var elementId }
            && accumulatorId == parameters[2].Id
            && elementId == parameters[1].Id;
    }

    private static string FoldRangeLocus(string nodeLocus, string nodeId)
    {
        var step = nodeLocus.LastIndexOf("/step", StringComparison.Ordinal);
        return step >= 0 ? $"{nodeLocus[..step]}/range/{nodeId}" : $"{nodeLocus}/range";
    }

    private static string EmitOwnerModelExpression(OwnerExpression expression, ImmutableDictionary<string, string> symbols, TypeLoweringSymbols typeSymbols)
    {
        var temporaryCounter = 0;
        return Emit(expression);
        string Emit(OwnerExpression current) => current.Op switch
        {
            "param" when current.ReferenceId is { } id && symbols.TryGetValue(id, out var symbol) => symbol,
            "i64.const" => current.I64Value!.Value.ToString(CultureInfo.InvariantCulture),
            "bool.const" => current.BoolValue!.Value ? "true" : "false",
            "i64.add" => Checked(current, "+"),
            "i64.sub" => Checked(current, "-"),
            "i64.le" => $"({Emit(current.Args[0])} <= {Emit(current.Args[1])})",
            "eq" => $"({Emit(current.Args[0])} == {Emit(current.Args[1])})",
            "bool.not" => $"(!{Emit(current.Args[0])})",
            "bool.and" => $"StrictAnd({Emit(current.Args[0])}, {Emit(current.Args[1])})",
            "bool.or" => $"StrictOr({Emit(current.Args[0])}, {Emit(current.Args[1])})",
            "if" => $"(if {Emit(current.Args[0])} then {Emit(current.Args[1])} else {Emit(current.Args[2])})",
            "record.make" => typeSymbols.ConstructRecord(current.RecordType!, current.FieldIds.Select((field, index) => (field, value: Emit(current.Args[index]))).OrderBy(pair => pair.field, StringComparer.Ordinal).Select(pair => pair.value).ToArray()),
            "record.get" => $"{Emit(current.Args[0])}.{typeSymbols.RecordField(current.Args[0].Type.Name!, current.ReferenceId!)}",
            "seq.empty" => "[]",
            "seq.length" => $"(|{Emit(current.Args[0])}| as I64)",
            "seq.get" => $"{Emit(current.Args[0])}[{Emit(current.Args[1])} as int]",
            "seq.append" => $"{Emit(current.Args[0])} + [{Emit(current.Args[1])}]",
            _ => throw ModulesExceptionFactory.Error("lowering", "UnsupportedOwnerFoldExpression", details: new { current.Op })
        };
        string Checked(OwnerExpression current, string op)
        {
            var temporary = $"e{temporaryCounter++:D3}";
            return $"(var {temporary}: I64 := ({Emit(current.Args[0])} {op} {Emit(current.Args[1])}); {temporary})";
        }
    }

    private static string EmitOwnerPrefixStep(OwnerExpression expression, ImmutableDictionary<string, string> symbols)
    {
        return Emit(expression);
        string Emit(OwnerExpression current) => current.Op switch
        {
            "param" when current.ReferenceId is { } id && symbols.TryGetValue(id, out var symbol) => symbol,
            "i64.const" => current.I64Value!.Value.ToString(CultureInfo.InvariantCulture),
            "bool.const" => current.BoolValue!.Value ? "true" : "false",
            "i64.add" => $"({Emit(current.Args[0])} + {Emit(current.Args[1])})",
            "i64.sub" => $"({Emit(current.Args[0])} - {Emit(current.Args[1])})",
            "i64.le" => $"({Emit(current.Args[0])} <= {Emit(current.Args[1])})",
            "eq" => $"({Emit(current.Args[0])} == {Emit(current.Args[1])})",
            "bool.not" => $"(!{Emit(current.Args[0])})",
            "bool.and" => $"StrictAnd({Emit(current.Args[0])}, {Emit(current.Args[1])})",
            "bool.or" => $"StrictOr({Emit(current.Args[0])}, {Emit(current.Args[1])})",
            "if" => $"(if {Emit(current.Args[0])} then {Emit(current.Args[1])} else {Emit(current.Args[2])})",
            _ => throw ModulesExceptionFactory.Error("lowering", "UnsupportedOwnerPrefixStep", details: new { current.Op })
        };
    }

    private sealed record FoldProofSymbols(string PrefixLength, string Sequence, string InitialAccumulator, string Accumulator, IReadOnlyList<string> Environment);

    private static string EmitProofExpression(ProofExpression expression, ImmutableDictionary<string, string> parameters, TypeLoweringSymbols typeSymbols, FoldProofSymbols? fold)
    {
        var binderCounter = 0;
        return Emit(expression, ImmutableDictionary<string, string>.Empty);
        string Emit(ProofExpression current, ImmutableDictionary<string, string> binders) => current.Op switch
        {
            "param" when current.ReferenceId is { } id && parameters.TryGetValue(id, out var symbol) => symbol,
            "proof.bound" when current.BinderId is { } id && binders.TryGetValue(id, out var symbol) => symbol,
            "fold.prefixLength" when fold is not null => fold.PrefixLength,
            "fold.sequence" when fold is not null => fold.Sequence,
            "fold.initialAccumulator" when fold is not null => fold.InitialAccumulator,
            "fold.accumulator" when fold is not null => fold.Accumulator,
            "fold.environment" when fold is not null && current.Position is { } position => fold.Environment[position],
            "i64.const" or "math.const" => current.NumberValue!,
            "bool.const" => current.BoolValue!.Value ? "true" : "false",
            "eq" => $"({Emit(current.Args[0], binders)} == {Emit(current.Args[1], binders)})",
            "i64.add" or "math.add" => $"({Emit(current.Args[0], binders)} + {Emit(current.Args[1], binders)})",
            "i64.sub" or "math.sub" => $"({Emit(current.Args[0], binders)} - {Emit(current.Args[1], binders)})",
            "i64.le" or "math.le" => $"({Emit(current.Args[0], binders)} <= {Emit(current.Args[1], binders)})",
            "math.from_i64" => $"({Emit(current.Args[0], binders)} as int)",
            "bool.not" => $"(!{Emit(current.Args[0], binders)})",
            "bool.and" => $"StrictAnd({Emit(current.Args[0], binders)}, {Emit(current.Args[1], binders)})",
            "bool.or" => $"StrictOr({Emit(current.Args[0], binders)}, {Emit(current.Args[1], binders)})",
            "if" => $"(if {Emit(current.Args[0], binders)} then {Emit(current.Args[1], binders)} else {Emit(current.Args[2], binders)})",
            "record.make" => typeSymbols.ConstructRecord(current.RecordType!, current.FieldIds.Select((field, index) => (field, value: Emit(current.Args[index], binders))).OrderBy(pair => pair.field, StringComparer.Ordinal).Select(pair => pair.value).ToArray()),
            "record.get" => $"{Emit(current.Args[0], binders)}.{typeSymbols.RecordField(current.Args[0].Type.Name!, current.ReferenceId!)}",
            "seq.empty" => "[]",
            "seq.length" => $"(|{Emit(current.Args[0], binders)}| as I64)",
            "seq.get" => EmitProofSequenceGet(current, binders),
            "seq.append" => $"{Emit(current.Args[0], binders)} + [{Emit(current.Args[1], binders)}]",
            "seq.sum_i64" => $"SumI64({Emit(current.Args[0], binders)})",
            "seq.prefix_sum_i64" => $"PrefixSumI64({Emit(current.Args[0], binders)}, {Emit(current.Args[1], binders)} as int)",
            "forall.sequence" => EmitForAll(current, binders),
            _ => throw ModulesExceptionFactory.Error("lowering", "UnsupportedProofOpcode", details: new { current.Op })
        };
        string EmitForAll(ProofExpression current, ImmutableDictionary<string, string> binders)
        {
            var sequence = Emit(current.Sequence!, binders);
            var symbol = $"q{binderCounter++:D3}";
            var body = Emit(current.Body!, binders.Add(current.BinderId!, symbol));
            return $"(forall {symbol}: int {{:trigger {sequence}[{symbol}]}} :: 0 <= {symbol} < |{sequence}| ==> {body})";
        }
        string EmitProofSequenceGet(ProofExpression current, ImmutableDictionary<string, string> binders)
        {
            var sequence = Emit(current.Args[0], binders);
            var index = Emit(current.Args[1], binders);
            return current.Args[1].Op == "proof.bound" ? $"{sequence}[{index}]" : $"{sequence}[{index} as int]";
        }
    }

    private static string JoinParameters(IReadOnlyList<string> parameters, string last)
        => parameters.Count == 0 ? last : $"{string.Join(", ", parameters)}, {last}";

    private static ImmutableArray<DafnyInvariantSpan> ComputeLineSpans(byte[] source, ImmutableArray<int> lines)
    {
        var result = ImmutableArray.CreateBuilder<DafnyInvariantSpan>();
        var start = 0;
        var line = 1;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] != (byte)'\n') continue;
            if (lines.Contains(line)) result.Add(new DafnyInvariantSpan(start, index + 1 - start, line));
            start = index + 1;
            line++;
        }
        return result.ToImmutable();
    }

    private static byte[] NormalizeInvariantSpans(byte[] source, ImmutableArray<DafnyInvariantSpan> spans)
    {
        var marker = Encoding.UTF8.GetBytes("<STROGO_AGENT_INVARIANT>\n");
        using var stream = new MemoryStream();
        var position = 0;
        foreach (var span in spans.OrderBy(span => span.StartByte))
        {
            stream.Write(source, position, span.StartByte - position);
            stream.Write(marker);
            position = span.StartByte + span.Length;
        }
        stream.Write(source, position, source.Length - position);
        return stream.ToArray();
    }
}
