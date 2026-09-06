using System.Globalization;
using System.Text;

namespace Kernel.Core;

public sealed record SmtObligations(string Domain, string Counterexample, string Witness);

public static class SmtEncoder
{
    private const string Min = "(- 9223372036854775808)";
    private const string Max = "9223372036854775807";
    internal static string Range(string expression) => $"(and (<= {Min} {expression}) (<= {expression} {Max}))";
    private static string Integer(long value) => value < 0 ? $"(- {value.ToString(CultureInfo.InvariantCulture)[1..]})" : value.ToString(CultureInfo.InvariantCulture);
    private static string And(IEnumerable<string> expressions)
    { var e = expressions.ToArray(); return e.Length switch { 0 => "true", 1 => e[0], _ => $"(and {string.Join(' ', e)})" }; }

    public static SmtObligations Encode(ValidatedProgram program)
    {
        var declarations = new StringBuilder("(set-logic QF_LIA)\n(set-option :produce-models true)\n(declare-const available Int)\n(declare-const quantity Int)\n");
        string domain = $"(and {Range("available")} {Range("quantity")} (<= 0 available) (< 0 quantity))";
        declarations.Append("(define-fun domain () Bool ").Append(domain).Append(")\n");
        var indices = program.TopologicalNodes.Select((node, index) => (node.Id, index)).ToDictionary(p => p.Id, p => p.index, StringComparer.Ordinal);
        for (int i = 0; i < program.TopologicalNodes.Length; i++)
        {
            var node = program.TopologicalNodes[i];
            string[] args = node.Args.Select(id => $"v{indices[id]}").ToArray();
            string expression = node.Op switch
            {
                "input" => node.FieldId == "state.available" ? "available" : "quantity",
                "i64.const" => Integer(node.I64Value!.Value),
                "bool.const" => node.BoolValue!.Value ? "true" : "false",
                "i64.add_checked" => $"(+ {args[0]} {args[1]})",
                "i64.sub_checked" => $"(- {args[0]} {args[1]})",
                "i64.le" => $"(<= {args[0]} {args[1]})",
                "i64.eq" => $"(= {args[0]} {args[1]})",
                "bool.not" => $"(not {args[0]})",
                "bool.and" => $"(and {args[0]} {args[1]})",
                "bool.or" => $"(or {args[0]} {args[1]})",
                "select" => $"(ite {args[0]} {args[1]} {args[2]})",
                _ => throw GraphValidator.Error("UnsupportedOpcode", node.Id)
            };
            declarations.Append($"(define-fun v{i} () {(node.Type == KernelType.I64 ? "Int" : "Bool")} {expression})\n");
            var conditions = node.Args.Select(id => $"d{indices[id]}").ToList();
            // A range check defines whether an arithmetic node succeeds; it is NEVER asserted as an input assumption.
            if (node.Op is "i64.add_checked" or "i64.sub_checked") conditions.Add(Range($"v{i}"));
            declarations.Append($"(define-fun d{i} () Bool {And(conditions)})\n");
        }
        var output = program.Program.Outputs;
        string accepted = $"v{indices[output.Accepted]}", remaining = $"v{indices[output.Available]}", reserved = $"v{indices[output.Reserved]}";
        string post = $"(and (<= 0 {remaining}) (<= 0 {reserved}) (= (+ {remaining} {reserved}) available) (= {accepted} (<= quantity available)) (ite (<= quantity available) (and (= {reserved} quantity) (= {remaining} (- available quantity))) (and (= {reserved} 0) (= {remaining} available))))";
        string allDefined = And(Enumerable.Range(0, program.TopologicalNodes.Length).Select(i => $"d{i}"));
        declarations.Append($"(define-fun failure () Bool (or (not {allDefined}) (not {post})))\n");
        return Queries(declarations.ToString(), "available quantity");
    }

    /// <summary>Fixed trusted regression fixture: x&gt;0, result=x+1, result&gt;x, with checked I64 definedness.</summary>
    public static SmtObligations EncodeArithmeticProbe(bool impossibleDomain = false)
    {
        string d = $"(and {Range("x")} (< 0 x){(impossibleDomain ? " (< x 0)" : "")})";
        string text = $"(set-logic QF_LIA)\n(set-option :produce-models true)\n(declare-const x Int)\n(define-fun domain () Bool {d})\n(define-fun result () Int (+ x 1))\n(define-fun failure () Bool (or (not {Range("result")}) (not (> result x))))\n";
        return Queries(text, "x");
    }
    private static SmtObligations Queries(string declarations, string inputNames) => new(
        "; obligation:domain\n" + declarations + "(assert domain)\n(check-sat)\n(exit)\n",
        "; obligation:counterexample\n" + declarations + "(assert domain)\n(assert failure)\n(check-sat)\n(exit)\n",
        "; obligation:witness\n" + declarations + $"(assert domain)\n(assert failure)\n(check-sat)\n(get-value ({inputNames}))\n(exit)\n");
}
