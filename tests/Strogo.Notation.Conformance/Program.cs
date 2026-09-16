using System.Text;
using Kernel.Core;
using Strogo.Notation;

const string baseline = """
{
  long available = input.resourceAvailable;
  long quantity = input.requestedQuantity;
  bool enough = quantity <= available;
  long zero = 0L;
  long debit = Select(enough, quantity, zero);
  long remaining = checked(available - debit);
  return (accepted: enough, available: remaining, reserved: debit);
}
""";

const string expectedGraph = """
{"schemaVersion":"kernel.v0","programId":"reserve","profileId":"reserve.v0","nodes":[{"id":"n.available","op":"input","type":"I64","args":[],"fieldId":"state.available"},{"id":"n.quantity","op":"input","type":"I64","args":[],"fieldId":"event.quantity"},{"id":"n.zero","op":"i64.const","type":"I64","args":[],"value":"0"},{"id":"n.enough","op":"i64.le","type":"Bool","args":["n.quantity","n.available"]},{"id":"n.debit","op":"select","type":"I64","args":["n.enough","n.quantity","n.zero"]},{"id":"n.remaining","op":"i64.sub_checked","type":"I64","args":["n.available","n.debit"]}],"outputs":{"accepted":"n.enough","available":"n.remaining","reserved":"n.debit"}}
""";

var positives = new[]
{
    baseline,
    baseline.Replace("accepted: enough", "accepted: true_value", StringComparison.Ordinal).Replace("  bool enough = quantity <= available;", "  bool enough = quantity <= available;\n  bool true_value = true;", StringComparison.Ordinal),
    baseline.Replace("accepted: enough", "accepted: false_value", StringComparison.Ordinal).Replace("  bool enough = quantity <= available;", "  bool enough = quantity <= available;\n  bool false_value = false;", StringComparison.Ordinal),
    baseline.Replace("  return (accepted: enough", "  bool accepted_value = !enough;\n  return (accepted: accepted_value", StringComparison.Ordinal),
    baseline.Replace("  return (accepted: enough", "  bool accepted_value = enough & enough;\n  return (accepted: accepted_value", StringComparison.Ordinal),
    baseline.Replace("  return (accepted: enough", "  bool accepted_value = enough | enough;\n  return (accepted: accepted_value", StringComparison.Ordinal),
    baseline.Replace("  return (accepted: enough", "  bool accepted_value = quantity == available;\n  return (accepted: accepted_value", StringComparison.Ordinal),
    baseline.Replace("  return (accepted: enough", "  bool true_value = true;\n  bool false_value = false;\n  bool accepted_value = Select(enough, true_value, false_value);\n  return (accepted: accepted_value", StringComparison.Ordinal),
    baseline.Replace("checked(available - debit)", "checked(available + debit)", StringComparison.Ordinal),
    baseline.Replace("  long remaining = checked(available - debit);", "  long offset = -1L;\n  long remaining = checked(available - offset);", StringComparison.Ordinal),
    baseline.Replace("  long remaining = checked(available - debit);", "  long offset = 9223372036854775807L;\n  long remaining = checked(available - offset);", StringComparison.Ordinal),
    baseline.Replace("  long remaining = checked(available - debit);", "  long one = 1L;\n  long remaining = checked(available - one);", StringComparison.Ordinal)
};

var negatives = new (byte[] Bytes, string Code)[]
{
    (Encoding.UTF8.GetBytes(baseline.Replace("{", "{// comment\n", StringComparison.Ordinal)), "UnsupportedSyntax"),
    (Encoding.UTF8.GetBytes("#define X\n" + baseline), "UnsupportedSyntax"),
    (Encoding.UTF8.GetBytes(baseline.Replace("0L", "\"0\"", StringComparison.Ordinal)), "UnsupportedSyntax"),
    (Encoding.UTF8.GetBytes(baseline.Replace("0L", "$\"0\"", StringComparison.Ordinal)), "UnsupportedSyntax"),
    (Encoding.UTF8.GetBytes(baseline.Replace("long available", "long ävailable", StringComparison.Ordinal)), "UnsupportedSyntax"),
    (new byte[] { 0xFF, 0xFE, 0xFD }, "SourceEncodingInvalid"),
    (new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(baseline)).ToArray(), "SourceEncodingInvalid"),
    (Encoding.UTF8.GetBytes("{" + new string(' ', 65536) + "}"), "SourceTooLarge"),
    (Encoding.UTF8.GetBytes(baseline.Replace("long zero", "long available", StringComparison.Ordinal)), "DuplicateLocal"),
    (Encoding.UTF8.GetBytes(baseline.Replace("bool enough = quantity <= available;", "long alias = enough;\n  bool enough = quantity <= available;", StringComparison.Ordinal)), "ForwardReference"),
    (Encoding.UTF8.GetBytes(baseline.Replace("bool enough", "long enough", StringComparison.Ordinal)), "TypeMismatch"),
    (Encoding.UTF8.GetBytes(baseline.Replace("bool enough = quantity <= available;", "long accepted_value = true;\n  bool enough = quantity <= available;", StringComparison.Ordinal).Replace("accepted: enough", "accepted: accepted_value", StringComparison.Ordinal)), "TypeMismatch"),
    (Encoding.UTF8.GetBytes(baseline.Replace("checked(available - debit)", "checked(available - Select(enough, quantity, zero))", StringComparison.Ordinal)), "GrammarInvalid"),
    (Encoding.UTF8.GetBytes(baseline.Replace("Select(enough, quantity, zero)", "Select(enough && enough, quantity, zero)", StringComparison.Ordinal)), "UnsupportedSyntax"),
    (Encoding.UTF8.GetBytes(baseline.Replace("return (accepted: enough, available: remaining, reserved: debit);", "return (accepted: enough, available: remaining, wrong: debit);", StringComparison.Ordinal)), "OutputInvalid"),
    (Encoding.UTF8.GetBytes(baseline.Replace("accepted: enough", "accepted: debit", StringComparison.Ordinal)), "TypeMismatch"),
    (Encoding.UTF8.GetBytes(baseline.Replace("long zero = 0L;\n", String.Empty, StringComparison.Ordinal)), "GrammarInvalid"),
    (Encoding.UTF8.GetBytes(baseline.Replace("input.resourceAvailable", "input.other", StringComparison.Ordinal)), "UnsupportedSyntax"),
    (Encoding.UTF8.GetBytes(baseline.Replace("quantity <= available", "!!enough", StringComparison.Ordinal)), "UnaryChainExceeded"),
    (Encoding.UTF8.GetBytes(baseline + " trailing"), "GrammarInvalid"),
    (Encoding.UTF8.GetBytes("{" + new string('(', 33) + baseline[1..]), "DelimiterDepthExceeded"),
    (Encoding.UTF8.GetBytes("{" + string.Join(';', Enumerable.Repeat("long x = 1L", 33)) + ";}"), "SyntaxInvalid"),
    (Encoding.UTF8.GetBytes("{" + string.Join(' ', Enumerable.Repeat("long", 4100)) + "}"), "TokenLimitExceeded")
};

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}

var baselineResult = NotationCompiler.Compile(Encoding.UTF8.GetBytes(baseline));
Check(baselineResult.Accepted, "baseline accepted");
var expected = ProgramCodec.Parse(expectedGraph);
Check(baselineResult.ProgramRevision == ProgramCodec.Revision(expected), "baseline exact graph revision");

for (int positiveIndex = 0; positiveIndex < positives.Length; positiveIndex++)
{
    string source = positives[positiveIndex];
    var bytes = Encoding.UTF8.GetBytes(source);
    var first = NotationCompiler.Compile(bytes);
    var second = NotationCompiler.Compile(bytes);
    if (!first.Accepted) Console.WriteLine($"positive[{positiveIndex}] refusal code={first.ErrorCode} locus={first.Locus}\n{source}");
    Check(first.Accepted, $"positive[{positiveIndex}] accepted");
    Check(first.ProgramRevision == second.ProgramRevision, "positive revision deterministic");
    Check(first.ReportBytes().SequenceEqual(second.ReportBytes()), "positive report deterministic");
    Check(first.Program is not null, "positive graph emitted");
    GraphValidator.Validate(first.Program!);
}

for (int negativeIndex = 0; negativeIndex < negatives.Length; negativeIndex++)
{
    var (bytes, expectedCode) = negatives[negativeIndex];
    var result = NotationCompiler.Compile(bytes);
    if (result.ErrorCode != expectedCode) Console.WriteLine($"negative[{negativeIndex}] expected={expectedCode} actual={result.ErrorCode} status={result.Status} locus={result.Locus}");
    Check(!result.Accepted, $"negative[{negativeIndex}] refused: {expectedCode}");
    Check(result.ErrorCode == expectedCode, $"negative[{negativeIndex}] code expected {expectedCode}, actual {result.ErrorCode}");
    Check(result.Program is null && result.ProgramRevision is null, "negative has no graph/revision");
    Check(!string.IsNullOrWhiteSpace(result.Locus), "negative has locus");
}

Console.WriteLine($"E08 notation conformance PASS: positives={positives.Length}, negatives={negatives.Length}, checks={checks}");
