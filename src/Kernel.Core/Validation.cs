using System.Collections.Immutable;

namespace Kernel.Core;

public static class GraphValidator
{
    public static ImmutableHashSet<string> Opcodes { get; } = ImmutableHashSet.Create(StringComparer.Ordinal,
        "input", "i64.const", "bool.const", "i64.add_checked", "i64.sub_checked", "i64.le", "i64.eq", "bool.not", "bool.and", "bool.or", "select");

    public static ValidatedProgram Validate(KernelProgram program, CoreLimits? limits = null, bool enforceFuel = true)
    {
        limits ??= new();
        ValidateLimits(limits);
        if (program.SchemaVersion != KernelVersions.Schema || program.ProgramId != "reserve" || program.ProfileId != "reserve.v0")
            throw Error("ProfileMismatch");
        if (program.Nodes.IsDefault || program.Nodes.Length == 0 || program.Nodes.Length > limits.MaxNodes)
            throw Error("NodeLimitExceeded");
        if (enforceFuel && program.Nodes.Length > limits.Fuel) throw Error("BudgetExceeded", details: new { requiredFuel = program.Nodes.Length, limits.Fuel });
        var nodes = new Dictionary<string, KernelNode>(StringComparer.Ordinal);
        foreach (var node in program.Nodes)
        {
            Wire.RequireId(node.Id);
            if (!nodes.TryAdd(node.Id, node)) throw Error("DuplicateId", node.Id);
            ValidateShape(node);
        }
        foreach (var node in program.Nodes)
        {
            foreach (var arg in node.Args)
                if (!nodes.ContainsKey(arg)) throw Error("DanglingReference", node.Id, new { reference = arg });
            ValidateTypes(node, node.Args.Select(a => nodes[a].Type).ToArray());
        }
        var outputIds = new[] { program.Outputs.Accepted, program.Outputs.Available, program.Outputs.Reserved };
        foreach (var id in outputIds)
            if (!nodes.ContainsKey(id)) throw Error("DanglingReference", id);
        if (nodes[outputIds[0]].Type != KernelType.Bool || nodes[outputIds[1]].Type != KernelType.I64 || nodes[outputIds[2]].Type != KernelType.I64)
            throw Error("TypeMismatch", details: new { reason = "OutputTypes" });

        // Duplicate operands are semantic operands, but count as one dependency edge.
        var dependencies = nodes.ToDictionary(p => p.Key, p => p.Value.Args.Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);
        var users = nodes.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var n in program.Nodes)
            foreach (var dep in n.Args.Distinct(StringComparer.Ordinal)) users[dep].Add(n.Id);
        var ready = new SortedSet<string>(dependencies.Where(p => p.Value == 0).Select(p => p.Key), StringComparer.Ordinal);
        var ordered = ImmutableArray.CreateBuilder<KernelNode>(nodes.Count);
        while (ready.Count > 0)
        {
            string id = ready.Min!; ready.Remove(id); ordered.Add(nodes[id]);
            foreach (var user in users[id]) if (--dependencies[user] == 0) ready.Add(user);
        }
        if (ordered.Count != nodes.Count) throw Error("CycleDetected");
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(outputIds);
        while (pending.TryPop(out var id))
            if (reachable.Add(id)) foreach (var arg in nodes[id].Args) pending.Push(arg);
        if (reachable.Count != nodes.Count)
            throw Error("UnreachableNode", nodes.Keys.Where(id => !reachable.Contains(id)).Order(StringComparer.Ordinal).First());
        return new(program, ordered.MoveToImmutable());
    }

    internal static void ValidateLimits(CoreLimits limits)
    {
        if (limits.MaxTransportBytes is < 1 or > 65536 || limits.MaxJsonDepth is < 1 or > 32 || limits.MaxNodes is < 1 or > 128 ||
            limits.Fuel is < 0 or > 128 || limits.SolverTimeoutMilliseconds is < 1 or > 5000 || limits.AdmissionTimeoutMilliseconds is < 1 or > 15000)
            throw Error("InvalidLimits");
    }
    internal static void ValidateShape(KernelNode node)
    {
        if (!Opcodes.Contains(node.Op)) throw Error("UnsupportedOpcode", node.Id);
        if (node.Type is not (KernelType.I64 or KernelType.Bool) || node.Args.IsDefault) throw Error("TypeMismatch", node.Id);
        foreach (var arg in node.Args) Wire.RequireId(arg);
        int arity = node.Op switch { "input" or "i64.const" or "bool.const" => 0, "bool.not" => 1, "select" => 3, _ => 2 };
        if (node.Args.Length != arity) throw Error("ArityMismatch", node.Id);
        if ((node.Op == "input" ? node.FieldId is not ("state.available" or "event.quantity") : node.FieldId is not null) ||
            (node.Op == "i64.const" ? node.I64Value is null : node.I64Value is not null) ||
            (node.Op == "bool.const" ? node.BoolValue is null : node.BoolValue is not null))
            throw Error("SchemaInvalid", node.Id, new { reason = "InvalidFieldOrLiteral" });
    }
    internal static void ValidateTypes(KernelNode node, IReadOnlyList<KernelType> operands)
    {
        bool valid = node.Op switch
        {
            "input" or "i64.const" => node.Type == KernelType.I64,
            "bool.const" => node.Type == KernelType.Bool,
            "i64.add_checked" or "i64.sub_checked" => node.Type == KernelType.I64 && operands.All(t => t == KernelType.I64),
            "i64.le" or "i64.eq" => node.Type == KernelType.Bool && operands.All(t => t == KernelType.I64),
            "bool.not" or "bool.and" or "bool.or" => node.Type == KernelType.Bool && operands.All(t => t == KernelType.Bool),
            "select" => operands[0] == KernelType.Bool && operands[1] == node.Type && operands[2] == node.Type,
            _ => false
        };
        if (!valid) throw Error("TypeMismatch", node.Id);
    }
    internal static KernelException Error(string code, string? id = null, object? details = null) => new(KernelError.Create("validation", code, id, details));
}

public static class Lowerer
{
    public static IrProgram Lower(ValidatedProgram program)
    {
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = ImmutableArray.CreateBuilder<IrInstruction>(program.TopologicalNodes.Length);
        foreach (var node in program.TopologicalNodes)
        {
            int destination = result.Count;
            result.Add(new(destination, node.Id, node.Op, [.. node.Args.Select(a => indices[a])], node.Type, node.FieldId, node.I64Value, node.BoolValue));
            indices.Add(node.Id, destination);
        }
        var outputs = program.Program.Outputs;
        var ir = new IrProgram(KernelVersions.Semantics, program.Revision, result.MoveToImmutable(), new(indices[outputs.Accepted], indices[outputs.Available], indices[outputs.Reserved]));
        IrValidator.Validate(ir); return ir;
    }
}

public static class IrValidator
{
    public static void Validate(IrProgram ir)
    {
        if (ir.SemanticsVersion != KernelVersions.Semantics) throw GraphValidator.Error("SemanticsMismatch");
        Wire.RequireDigest(ir.ProgramRevision);
        if (ir.Instructions.IsDefault || ir.Instructions.Length is < 1 or > 128) throw GraphValidator.Error("InvalidIr");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < ir.Instructions.Length; index++)
        {
            var instruction = ir.Instructions[index];
            Wire.RequireId(instruction.OriginNodeId);
            if (instruction.DestinationIndex != index || !ids.Add(instruction.OriginNodeId) || instruction.OperandIndices.IsDefault ||
                instruction.OperandIndices.Any(i => i < 0 || i >= index)) throw GraphValidator.Error("InvalidIr", instruction.OriginNodeId);
            var node = new KernelNode(instruction.OriginNodeId, instruction.Opcode, instruction.Type,
                [.. instruction.OperandIndices.Select(i => ir.Instructions[i].OriginNodeId)], instruction.FieldId, instruction.I64Value, instruction.BoolValue);
            GraphValidator.ValidateShape(node);
            GraphValidator.ValidateTypes(node, instruction.OperandIndices.Select(i => ir.Instructions[i].Type).ToArray());
        }
        var outputs = new[] { ir.Outputs.Accepted, ir.Outputs.Available, ir.Outputs.Reserved };
        if (outputs.Any(i => i < 0 || i >= ir.Instructions.Length)) throw GraphValidator.Error("InvalidIr");
        if (ir.Instructions[outputs[0]].Type != KernelType.Bool || ir.Instructions[outputs[1]].Type != KernelType.I64 || ir.Instructions[outputs[2]].Type != KernelType.I64)
            throw GraphValidator.Error("TypeMismatch");
        var reached = new HashSet<int>(); var pending = new Stack<int>(outputs);
        while (pending.TryPop(out int index)) if (reached.Add(index)) foreach (int operand in ir.Instructions[index].OperandIndices) pending.Push(operand);
        if (reached.Count != ir.Instructions.Length) throw GraphValidator.Error("UnreachableNode");
        // A valid persisted IR must retain the same canonical ready-node order as lowering.
        var remaining = new HashSet<int>(Enumerable.Range(0, ir.Instructions.Length));
        for (int i = 0; i < ir.Instructions.Length; i++)
        {
            int next = remaining.Where(n => ir.Instructions[n].OperandIndices.All(a => !remaining.Contains(a)))
                .OrderBy(n => ir.Instructions[n].OriginNodeId, StringComparer.Ordinal).First();
            if (next != i) throw GraphValidator.Error("InvalidIr", ir.Instructions[i].OriginNodeId, new { reason = "NonCanonicalOrder" });
            remaining.Remove(next);
        }
    }
}
