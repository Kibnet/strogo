using Strogo.Portable.V01;

const string SummarizeRequest = "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"summarize\",\"arguments\":[{\"kind\":\"sequence\",\"items\":[{\"kind\":\"i64\",\"value\":\"4\"},{\"kind\":\"i64\",\"value\":\"-3\"},{\"kind\":\"i64\",\"value\":\"1\"}]}]}";
const string SummarizeExpected = "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",\"value\":{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[{\"kind\":\"i64\",\"value\":\"4\"},{\"kind\":\"i64\",\"value\":\"-3\"},{\"kind\":\"i64\",\"value\":\"1\"}]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"1\"}},{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"2\"}}]}}";
const string HeadRequest = "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"headOrZero\",\"arguments\":[{\"kind\":\"sequence\",\"items\":[]}]}";
const string HeadExpected = "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}}";
const string RefusalRequest = "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\",\"arguments\":[{\"kind\":\"i64\",\"value\":\"01\"}]}";
const string RefusalExpected = "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"refusal\",\"code\":\"InvalidI64Encoding\",\"locus\":\"$/arguments/0/value\",\"details\":{\"value\":\"01\"}}";

var probes = new Dictionary<string, (string Request, string Expected, bool CrashAllowed)>(StringComparer.Ordinal)
{
    ["flip-sum-sign"] = (SummarizeRequest, SummarizeExpected, false),
    ["reverse-input-sequence"] = (SummarizeRequest, SummarizeExpected, false),
    ["force-eager-head"] = (HeadRequest, HeadExpected, true),
    ["alter-refusal-code"] = (RefusalRequest, RefusalExpected, false)
};

if (args.Length != 2 || args[0] is not ("baseline" or "mutant"))
    throw new ArgumentException("usage: MutationConsumer baseline|mutant all|MUTATION_ID");

var selected = args[1] == "all"
    ? probes
    : probes.TryGetValue(args[1], out var probe)
        ? new Dictionary<string, (string Request, string Expected, bool CrashAllowed)>(StringComparer.Ordinal) { [args[1]] = probe }
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
        if (!item.CrashAllowed) throw new InvalidOperationException($"unexpected mutant exception for {mutationId}", invocationException);
        Console.WriteLine($"MUTATION_DETECTED mutation={mutationId} mode=exception exception={invocationException.GetType().FullName}");
    }
    else
    {
        if (actual == item.Expected) throw new InvalidOperationException($"mutation survived comparison: {mutationId}");
        Console.WriteLine($"MUTATION_DETECTED mutation={mutationId} mode=mismatch");
    }
}
