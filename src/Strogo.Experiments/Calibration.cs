using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kernel.Core;
using Strogo.Notation;

namespace Strogo.Experiments;

public static class CalibrationIdentity
{
    public const string Protocol = "strogo.e09.offline-calibration.v0.1";
    public const string Corpus = "e09-reserve.v0.1";
    public const string Evaluator = "arm-adapter>strict-parse-compile>graph-validator>lowerer>reference+ir>reserve-oracle>compare";
}

public sealed record CalibrationInput(long Available, long Quantity);
public sealed record CalibrationCase(string Id, KernelProgram Program, string NotationSource, ImmutableArray<CalibrationInput> Inputs, string VectorTag);
public sealed record StageResult(string Stage, string Status, string? Code = null);
public sealed record ArmEvaluation(string CaseId, string Arm, string Status, string ProgramRevision, string IrRevision, ImmutableArray<StageResult> Stages, ImmutableArray<string> Outcomes);
public sealed record PairEvaluation(string CaseId, ArmEvaluation Graph, ArmEvaluation Notation, bool Equivalent);
public sealed record CalibrationReport(string Protocol, string Corpus, string Evaluator, string Status, int PositiveCases, int NegativeCases, ImmutableArray<PairEvaluation> Pairs, ImmutableArray<string> NegativeCategories, string ReportDigest);
public sealed record ExportManifest(string Protocol, string Corpus, string CaseId, string Arm, string Mode, string DataOrigin, string SourceDigest, string CandidateDigest, string FrontendRevision, string CoreSchema, string HostRevision, string SolverDigest, string OracleRevision);
public sealed record ExportBundle(ExportManifest Manifest, byte[] CandidateBytes);

public static class ReserveOracle
{
    public const string Revision = "reserve-oracle.bigint.v0.1";
    public static ReserveOutput? Evaluate(ReserveInput input, out string? error)
    {
        error = null;
        if (input.Available < 0) { error = "StateInvariantFailed"; return null; }
        if (input.Quantity <= 0) { error = "PreconditionFailed"; return null; }
        BigInteger available = input.Available, quantity = input.Quantity;
        bool accepted = quantity <= available;
        BigInteger reserved = accepted ? quantity : BigInteger.Zero;
        BigInteger remaining = accepted ? available - quantity : available;
        if (remaining < long.MinValue || remaining > long.MaxValue || reserved < long.MinValue || reserved > long.MaxValue)
        { error = "ArithmeticOverflow"; return null; }
        var output = new ReserveOutput(accepted, (long)remaining, (long)reserved);
        error = ReserveContract.CheckOutput(input, output)?.Code;
        return error is null ? output : null;
    }
}

public static class CalibrationCorpus
{
    public static ImmutableArray<CalibrationCase> Create() =>
    [
        Case("R01-baseline", Baseline(), Source("baseline"), "baseline"),
        Case("R02-bool-true", BoolTrue(), Source("bool.true"), "bool.true"),
        Case("R03-bool-false", BoolFalse(), Source("bool.false"), "bool.false"),
        Case("R04-not", Not(), Source("bool.not"), "bool.not"),
        Case("R05-and", And(), Source("bool.and"), "bool.and"),
        Case("R06-or", Or(), Source("bool.or"), "bool.or"),
        Case("R07-eq", Eq(), Source("i64.eq"), "i64.eq"),
        Case("R08-bool-select", SelectCase(), Source("select"), "select"),
        Case("R09-add", Checked("add"), Source("checked.add"), "i64.add_checked"),
        Case("R10-min", Checked("min"), Source("checked.min"), "i64.sub_checked.min"),
        Case("R11-max", Checked("max"), Source("checked.max"), "i64.sub_checked.max"),
        Case("R12-order", Baseline(), Source("order"), "declaration.order")
    ];

    private static CalibrationCase Case(string id, KernelProgram program, string source, string tag) =>
        new(id, program, source, [new(10, 3), new(3, 5), new(long.MaxValue, 1), new(0, 1)], tag);

    private static string Source(string variant) => variant switch
    {
        "baseline" or "order" => "{ long available = input.resourceAvailable; long quantity = input.requestedQuantity; bool enough = quantity <= available; long zero = 0L; long debit = Select(enough, quantity, zero); long remaining = checked(available - debit); return (accepted: enough, available: remaining, reserved: debit); }",
        "bool.true" => "{ long available = input.resourceAvailable; long quantity = input.requestedQuantity; bool enough = quantity <= available; long zero = 0L; bool flag = true; bool not_flag = !flag; bool flag_ok = flag | not_flag; long debit = Select(enough, quantity, zero); long remaining = checked(available - debit); bool accepted = enough & flag_ok; return (accepted: accepted, available: remaining, reserved: debit); }",
        "bool.false" => "{ long available = input.resourceAvailable; long quantity = input.requestedQuantity; bool enough = quantity <= available; long zero = 0L; bool flag = false; bool flag_ok = !flag; long debit = Select(enough, quantity, zero); long remaining = checked(available - debit); bool accepted = enough & flag_ok; return (accepted: accepted, available: remaining, reserved: debit); }",
        "bool.not" => "{ long available = input.resourceAvailable; long quantity = input.requestedQuantity; bool enough = quantity <= available; long zero = 0L; bool marker = !enough; bool marker_ok = marker | enough; long debit = Select(enough, quantity, zero); long remaining = checked(available - debit); bool accepted = enough & marker_ok; return (accepted: accepted, available: remaining, reserved: debit); }",
        "bool.and" => "{ long available = input.resourceAvailable; long quantity = input.requestedQuantity; bool enough = quantity <= available; long zero = 0L; long debit = Select(enough, quantity, zero); long remaining = checked(available - debit); bool accepted = enough & enough; return (accepted: accepted, available: remaining, reserved: debit); }",
        "bool.or" => "{ long available = input.resourceAvailable; long quantity = input.requestedQuantity; bool enough = quantity <= available; long zero = 0L; long debit = Select(enough, quantity, zero); long remaining = checked(available - debit); bool accepted = enough | enough; return (accepted: accepted, available: remaining, reserved: debit); }",
        "i64.eq" => "{ long available = input.resourceAvailable; long quantity = input.requestedQuantity; bool enough = quantity <= available; long zero = 0L; bool equal = quantity == quantity; long debit = Select(enough, quantity, zero); long remaining = checked(available - debit); bool accepted = enough & equal; return (accepted: accepted, available: remaining, reserved: debit); }",
        "select" => "{ long available = input.resourceAvailable; long quantity = input.requestedQuantity; bool enough = quantity <= available; long zero = 0L; bool true_value = true; bool false_value = false; bool choice = Select(enough, true_value, false_value); long debit = Select(enough, quantity, zero); long remaining = checked(available - debit); bool accepted = enough & choice; return (accepted: accepted, available: remaining, reserved: debit); }",
        "checked.add" => "{ long available = input.resourceAvailable; long quantity = input.requestedQuantity; bool enough = quantity <= available; long zero = 0L; long offset = 0L; long marker = checked(offset + offset); bool marker_ok = marker == zero; long debit = Select(enough, quantity, zero); long remaining = checked(available - debit); bool accepted = enough & marker_ok; return (accepted: accepted, available: remaining, reserved: debit); }",
        "checked.min" => "{ long available = input.resourceAvailable; long quantity = input.requestedQuantity; bool enough = quantity <= available; long zero = 0L; long offset = -9223372036854775808L; long marker = checked(offset - offset); bool marker_ok = marker == zero; long debit = Select(enough, quantity, zero); long remaining = checked(available - debit); bool accepted = enough & marker_ok; return (accepted: accepted, available: remaining, reserved: debit); }",
        "checked.max" => "{ long available = input.resourceAvailable; long quantity = input.requestedQuantity; bool enough = quantity <= available; long zero = 0L; long offset = 9223372036854775807L; long marker = checked(offset - offset); bool marker_ok = marker == zero; long debit = Select(enough, quantity, zero); long remaining = checked(available - debit); bool accepted = enough & marker_ok; return (accepted: accepted, available: remaining, reserved: debit); }",
        _ => throw new ArgumentOutOfRangeException(nameof(variant))
    };

    private static KernelProgram Base(ImmutableArray<KernelNode> nodes, string accepted) => new(KernelVersions.Schema, "reserve", "reserve.v0", nodes, new(accepted, "n.remaining", "n.debit"));
    private static ImmutableArray<KernelNode> Prefix() =>
    [new("n.available", "input", KernelType.I64, [], "state.available"), new("n.quantity", "input", KernelType.I64, [], "event.quantity"),
     new("n.enough", "i64.le", KernelType.Bool, ["n.quantity", "n.available"]), new("n.zero", "i64.const", KernelType.I64, [], null, 0),
     new("n.debit", "select", KernelType.I64, ["n.enough", "n.quantity", "n.zero"]), new("n.remaining", "i64.sub_checked", KernelType.I64, ["n.available", "n.debit"])] ;
    private static KernelProgram Baseline() => Base(Prefix(), "n.enough");
    private static KernelProgram BoolTrue() { var n = Prefix().ToBuilder(); n.Add(new("n.bool.true", "bool.const", KernelType.Bool, [], null, null, true)); n.Add(new("n.not_flag", "bool.not", KernelType.Bool, ["n.bool.true"])); n.Add(new("n.flag_ok", "bool.or", KernelType.Bool, ["n.bool.true", "n.not_flag"])); n.Add(new("n.accepted", "bool.and", KernelType.Bool, ["n.enough", "n.flag_ok"])); return Base(n.ToImmutable(), "n.accepted"); }
    private static KernelProgram BoolFalse() { var n = Prefix().ToBuilder(); n.Add(new("n.bool.false", "bool.const", KernelType.Bool, [], null, null, false)); n.Add(new("n.flag_ok", "bool.not", KernelType.Bool, ["n.bool.false"])); n.Add(new("n.accepted", "bool.and", KernelType.Bool, ["n.enough", "n.flag_ok"])); return Base(n.ToImmutable(), "n.accepted"); }
    private static KernelProgram Not() { var n = Prefix().ToBuilder(); n.Add(new("n.marker", "bool.not", KernelType.Bool, ["n.enough"])); n.Add(new("n.marker_ok", "bool.or", KernelType.Bool, ["n.marker", "n.enough"])); n.Add(new("n.accepted", "bool.and", KernelType.Bool, ["n.enough", "n.marker_ok"])); return Base(n.ToImmutable(), "n.accepted"); }
    private static KernelProgram And() { var n = Prefix().ToBuilder(); n.Add(new("n.accepted", "bool.and", KernelType.Bool, ["n.enough", "n.enough"])); return Base(n.ToImmutable(), "n.accepted"); }
    private static KernelProgram Or() { var n = Prefix().ToBuilder(); n.Add(new("n.accepted", "bool.or", KernelType.Bool, ["n.enough", "n.enough"])); return Base(n.ToImmutable(), "n.accepted"); }
    private static KernelProgram Eq() { var n = Prefix().ToBuilder(); n.Add(new("n.equal", "i64.eq", KernelType.Bool, ["n.quantity", "n.quantity"])); n.Add(new("n.accepted", "bool.and", KernelType.Bool, ["n.enough", "n.equal"])); return Base(n.ToImmutable(), "n.accepted"); }
    private static KernelProgram SelectCase() { var n = Prefix().ToBuilder(); n.Add(new("n.bool.true", "bool.const", KernelType.Bool, [], null, null, true)); n.Add(new("n.bool.false", "bool.const", KernelType.Bool, [], null, null, false)); n.Add(new("n.choice", "select", KernelType.Bool, ["n.enough", "n.bool.true", "n.bool.false"])); n.Add(new("n.accepted", "bool.and", KernelType.Bool, ["n.enough", "n.choice"])); return Base(n.ToImmutable(), "n.accepted"); }
    private static KernelProgram Checked(string kind) { var n = Prefix().ToBuilder(); long value = kind == "min" ? long.MinValue : kind == "max" ? long.MaxValue : 0; string operand = value == 0 ? "n.zero" : $"n.const.i64.{value}"; if (value != 0) n.Add(new(operand, "i64.const", KernelType.I64, [], null, value)); n.Add(new("n.marker", kind == "add" ? "i64.add_checked" : "i64.sub_checked", KernelType.I64, [operand, operand])); n.Add(new("n.marker_ok", "i64.eq", KernelType.Bool, ["n.marker", "n.zero"])); n.Add(new("n.accepted", "bool.and", KernelType.Bool, ["n.enough", "n.marker_ok"])); return Base(n.ToImmutable(), "n.accepted"); }
}

public static class CalibrationEvaluator
{
    public static ArmEvaluation Evaluate(CalibrationCase c, string arm)
    {
        var stages = ImmutableArray.CreateBuilder<StageResult>();
        try
        {
            KernelProgram p;
            if (arm == "graph-json") { p = ProgramCodec.Parse(Encoding.UTF8.GetString(ProgramCodec.CanonicalBytes(c.Program))); stages.Add(new("arm-adapter", "Accepted")); }
            else if (arm == "strogo-notation") { var result = NotationCompiler.Compile(Encoding.UTF8.GetBytes(c.NotationSource)); if (!result.Accepted) return Refused(c.Id, arm, result.ErrorCode ?? "NotationRefused", stages); p = result.Program!; stages.Add(new("arm-adapter", "Accepted")); }
            else return Refused(c.Id, arm, "UnknownArm", stages);
            stages.Add(new("strict-parse-compile", "Accepted")); var validated = GraphValidator.Validate(p); stages.Add(new("graph-validator", "Accepted")); var ir = Lowerer.Lower(validated); stages.Add(new("lowerer", "Accepted"));
            var outcomes = ImmutableArray.CreateBuilder<string>();
            foreach (var input in c.Inputs)
            {
                var reference = ReferenceInterpreter.Evaluate(validated, new(input.Available, input.Quantity)); var machine = IrInterpreter.Evaluate(ir, new(input.Available, input.Quantity));
                var oracle = ReserveOracle.Evaluate(new(input.Available, input.Quantity), out var oracleError);
                string referenceToken = Token(reference, oracle, oracleError), machineToken = Token(machine, oracle, oracleError);
                if (referenceToken != machineToken) return Refused(c.Id, arm, "EvaluatorMismatch", stages);
                outcomes.Add(referenceToken);
            }
            stages.Add(new("reference-interpreter", "Accepted")); stages.Add(new("ir-interpreter", "Accepted")); stages.Add(new("reserve-oracle", "Accepted")); stages.Add(new("compare", "Equivalent"));
            return new(c.Id, arm, "Accepted", ProgramCodec.Revision(p), IrCodec.Revision(ir), stages.ToImmutable(), outcomes.ToImmutable());
        }
        catch (KernelException e) { return Refused(c.Id, arm, e.Error.Code, stages); }
        catch (Exception e) { return Refused(c.Id, arm, e.GetType().Name, stages); }
    }
    private static ArmEvaluation Refused(string id, string arm, string code, ImmutableArray<StageResult>.Builder stages) { stages.Add(new("compare", "Refused", code)); return new(id, arm, "Refused", "", "", stages.ToImmutable(), []); }
    private static string Token(EvaluationResult result, ReserveOutput? oracle, string? oracleError) => result.Error?.Code ?? (result.Output is null ? "NoOutput" : oracleError ?? (result.Output == oracle ? "Output" : "OracleMismatch"));
    public static PairEvaluation EvaluatePair(CalibrationCase c) { var graph = Evaluate(c, "graph-json"); var notation = Evaluate(c, "strogo-notation"); return new(c.Id, graph, notation, graph.Status == "Accepted" && notation.Status == "Accepted" && graph.ProgramRevision == notation.ProgramRevision && graph.IrRevision == notation.IrRevision && graph.Outcomes.SequenceEqual(notation.Outcomes)); }
    public static CalibrationReport Run()
    {
        var pairs = CalibrationCorpus.Create().Select(EvaluatePair).ToImmutableArray();
        var negative = ImmutableArray.Create("MalformedJson", "UnsupportedOpcode", "TypeMismatch", "DanglingReference", "CycleDetected", "UnreachableNode", "InvalidUtf8", "UnsupportedSyntax", "CommentSyntax", "DeepDelimiter", "UnaryChain", "WrongRefusal", "InvalidCandidate", "InfrastructureFailure", "EvaluatorMismatch", "ManifestDigestMismatch", "MissingAllowlistedField", "ExtraManifestField", "StarterCollision", "ExpectedGraphLeak");
        string status = pairs.All(p => p.Equivalent) ? "Accepted" : "Refused";
        var skeleton = new { protocol = CalibrationIdentity.Protocol, corpus = CalibrationIdentity.Corpus, evaluator = CalibrationIdentity.Evaluator, status, positiveCases = pairs.Length, negativeCases = negative.Length, pairs, negativeCategories = negative };
        string digest = Convert.ToHexStringLower(SHA256.HashData(CanonicalJson.Encode(skeleton)));
        return new(CalibrationIdentity.Protocol, CalibrationIdentity.Corpus, CalibrationIdentity.Evaluator, status, pairs.Length, negative.Length, pairs, negative, digest);
    }
    public static byte[] ReportBytes(CalibrationReport report) => CanonicalJson.Encode(report);
}

public static class Exporter
{
    public static ExportBundle Export(CalibrationCase c, string arm)
    {
        byte[] expected = ProgramCodec.CanonicalBytes(c.Program);
        byte[] starter = Encoding.UTF8.GetBytes("{\"schemaVersion\":\"kernel.v0\",\"programId\":\"reserve\",\"profileId\":\"reserve.v0\",\"nodes\":[],\"outputs\":{\"accepted\":\"n.missing\",\"available\":\"n.missing\",\"reserved\":\"n.missing\"}}\n");
        if (starter.AsSpan().SequenceEqual(expected)) throw new InvalidOperationException("StarterCollision");
        var manifest = new ExportManifest(CalibrationIdentity.Protocol, CalibrationIdentity.Corpus, c.Id, arm, "job-starter", "scripted-fixture", CanonicalJson.RawDigest(Encoding.UTF8.GetBytes(c.NotationSource)), CanonicalJson.RawDigest(starter), NotationCompiler.GrammarRevision, KernelVersions.Schema, "host.e09.v0.1", "solver.none", ReserveOracle.Revision);
        return new(manifest, starter);
    }

    public static bool ValidateStarter(ExportBundle bundle, CalibrationCase trusted, out string code)
    {
        code = "";
        if (bundle.Manifest.Protocol != CalibrationIdentity.Protocol || bundle.Manifest.CaseId != trusted.Id) { code = "ManifestMismatch"; return false; }
        if (bundle.Manifest.CandidateDigest != CanonicalJson.RawDigest(bundle.CandidateBytes)) { code = "ArtifactMismatch"; return false; }
        if (bundle.CandidateBytes.AsSpan().SequenceEqual(ProgramCodec.CanonicalBytes(trusted.Program))) { code = "ExpectedGraphLeak"; return false; }
        try { var parsed = ProgramCodec.Parse(Encoding.UTF8.GetString(bundle.CandidateBytes)); GraphValidator.Validate(parsed); code = "StarterAccepted"; return false; }
        catch (KernelException) { return true; }
        catch { code = "InfrastructureFailure"; return false; }
    }
}
