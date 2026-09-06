using System.Collections.Immutable;
using System.Numerics;

namespace Kernel.Core;

public static class ReserveContract
{
    public static KernelError? CheckInput(ReserveInput input)
    {
        if (input.Available < 0) return KernelError.Create("precondition", "StateInvariantFailed", "state.available");
        if (input.Quantity <= 0) return KernelError.Create("precondition", "PreconditionFailed", "event.quantity");
        return null;
    }
    public static KernelError? CheckOutput(ReserveInput input, ReserveOutput output)
    {
        BigInteger available = input.Available, quantity = input.Quantity;
        BigInteger remaining = output.Available, reserved = output.Reserved;
        bool enough = quantity <= available;
        bool valid = available >= 0 && quantity > 0 && remaining >= 0 && reserved >= 0 && remaining + reserved == available &&
            output.Accepted == enough && (enough ? reserved == quantity && remaining == available - quantity : reserved == 0 && remaining == available);
        return valid ? null : KernelError.Create("postcondition", "PostconditionFailed", details: new { input, output }, witness: input);
    }
}

/// <summary>Independent mathematical arithmetic implementation; checked conversion occurs only after explicit I64 range checks.</summary>
public static class ReferenceInterpreter
{
    public static EvaluationResult Evaluate(ValidatedProgram program, ReserveInput input, int fuel = 128)
    {
        if (fuel < 0 || fuel > 128) throw GraphValidator.Error("InvalidLimits");
        var values = new Dictionary<string, KernelValue>(StringComparer.Ordinal);
        var trace = ImmutableArray.CreateBuilder<TraceEntry>();
        int used = 0;
        foreach (var node in program.TopologicalNodes)
        {
            if (used == fuel) return new(null, trace.ToImmutable(), used, KernelError.Create("execution", "BudgetExceeded", node.Id, programRevision: program.Revision));
            used++;
            ImmutableArray<KernelValue> operands = [.. node.Args.Select(a => values[a])];
            KernelValue value;
            if (node.Op is "i64.add_checked" or "i64.sub_checked")
            {
                BigInteger left = operands[0].Number, right = operands[1].Number;
                BigInteger result = node.Op == "i64.add_checked" ? left + right : left - right;
                if (result < long.MinValue || result > long.MaxValue)
                {
                    trace.Add(new(node.Id, node.Op, operands, null, "ArithmeticOverflow"));
                    return new(null, trace.ToImmutable(), used, KernelError.Create("execution", "ArithmeticOverflow", node.Id, programRevision: program.Revision, witness: input));
                }
                value = KernelValue.I64((long)result);
            }
            else
            {
                value = node.Op switch
                {
                    "input" => KernelValue.I64(node.FieldId == "state.available" ? input.Available : input.Quantity),
                    "i64.const" => KernelValue.I64(node.I64Value!.Value),
                    "bool.const" => KernelValue.Bool(node.BoolValue!.Value),
                    "i64.le" => KernelValue.Bool(new BigInteger(operands[0].Number) <= new BigInteger(operands[1].Number)),
                    "i64.eq" => KernelValue.Bool(new BigInteger(operands[0].Number) == new BigInteger(operands[1].Number)),
                    "bool.not" => KernelValue.Bool(!operands[0].Boolean),
                    "bool.and" => KernelValue.Bool(operands[0].Boolean & operands[1].Boolean),
                    "bool.or" => KernelValue.Bool(operands[0].Boolean | operands[1].Boolean),
                    "select" => operands[0].Boolean ? operands[1] : operands[2],
                    _ => throw GraphValidator.Error("UnsupportedOpcode", node.Id)
                };
            }
            values.Add(node.Id, value); trace.Add(new(node.Id, node.Op, operands, value));
        }
        var outputs = program.Program.Outputs;
        return new(new(values[outputs.Accepted].Boolean, values[outputs.Available].Number, values[outputs.Reserved].Number), trace.ToImmutable(), used, null);
    }
}

/// <summary>Separate machine-I64 execution backend. Arithmetic is checked in this method, independent of project flags.</summary>
public static class IrInterpreter
{
    public static EvaluationResult Evaluate(IrProgram ir, ReserveInput input, int fuel = 128)
    {
        IrValidator.Validate(ir);
        if (fuel < 0 || fuel > 128) throw GraphValidator.Error("InvalidLimits");
        var values = new KernelValue[ir.Instructions.Length];
        var trace = ImmutableArray.CreateBuilder<TraceEntry>();
        int used = 0;
        foreach (var instruction in ir.Instructions)
        {
            if (used == fuel) return new(null, trace.ToImmutable(), used, KernelError.Create("execution", "BudgetExceeded", instruction.OriginNodeId, programRevision: ir.ProgramRevision));
            used++;
            ImmutableArray<KernelValue> operands = [.. instruction.OperandIndices.Select(i => values[i])];
            KernelValue value;
            try
            {
                value = instruction.Opcode switch
                {
                    "input" => KernelValue.I64(instruction.FieldId == "state.available" ? input.Available : input.Quantity),
                    "i64.const" => KernelValue.I64(instruction.I64Value!.Value),
                    "bool.const" => KernelValue.Bool(instruction.BoolValue!.Value),
                    "i64.add_checked" => KernelValue.I64(checked(operands[0].Number + operands[1].Number)),
                    "i64.sub_checked" => KernelValue.I64(checked(operands[0].Number - operands[1].Number)),
                    "i64.le" => KernelValue.Bool(operands[0].Number <= operands[1].Number),
                    "i64.eq" => KernelValue.Bool(operands[0].Number == operands[1].Number),
                    "bool.not" => KernelValue.Bool(!operands[0].Boolean),
                    "bool.and" => KernelValue.Bool(operands[0].Boolean & operands[1].Boolean),
                    "bool.or" => KernelValue.Bool(operands[0].Boolean | operands[1].Boolean),
                    "select" => operands[0].Boolean ? operands[1] : operands[2],
                    _ => throw GraphValidator.Error("UnsupportedOpcode", instruction.OriginNodeId)
                };
            }
            catch (OverflowException)
            {
                trace.Add(new(instruction.OriginNodeId, instruction.Opcode, operands, null, "ArithmeticOverflow"));
                return new(null, trace.ToImmutable(), used, KernelError.Create("execution", "ArithmeticOverflow", instruction.OriginNodeId, programRevision: ir.ProgramRevision, witness: input));
            }
            values[instruction.DestinationIndex] = value;
            trace.Add(new(instruction.OriginNodeId, instruction.Opcode, operands, value));
        }
        return new(new(values[ir.Outputs.Accepted].Boolean, values[ir.Outputs.Available].Number, values[ir.Outputs.Reserved].Number), trace.ToImmutable(), used, null);
    }
}
