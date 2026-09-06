using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Kernel.Core;

namespace Kernel.Conformance;

internal static class CoreCases
{
    public static void Register(List<ConformanceCase> cases)
    {
        Add("canonical-bytes-and-identity", ["AC1", "AC12"], Canonical);
        Add("strict-schema-and-graph-rejections", ["AC1", "AC4", "AC8"], Schema);
        Add("checked-arithmetic-and-strict-select", ["AC2", "AC8", "AC9"], Arithmetic);
        Add("reserve-independent-business-grid", ["AC6", "AC9"], BusinessGrid);
        Add("seeded-200-dags-independent-oracle", ["AC2", "AC9"], GeneratedDags);
        Add("ir-validation-rejects-invalid-input", ["AC4", "AC9"], InvalidIr);
        cases.Add(new("core", "real-z3-reserve-and-counterexample", ["AC3", "AC12"], RealSolver));
        cases.Add(new("core", "solver-fail-closed-statuses", ["AC3"], FailedSolvers));
        void Add(string name, string[] ac, Action<TestContext> run) => cases.Add(new("core", name, ac, c => { run(c); return Task.CompletedTask; }));
    }

    private static void Canonical(TestContext c)
    {
        var program = Fixtures.Reserve();
        const string expected = """{"nodes":[{"args":[],"fieldId":"state.available","id":"n.available","op":"input","type":"I64"},{"args":["n.enough","n.quantity","n.zero"],"id":"n.debit","op":"select","type":"I64"},{"args":["n.quantity","n.available"],"id":"n.enough","op":"i64.le","type":"Bool"},{"args":[],"fieldId":"event.quantity","id":"n.quantity","op":"input","type":"I64"},{"args":["n.available","n.debit"],"id":"n.remaining","op":"i64.sub_checked","type":"I64"},{"args":[],"id":"n.zero","op":"i64.const","type":"I64","value":"0"}],"outputs":{"accepted":"n.enough","available":"n.remaining","reserved":"n.debit"},"profileId":"reserve.v0","programId":"reserve","schemaVersion":"kernel.v0"}""";
        var actual = ProgramCodec.CanonicalBytes(program);
        c.Equal(expected, Encoding.UTF8.GetString(actual), "canonical bytes match independent golden");
        c.Equal(Fixtures.IndependentHash("program", Encoding.UTF8.GetBytes(expected)), ProgramCodec.Revision(program), "program domain-separated SHA-256");
        var permuted = program with { Nodes = [.. program.Nodes.Reverse()] };
        c.SequenceEqual(actual, ProgramCodec.CanonicalBytes(permuted), "node transport order does not affect bytes");
        string escaped = Fixtures.ReserveJson.Replace("\"reserve\"", "\"r\\u0065serve\"", StringComparison.Ordinal);
        c.Equal(ProgramCodec.Revision(program), ProgramCodec.Revision(ProgramCodec.Parse(escaped)), "ASCII escape has same identity");
        var roundTrip = ProgramCodec.Parse("\n " + expected + "\r\n");
        c.Equal(ProgramCodec.Revision(program), ProgramCodec.Revision(roundTrip), "whitespace and key order do not affect identity");
        c.SequenceEqual(new[] { "n.available", "n.quantity", "n.enough", "n.zero", "n.debit", "n.remaining" },
            GraphValidator.Validate(program).TopologicalNodes.Select(n => n.Id), "canonical ready-node topological order");
        var changed = Fixtures.Replace(program, Fixtures.I64("n.zero", 1));
        c.True(ProgramCodec.Revision(program) != ProgramCodec.Revision(changed), "content changes program revision");
        c.True(ProgramCodec.NodeRevision(program.Nodes.Single(n => n.Id == "n.zero")) != ProgramCodec.NodeRevision(changed.Nodes.Single(n => n.Id == "n.zero")), "content changes node revision while ID stays stable");
        c.Evidence["canonicalProgram"] = expected;
        c.Evidence["programRevision"] = ProgramCodec.Revision(program);
    }

    private static void Schema(TestContext c)
    {
        var source = Fixtures.ReserveJson;
        var mutations = new Dictionary<string, string>
        {
            ["duplicate-top-key"] = source.Replace("\"schemaVersion\":\"kernel.v0\"", "\"schemaVersion\":\"kernel.v0\",\"schemaVersion\":\"kernel.v0\""),
            ["duplicate-node-key"] = source.Replace("\"id\":\"n.zero\"", "\"id\":\"n.zero\",\"id\":\"n.zero\""),
            ["unknown-top-property"] = source.Replace("\"schemaVersion\":", "\"verified\":true,\"schemaVersion\":"),
            ["policy-in-program"] = source.Replace("\"schemaVersion\":", "\"policy\":{},\"schemaVersion\":"),
            ["manifest-in-program"] = source.Replace("\"schemaVersion\":", "\"manifest\":{},\"schemaVersion\":"),
            ["unknown-node-property"] = source.Replace("\"id\":\"n.zero\"", "\"forgedType\":true,\"id\":\"n.zero\""),
            ["negative-zero"] = source.Replace("\"value\":\"0\"", "\"value\":\"-0\""),
            ["numeric-i64"] = source.Replace("\"value\":\"0\"", "\"value\":0"),
            ["i64-leading-plus"] = source.Replace("\"value\":\"0\"", "\"value\":\"+1\""),
            ["i64-exponent"] = source.Replace("\"value\":\"0\"", "\"value\":\"1e2\""),
            ["i64-leading-zero"] = source.Replace("\"value\":\"0\"", "\"value\":\"01\""),
            ["i64-too-large"] = source.Replace("\"value\":\"0\"", "\"value\":\"9223372036854775808\""),
            ["unknown-type"] = source.Replace("\"type\":\"I64\"", "\"type\":\"Decimal\""),
            ["non-ascii-id"] = source.Replace("n.zero", "n.ноль"),
            ["wrong-profile"] = source.Replace("reserve.v0", "arbitrary.v0"),
            ["missing-field"] = source.Replace("\"schemaVersion\":\"kernel.v0\",", ""),
            ["unknown-input"] = source.Replace("state.available", "state.secret"),
            ["io-opcode"] = source.Replace("i64.sub_checked", "http.send")
        };
        var rejected = new Dictionary<string, string>();
        foreach (var (name, json) in mutations)
        {
            var error = c.Throws<KernelException>(() => GraphValidator.Validate(ProgramCodec.Parse(json)), name);
            c.True(!string.IsNullOrWhiteSpace(error.Error.Code), name + " has structured code");
            rejected[name] = error.Error.Code;
        }
        var p = Fixtures.Reserve();
        var invalidGraphs = new Dictionary<string, KernelProgram>
        {
            ["dangling"] = Fixtures.Replace(p, Fixtures.Op("n.remaining", "i64.sub_checked", KernelType.I64, "missing", "n.debit")),
            ["cycle"] = Fixtures.Replace(p, Fixtures.Op("n.remaining", "i64.sub_checked", KernelType.I64, "n.remaining", "n.debit")),
            ["dead"] = p with { Nodes = p.Nodes.Add(Fixtures.I64("unused", 1)) },
            ["duplicate-id"] = p with { Nodes = p.Nodes.Add(p.Nodes[0]) },
            ["wrong-output-type"] = p with { Outputs = p.Outputs with { Accepted = "n.available" } },
            ["wrong-operand-type"] = Fixtures.Replace(p, Fixtures.Op("n.remaining", "i64.sub_checked", KernelType.I64, "n.enough", "n.debit")),
            ["wrong-arity"] = Fixtures.Replace(p, Fixtures.Op("n.remaining", "i64.sub_checked", KernelType.I64, "n.debit"))
        };
        foreach (var (name, graph) in invalidGraphs)
        {
            var error = c.Throws<KernelException>(() => GraphValidator.Validate(graph), name);
            c.True(!string.IsNullOrWhiteSpace(error.Error.Code), name + " structured code");
            rejected[name] = error.Error.Code;
        }
        c.Throws<KernelException>(() => ProgramCodec.Parse(new string(' ', 65537) + source), "transport byte limit");
        c.Throws<KernelException>(() => ProgramCodec.Parse(source, new CoreLimits(MaxJsonDepth: 2)), "JSON depth limit");
        c.Throws<KernelException>(() => GraphValidator.Validate(p, new CoreLimits(MaxNodes: 5)), "node limit");
        Fixtures.Error(c, "BudgetExceeded", () => GraphValidator.Validate(p, new CoreLimits(Fuel: 5)), "six nodes exceed admission fuel five");
        c.Evidence["rejections"] = rejected;
    }

    private static void Arithmetic(TestContext c)
    {
        (string Op, long A, long B, long? Expected)[] vectors = [
            ("i64.add_checked", long.MaxValue, 1, null), ("i64.sub_checked", long.MinValue, 1, null),
            ("i64.add_checked", long.MaxValue, 0, long.MaxValue), ("i64.add_checked", long.MinValue, 0, long.MinValue),
            ("i64.sub_checked", long.MaxValue, -1, null), ("i64.add_checked", long.MinValue, -1, null),
            ("i64.add_checked", -1, 1, 0), ("i64.sub_checked", 0, 1, -1),
            ("i64.sub_checked", long.MinValue, long.MinValue, 0), ("i64.add_checked", long.MaxValue, long.MinValue, -1)
        ];
        foreach (var v in vectors)
        {
            var p = GraphValidator.Validate(Fixtures.Arithmetic(v.Op, v.A, v.B));
            var result = Compare(c, p, new(0, 1), 128);
            if (v.Expected is long expected)
            {
                c.True(result.Success, "boundary arithmetic succeeds");
                c.Equal(expected, result.Output!.Available, "independent arithmetic vector");
            }
            else
            {
                c.Equal("ArithmeticOverflow", result.Error?.Code, "overflow never wraps");
                c.Equal("c", result.Error?.EntityId, "overflow origin node");
                c.Equal(3, result.UsedFuel, "overflowing attempt consumes fuel");
            }
        }
        var strict = Compare(c, GraphValidator.Validate(Fixtures.StrictOverflow()), new(0, 1), 128);
        c.Equal("ArithmeticOverflow", strict.Error?.Code, "unselected overflow operand is still evaluated");
        c.Equal("overflow", strict.Error?.EntityId, "strict select overflow origin");
        var reserve = GraphValidator.Validate(Fixtures.Reserve());
        var limited = Compare(c, reserve, new(10, 3), 5);
        c.Equal("BudgetExceeded", limited.Error?.Code, "fuel five refuses sixth node");
        c.Equal("n.remaining", limited.Error?.EntityId, "fuel error before sixth node");
        c.Equal(5, limited.UsedFuel, "unexecuted sixth node does not consume fuel");
        c.True(limited.Trace.All(t => t.NodeId != "n.remaining" || t.Value is null), "sixth node has no computed value");
        var exact = Compare(c, reserve, new(10, 3), 6);
        c.True(exact.Success, "exact fuel succeeds");
        c.Equal(6, exact.UsedFuel, "six-node graph consumes exactly six");
        c.Evidence["boundaryVectors"] = vectors.Length;
    }

    private static void BusinessGrid(TestContext c)
    {
        long[] available = [0, 1, 2, 9, 10, long.MaxValue - 1, long.MaxValue];
        long[] quantity = [1, 2, 3, 10, 11, long.MaxValue];
        var program = GraphValidator.Validate(Fixtures.Reserve());
        foreach (long a in available)
            foreach (long q in quantity)
            {
                var actual = Compare(c, program, new(a, q), 128);
                c.True(actual.Success, $"reserve ({a},{q}) is total");
                c.Equal(Fixtures.BusinessOracle(a, q), actual.Output, $"independent reserve oracle ({a},{q})");
            }
        c.Evidence["vectors"] = available.Length * quantity.Length;
    }

    private static EvaluationResult Compare(TestContext c, ValidatedProgram program, ReserveInput input, int fuel)
    {
        var graph = ReferenceInterpreter.Evaluate(program, input, fuel);
        var ir = IrInterpreter.Evaluate(Lowerer.Lower(program), input, fuel);
        c.Equal(graph.Output, ir.Output, "graph/IR output");
        c.Equal(graph.Error?.Code, ir.Error?.Code, "graph/IR error code");
        c.Equal(graph.Error?.EntityId, ir.Error?.EntityId, "graph/IR error node");
        c.Equal(graph.UsedFuel, ir.UsedFuel, "graph/IR fuel");
        c.Equal(JsonSerializer.Serialize(graph.Trace), JsonSerializer.Serialize(ir.Trace), "graph/IR normalized trace");
        return graph;
    }

    private static void InvalidIr(TestContext c)
    {
        var ir = Lowerer.Lower(GraphValidator.Validate(Fixtures.Reserve()));
        c.Throws<KernelException>(() => IrValidator.Validate(ir with { Instructions = ir.Instructions.SetItem(0, ir.Instructions[0] with { DestinationIndex = 2 }) }), "invalid IR destination");
        c.Throws<KernelException>(() => IrValidator.Validate(ir with { Instructions = ir.Instructions.SetItem(2, ir.Instructions[2] with { OperandIndices = [5, 0] }) }), "forward IR reference");
        c.Throws<KernelException>(() => IrValidator.Validate(ir with { Instructions = ir.Instructions.SetItem(0, ir.Instructions[0] with { Opcode = "http.send" }) }), "unknown IR opcode");
        c.Throws<KernelException>(() => IrValidator.Validate(ir with { Outputs = ir.Outputs with { Accepted = 0 } }), "wrong IR output type");
        c.Throws<KernelException>(() => IrValidator.Validate(ir with { SemanticsVersion = "kernel.future" }), "unknown IR semantics");
    }

    private static async Task RealSolver(TestContext c)
    {
        var solver = await Toolchain.SolverAsync(c);
        var verifier = new AdmissionVerifier(solver);
        var context = new VerificationContext(new string('b', 64), new string('c', 64));
        var good = await verifier.VerifyAsync(GraphValidator.Validate(Fixtures.Reserve()), context);
        c.True(good.IsVerified, "real Z3 admits correct Reserve");
        c.True(good.Evidence.Obligations.Length >= 2, "domain and counterexample obligations present");
        c.True(good.Evidence.Obligations.Any(o => o.Status == "sat"), "domain has witness");
        c.True(good.Evidence.Obligations.Any(o => o.Status == "unsat"), "no contract/overflow counterexample for Reserve");
        var bad = await verifier.VerifyAsync(GraphValidator.Validate(Fixtures.BadAdd()), context);
        c.Equal(VerificationStatus.Counterexample, bad.Status, "add instead of sub refused");
        c.True(bad.Witness is not null, "counterexample includes exact inputs");
        c.True(bad.WitnessReplay is not null, "counterexample replay saved");
        var witness = bad.Witness!;
        var replay = ReferenceInterpreter.Evaluate(GraphValidator.Validate(Fixtures.BadAdd()), witness);
        c.True(replay.Error is not null || replay.Output != Fixtures.BusinessOracle(witness.Available, witness.Quantity), "witness independently violates business oracle");
        c.Evidence["verified"] = good.Evidence;
        c.Evidence["counterexample"] = bad;
        var strictReserve = Fixtures.Reserve();
        strictReserve = strictReserve with { Nodes = strictReserve.Nodes.AddRange(new KernelNode[] {
            Fixtures.I64("n.one", 1), Fixtures.Bool("n.false", false),
            Fixtures.Op("n.overflow", "i64.add_checked", KernelType.I64, "n.available", "n.one"),
            Fixtures.Op("n.safezero", "select", KernelType.I64, "n.false", "n.overflow", "n.zero") }) };
        strictReserve = Fixtures.Replace(strictReserve, Fixtures.Op("n.debit", "select", KernelType.I64, "n.enough", "n.quantity", "n.safezero"));
        var checkedStrict = GraphValidator.Validate(strictReserve);
        var strictProof = await verifier.VerifyAsync(checkedStrict, context);
        c.Equal(VerificationStatus.Counterexample, strictProof.Status, "real Reserve encoder must check overflow even in unselected branch");
        c.Equal(long.MaxValue, strictProof.Witness?.Available, "strict graph witness reaches machine boundary");
        c.Equal("ArithmeticOverflow", strictProof.WitnessReplay?.Error?.Code, "strict graph witness replays overflow");
        c.Equal("n.overflow", strictProof.WitnessReplay?.Error?.EntityId, "strict graph witness exact origin");
        c.Equal("ArithmeticOverflow", ReferenceInterpreter.Evaluate(checkedStrict, new(long.MaxValue, 1)).Error?.Code, "explicit Max vector independent of solver witness");
        c.Evidence["strictReserveOverflow"] = strictProof;
        var arithmetic = await verifier.VerifyArithmeticProbeAsync();
        c.Equal(VerificationStatus.Counterexample, arithmetic.Status, "x+1 strict-greater probe cannot verify over all positive I64");
        c.Equal(long.MaxValue, arithmetic.Witness, "arithmetic regression witness is exactly MaxInt64");
        c.Equal("ArithmeticOverflow", ReferenceInterpreter.Evaluate(GraphValidator.Validate(Fixtures.Arithmetic("i64.add_checked", long.MaxValue, 1)), new(0, 1)).Error?.Code,
            "MaxInt64 witness reproduces in checked reference");
        var impossible = await verifier.VerifyArithmeticProbeAsync(impossibleDomain: true);
        c.True(impossible.Status != VerificationStatus.Verified && impossible.Error is not null, "real SMT impossible domain refused");
        c.Evidence["arithmeticProbe"] = arithmetic;
        c.Evidence["impossibleDomain"] = impossible;
    }

    private static async Task FailedSolvers(TestContext c)
    {
        var p = GraphValidator.Validate(Fixtures.Reserve());
        var verificationContext = new VerificationContext(new string('b', 64), new string('c', 64));
        (string Name, string Code, SolverResponse Response)[] cases = [
            ("unknown", "SolverUnknown", new(0, "unknown\n", "")), ("missing-status", "SolverMalformedResponse", new(0, "", "")),
            ("crash", "SolverFailure", new(9, "unsat\n", "crash")), ("timeout", "SolverTimeout", new(0, "unsat\n", "", true)),
            ("multiple-statuses", "SolverMalformedResponse", new(0, "unsat\nsat\n", "")), ("solver-error", "SolverMalformedResponse", new(0, "(error \"bad query\")\nunsat\n", ""))
        ];
        var errors = new Dictionary<string, string?>();
        foreach (var (name, code, response) in cases)
        {
            int call = 0;
            var solver = new StubSolver(_ => ++call == 1 ? new(0, "sat\n", "") : response);
            var result = await new AdmissionVerifier(solver).VerifyAsync(p, verificationContext);
            c.True(!result.IsVerified, name + " cannot grant admission");
            c.True(result.Error is not null, name + " has structured error");
            c.Equal(code, result.Error?.Code, name + " exact failure classification");
            errors[name] = result.Error?.Code;
        }
        var emptyDomain = await new AdmissionVerifier(new StubSolver(_ => new(0, "unsat\n", ""))).VerifyAsync(p, verificationContext);
        c.True(!emptyDomain.IsVerified, "empty domain cannot vacuously verify");
        c.True(emptyDomain.Error is not null, "empty domain classified");
        c.Equal("DomainUnsatisfiable", emptyDomain.Error?.Code, "empty domain exact classification");
        int witnessCalls = 0;
        var falseWitness = new StubSolver(_ => ++witnessCalls < 3 ? new(0, "sat\n", "") : new(0, "sat\n((available 10) (quantity 3))\n", ""));
        var mismatch = await new AdmissionVerifier(falseWitness).VerifyAsync(p, verificationContext);
        c.True(!mismatch.IsVerified, "nonreproducing solver witness cannot grant admission");
        c.Equal("VerifierMismatch", mismatch.Error?.Code, "false witness classified as verifier mismatch");
        c.Evidence["rejections"] = errors;
    }

    private static void GeneratedDags(TestContext c)
    {
        const int seed = 20260904;
        var random = new Random(seed);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        ReserveInput[] inputs = [new(0, 1), new(10, 3), new(long.MaxValue, 1), new(long.MinValue, -1), new(long.MaxValue, long.MinValue), new(1, 1)];
        for (int index = 0; index < 200; index++)
        {
            var graph = GeneratedGraph(random);
            var validated = GraphValidator.Validate(graph);
            foreach (var n in graph.Nodes) seen.Add(n.Op);
            foreach (var input in inputs)
            {
                var actual = Compare(c, validated, input, 128);
                var expected = MathematicalOracle(graph, input);
                c.Equal(expected.ErrorNode is null, actual.Success, $"generated {index} mathematical definedness");
                if (expected.ErrorNode is null) c.Equal(expected.Output, actual.Output, $"generated {index} mathematical output");
                else
                {
                    c.Equal("ArithmeticOverflow", actual.Error?.Code, "generated overflow classification");
                    c.Equal(expected.ErrorNode, actual.Error?.EntityId, "generated first mathematical overflow");
                }
                c.Equal(expected.UsedFuel, actual.UsedFuel, "generated independent fuel oracle");
            }
        }
        foreach (string op in new[] { "input", "i64.const", "bool.const", "i64.add_checked", "i64.sub_checked", "i64.le", "i64.eq", "bool.not", "bool.and", "bool.or", "select" })
            c.True(seen.Contains(op), "generator covers " + op);
        c.Evidence["seed"] = seed;
        c.Evidence["graphs"] = 200;
        c.Evidence["inputsPerGraph"] = inputs.Length;
        c.Evidence["opcodes"] = seen.Order(StringComparer.Ordinal).ToArray();
    }

    private static KernelProgram GeneratedGraph(Random random)
    {
        long[] constants = [long.MinValue, -1, 0, 1, 2, long.MaxValue];
        var nodes = new List<KernelNode> { Fixtures.Input("g00", "state.available"), Fixtures.Input("g01", "event.quantity"), Fixtures.Bool("g02", random.Next(2) == 0) };
        for (int i = 3; i < 16; i++)
        {
            string id = $"g{i:00}";
            string Num() => nodes.Where(n => n.Type == KernelType.I64).ElementAt(random.Next(nodes.Count(n => n.Type == KernelType.I64))).Id;
            string Bool() => nodes.Where(n => n.Type == KernelType.Bool).ElementAt(random.Next(nodes.Count(n => n.Type == KernelType.Bool))).Id;
            nodes.Add(random.Next(10) switch
            {
                0 => Fixtures.I64(id, constants[random.Next(constants.Length)]),
                1 => Fixtures.Op(id, "i64.add_checked", KernelType.I64, Num(), Num()),
                2 => Fixtures.Op(id, "i64.sub_checked", KernelType.I64, Num(), Num()),
                3 => Fixtures.Op(id, "i64.le", KernelType.Bool, Num(), Num()),
                4 => Fixtures.Op(id, "i64.eq", KernelType.Bool, Num(), Num()),
                5 => Fixtures.Op(id, "bool.not", KernelType.Bool, Bool()),
                6 => Fixtures.Op(id, "bool.and", KernelType.Bool, Bool(), Bool()),
                7 => Fixtures.Op(id, "bool.or", KernelType.Bool, Bool(), Bool()),
                8 => Fixtures.Op(id, "select", KernelType.I64, Bool(), Num(), Num()),
                _ => Fixtures.Op(id, "select", KernelType.Bool, Bool(), Bool(), Bool())
            });
        }
        var outputs = new OutputRefs(nodes.Last(n => n.Type == KernelType.Bool).Id, nodes.Last(n => n.Type == KernelType.I64).Id, nodes[0].Id);
        var byId = nodes.ToDictionary(n => n.Id);
        var reachable = new HashSet<string>();
        void Visit(string id) { if (reachable.Add(id)) foreach (var arg in byId[id].Args) Visit(arg); }
        Visit(outputs.Accepted); Visit(outputs.Available); Visit(outputs.Reserved);
        return Fixtures.Graph(nodes.Where(n => reachable.Contains(n.Id)), outputs);
    }

    private static (ReserveOutput? Output, string? ErrorNode, int UsedFuel) MathematicalOracle(KernelProgram graph, ReserveInput input)
    {
        var byId = graph.Nodes.ToDictionary(n => n.Id);
        var memo = new Dictionary<string, object>();
        object Value(string id)
        {
            if (memo.TryGetValue(id, out var cached)) return cached;
            var n = byId[id];
            object A(int i) => Value(n.Args[i]);
            BigInteger N(int i) => (BigInteger)A(i);
            bool B(int i) => (bool)A(i);
            object value = n.Op switch
            {
                "input" => new BigInteger(n.FieldId == "state.available" ? input.Available : input.Quantity),
                "i64.const" => new BigInteger(n.I64Value!.Value), "bool.const" => n.BoolValue!.Value,
                "i64.add_checked" => N(0) + N(1), "i64.sub_checked" => N(0) - N(1),
                "i64.le" => N(0) <= N(1), "i64.eq" => N(0) == N(1),
                "bool.not" => !B(0), "bool.and" => B(0) & B(1), "bool.or" => B(0) | B(1),
                "select" => B(0) ? A(1) : A(2), _ => throw new InvalidOperationException(n.Op)
            };
            memo[id] = value;
            return value;
        }
        // Compute the total mathematical DAG, then independently find the first out-of-range node.
        foreach (var node in graph.Nodes) Value(node.Id);
        var done = new HashSet<string>();
        int used = 0;
        while (done.Count != graph.Nodes.Length)
        {
            var next = graph.Nodes.Where(n => !done.Contains(n.Id) && n.Args.All(done.Contains)).OrderBy(n => n.Id, StringComparer.Ordinal).First();
            used++;
            if (memo[next.Id] is BigInteger number && (number < long.MinValue || number > long.MaxValue))
                return (null, next.Id, used);
            done.Add(next.Id);
        }
        return (new ReserveOutput((bool)memo[graph.Outputs.Accepted], (long)(BigInteger)memo[graph.Outputs.Available], (long)(BigInteger)memo[graph.Outputs.Reserved]), null, used);
    }
}
