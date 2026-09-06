using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Kernel.Core;

public sealed class AdmissionVerifier
{
    private readonly ISolver solver;
    private readonly CoreLimits limits;
    public AdmissionVerifier(ISolver solver, CoreLimits? limits = null)
    {
        this.solver = solver ?? throw new ArgumentNullException(nameof(solver));
        this.limits = limits ?? new(); GraphValidator.ValidateLimits(this.limits);
        Wire.RequireDigest(solver.Identity.BinaryDigest); CanonicalJson.CheckAscii(solver.Identity.Version);
        if (string.IsNullOrWhiteSpace(solver.Identity.Version)) throw GraphValidator.Error("SolverIdentityMissing");
    }

    public async Task<VerificationResult> VerifyAsync(ValidatedProgram program, VerificationContext context, CancellationToken cancellationToken = default)
    {
        Wire.RequireDigest(context.PolicyRevision); Wire.RequireDigest(context.ManifestRevision);
        // A ValidatedProgram created by a harness with enforceFuel=false is not an admission bypass.
        GraphValidator.Validate(program.Program, limits, enforceFuel: true);
        var ir = Lowerer.Lower(program);
        var obligations = ImmutableArray.CreateBuilder<ObligationEvidence>();
        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        watchdog.CancelAfter(limits.AdmissionTimeoutMilliseconds);
        var queries = SmtEncoder.Encode(program);
        VerificationEvidence Evidence() => new(KernelVersions.Semantics, program.Revision, IrCodec.Revision(ir), context.PolicyRevision, context.ManifestRevision,
            KernelVersions.Encoder, KernelVersions.Reference, KernelVersions.Interpreter, KernelVersions.Lowering, solver.Identity, obligations.ToImmutable());
        VerificationResult Failure(VerificationStatus status, string code, ReserveInput? witness = null, EvaluationResult? replay = null) =>
            new(status, ir, Evidence(), KernelError.Create("verification", code, programRevision: program.Revision, witness: witness), witness, replay);
        var domain = await Solve("domain", queries.Domain, obligations, watchdog.Token);
        if (domain.ErrorCode is not null) return Failure(domain.Status, domain.ErrorCode);
        if (domain.Answer != "sat") return Failure(VerificationStatus.Error, "DomainUnsatisfiable");
        var main = await Solve("counterexample", queries.Counterexample, obligations, watchdog.Token);
        if (main.ErrorCode is not null) return Failure(main.Status, main.ErrorCode);
        if (main.Answer == "unsat") return new(VerificationStatus.Verified, ir, Evidence());
        var witnessResponse = await Solve("witness", queries.Witness, obligations, watchdog.Token, witness: true);
        if (witnessResponse.ErrorCode is not null) return Failure(witnessResponse.Status, witnessResponse.ErrorCode);
        if (witnessResponse.Answer != "sat") return Failure(VerificationStatus.Error, "VerifierMismatch");
        try
        {
            var model = SolverSyntax.ReadValues(witnessResponse.Output, "available", "quantity");
            var input = new ReserveInput(model["available"], model["quantity"]);
            if (ReserveContract.CheckInput(input) is not null) return Failure(VerificationStatus.Error, "VerifierMismatch", input);
            var replay = ReferenceInterpreter.Evaluate(program, input, limits.Fuel);
            var irReplay = IrInterpreter.Evaluate(ir, input, limits.Fuel);
            bool implementationsAgree = replay.Output == irReplay.Output && replay.Error?.Code == irReplay.Error?.Code && replay.Error?.EntityId == irReplay.Error?.EntityId &&
                replay.UsedFuel == irReplay.UsedFuel && ExecutionCodec.TraceBytes(replay).AsSpan().SequenceEqual(ExecutionCodec.TraceBytes(irReplay));
            bool reproduced = replay.Error?.Code == "ArithmeticOverflow" || replay.Output is { } output && ReserveContract.CheckOutput(input, output) is not null;
            if (!implementationsAgree || !reproduced) return Failure(VerificationStatus.Error, "VerifierMismatch", input, replay);
            return Failure(VerificationStatus.Counterexample, "Counterexample", input, replay);
        }
        catch (FormatException) { return Failure(VerificationStatus.Error, "SolverMalformedResponse"); }
    }

    public async Task<ArithmeticProbeResult> VerifyArithmeticProbeAsync(bool impossibleDomain = false, CancellationToken cancellationToken = default)
    {
        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        watchdog.CancelAfter(limits.AdmissionTimeoutMilliseconds);
        var obligations = ImmutableArray.CreateBuilder<ObligationEvidence>();
        var queries = SmtEncoder.EncodeArithmeticProbe(impossibleDomain);
        ArithmeticProbeResult Fail(VerificationStatus status, string code) => new(status, null, obligations.ToImmutable(), KernelError.Create("verification", code));
        var domain = await Solve("domain", queries.Domain, obligations, watchdog.Token);
        if (domain.ErrorCode is not null) return Fail(domain.Status, domain.ErrorCode);
        if (domain.Answer != "sat") return Fail(VerificationStatus.Error, "DomainUnsatisfiable");
        var main = await Solve("counterexample", queries.Counterexample, obligations, watchdog.Token);
        if (main.ErrorCode is not null) return Fail(main.Status, main.ErrorCode);
        // This fixed probe necessarily overflows at MaxI64. Unsat is itself a mismatch with the reference semantics.
        if (main.Answer != "sat") return Fail(VerificationStatus.Error, "VerifierMismatch");
        var witness = await Solve("witness", queries.Witness, obligations, watchdog.Token, witness: true);
        if (witness.ErrorCode is not null) return Fail(witness.Status, witness.ErrorCode);
        if (witness.Answer != "sat") return Fail(VerificationStatus.Error, "VerifierMismatch");
        try
        {
            long x = SolverSyntax.ReadValues(witness.Output, "x")["x"];
            BigInteger result = new BigInteger(x) + 1;
            if (x <= 0 || impossibleDomain || result >= long.MinValue && result <= long.MaxValue && result > x)
                return Fail(VerificationStatus.Error, "VerifierMismatch");
            return new(VerificationStatus.Counterexample, x, obligations.ToImmutable(), KernelError.Create("verification", "Counterexample", details: new { x = x.ToString(CultureInfo.InvariantCulture), operation = "i64.add_checked" }));
        }
        catch (FormatException) { return Fail(VerificationStatus.Error, "SolverMalformedResponse"); }
    }

    private sealed record SolveAnswer(string? Answer, VerificationStatus Status, string? ErrorCode, string Output);

    private async Task<SolveAnswer> Solve(string name, string query, ImmutableArray<ObligationEvidence>.Builder evidence, CancellationToken cancellationToken, bool witness = false)
    {
        SolverResponse response;
        try
        {
            // WaitAsync also enforces the deadline for a faulty trusted harness implementation that ignores cancellation.
            response = await solver.SolveAsync(query, TimeSpan.FromMilliseconds(limits.SolverTimeoutMilliseconds), cancellationToken)
                .WaitAsync(TimeSpan.FromMilliseconds(limits.SolverTimeoutMilliseconds), cancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        { response = new(-1, "", "", true); }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        { response = new(-1, "", exception.GetType().Name); }
        string status = "error";
        string? error = null;
        VerificationStatus result = VerificationStatus.Error;
        if (response.TimedOut || cancellationToken.IsCancellationRequested) { status = "timeout"; error = "SolverTimeout"; result = VerificationStatus.Timeout; }
        else if (response.ExitCode != 0 || !string.IsNullOrWhiteSpace(response.Stderr)) error = "SolverFailure";
        else
        {
            var text = response.Stdout.Trim();
            if (text == "unknown") { status = "unknown"; error = "SolverUnknown"; result = VerificationStatus.Unknown; }
            else if (!witness && (text is "sat" or "unsat")) status = text;
            else if (witness && text.StartsWith("sat", StringComparison.Ordinal) && text.Length > 3 && char.IsWhiteSpace(text[3])) status = "sat";
            else error = "SolverMalformedResponse";
        }
        evidence.Add(new(name, CanonicalJson.RawDigest(Encoding.UTF8.GetBytes(query)), status, query, response.Stdout, response.Stderr, response.ExitCode));
        return new(error is null ? status : null, result, error, response.Stdout);
    }
}

internal static class SolverSyntax
{
    private abstract record Expr;
    private sealed record Atom(string Text) : Expr;
    private sealed record ListExpr(List<Expr> Items) : Expr;
    internal static Dictionary<string, long> ReadValues(string output, params string[] names)
    {
        if (output.Length > 65536) throw new FormatException("Model too large");
        int position = 0;
        Expr Read(int depth)
        {
            if (depth > 8) throw new FormatException("Deep model");
            while (position < output.Length && char.IsWhiteSpace(output[position])) position++;
            if (position == output.Length) throw new FormatException("Missing expression");
            if (output[position] == '(')
            {
                position++; var items = new List<Expr>();
                while (true)
                {
                    while (position < output.Length && char.IsWhiteSpace(output[position])) position++;
                    if (position == output.Length) throw new FormatException("Unclosed model");
                    if (output[position] == ')') { position++; return new ListExpr(items); }
                    if (items.Count > 16) throw new FormatException("Wide model");
                    items.Add(Read(depth + 1));
                }
            }
            int start = position;
            while (position < output.Length && !char.IsWhiteSpace(output[position]) && output[position] != '(' && output[position] != ')') position++;
            if (position == start) throw new FormatException("Unexpected token");
            return new Atom(output[start..position]);
        }
        if (Read(0) is not Atom { Text: "sat" } || Read(0) is not ListExpr values || values.Items.Count != names.Length) throw new FormatException("Invalid model envelope");
        while (position < output.Length && char.IsWhiteSpace(output[position])) position++;
        if (position != output.Length) throw new FormatException("Trailing model data");
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var item in values.Items)
        {
            if (item is not ListExpr { Items.Count: 2 } pair || pair.Items[0] is not Atom name || !names.Contains(name.Text, StringComparer.Ordinal)) throw new FormatException("Unexpected model binding");
            string decimalValue = pair.Items[1] switch
            {
                Atom atom when atom.Text.All(char.IsAsciiDigit) && atom.Text.Length > 0 => atom.Text,
                ListExpr { Items.Count: 2 } negative when negative.Items[0] is Atom { Text: "-" } && negative.Items[1] is Atom number && number.Text.Length > 0 && number.Text.All(char.IsAsciiDigit) => "-" + number.Text,
                _ => throw new FormatException("Noninteger model value")
            };
            if (!long.TryParse(decimalValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value) || !result.TryAdd(name.Text, value)) throw new FormatException("Duplicate or out-of-range model value");
        }
        return result;
    }
}
