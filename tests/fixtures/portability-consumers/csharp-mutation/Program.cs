using Strogo.Portable.V01;

const string SummarizeRequest = "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"summarize\",\"arguments\":[{\"kind\":\"sequence\",\"items\":[{\"kind\":\"i64\",\"value\":\"4\"},{\"kind\":\"i64\",\"value\":\"-3\"},{\"kind\":\"i64\",\"value\":\"1\"}]}]}";
const string SummarizeExpected = "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",\"value\":{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[{\"kind\":\"i64\",\"value\":\"4\"},{\"kind\":\"i64\",\"value\":\"-3\"},{\"kind\":\"i64\",\"value\":\"1\"}]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"1\"}},{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"2\"}}]}}";
const string FlippedSumExpected = "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",\"value\":{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[{\"kind\":\"i64\",\"value\":\"4\"},{\"kind\":\"i64\",\"value\":\"-3\"},{\"kind\":\"i64\",\"value\":\"1\"}]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"1\"}},{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"-2\"}}]}}";
const string ReversedSequenceExpected = "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",\"value\":{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[{\"kind\":\"i64\",\"value\":\"1\"},{\"kind\":\"i64\",\"value\":\"-3\"},{\"kind\":\"i64\",\"value\":\"4\"}]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"1\"}},{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"2\"}}]}}";
const string HeadRequest = "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"headOrZero\",\"arguments\":[{\"kind\":\"sequence\",\"items\":[]}]}";
const string HeadExpected = "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}}";
const string RefusalRequest = "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\",\"arguments\":[{\"kind\":\"i64\",\"value\":\"01\"}]}";
const string RefusalExpected = "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"refusal\",\"code\":\"InvalidI64Encoding\",\"locus\":\"$/arguments/0/value\",\"details\":{\"value\":\"01\"}}";
const string AlteredRefusalExpected = "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"refusal\",\"code\":\"InvalidI64EncodingMutant\",\"locus\":\"$/arguments/0/value\",\"details\":{\"value\":\"01\"}}";

var probes = new Dictionary<string, (string Request, string Expected, string? MutantExpected, Type? MutantException, string Status, string Vector, string Locus)>(StringComparer.Ordinal)
{
    ["flip-sum-sign"] = (SummarizeRequest, SummarizeExpected, FlippedSumExpected, null, "BackendSemanticMismatch", "summarize-mixed", "$/value/fields/2/value"),
    ["reverse-input-sequence"] = (SummarizeRequest, SummarizeExpected, ReversedSequenceExpected, null, "BackendSemanticMismatch", "summarize-mixed", "$/value/fields/0/value/items/0"),
    ["force-eager-head"] = (HeadRequest, HeadExpected, null, typeof(IndexOutOfRangeException), "TargetExecutionFailed", "head-empty", "function/headOrZero/result"),
    ["alter-refusal-code"] = (RefusalRequest, RefusalExpected, AlteredRefusalExpected, null, "BackendSemanticMismatch", "i64-leading-zero", "$/code")
};

if (args.Length != 2 || args[0] is not ("baseline" or "mutant"))
    throw new ArgumentException("usage: MutationConsumer baseline|mutant all|MUTATION_ID");

var selected = args[1] == "all"
    ? probes
    : probes.TryGetValue(args[1], out var probe)
        ? new Dictionary<string, (string Request, string Expected, string? MutantExpected, Type? MutantException, string Status, string Vector, string Locus)>(StringComparer.Ordinal) { [args[1]] = probe }
        : throw new ArgumentException($"unknown mutation: {args[1]}");

foreach (var (mutationId, item) in selected)
{
    string? actual = null;
    Exception? invocationException = null;
    try
    {
        actual = ModuleApi.Invoke(item.Request);
    }
    catch (Exception exception)
    {
        invocationException = exception;
    }

    if (args[0] == "baseline")
    {
        if (invocationException is not null) throw new InvalidOperationException($"baseline exception for {mutationId}", invocationException);
        if (actual != item.Expected) throw new InvalidOperationException($"baseline mismatch for {mutationId}: {actual}");
        Console.WriteLine($"BASELINE_PASS mutation={mutationId}");
    }
    else if (invocationException is not null)
    {
        if (invocationException.GetType() != item.MutantException) throw new InvalidOperationException($"unexpected mutant exception for {mutationId}", invocationException);
        Console.WriteLine($"MUTATION_DETECTED mutation={mutationId} status={item.Status} vector={item.Vector} locus={item.Locus} exception={invocationException.GetType().FullName}");
    }
    else
    {
        if (actual == item.Expected) throw new InvalidOperationException($"mutation survived comparison: {mutationId}");
        if (actual != item.MutantExpected) throw new InvalidOperationException($"unexpected mutant outcome for {mutationId}: {actual}");
        Console.WriteLine($"MUTATION_DETECTED mutation={mutationId} status={item.Status} vector={item.Vector} locus={item.Locus}");
    }
}
