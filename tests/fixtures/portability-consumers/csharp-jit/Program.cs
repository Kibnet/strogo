using Strogo.Portable.V01;

const int calls = 50_000;
const string request = "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"summarize\",\"arguments\":[{\"kind\":\"sequence\",\"items\":[{\"kind\":\"i64\",\"value\":\"4\"},{\"kind\":\"i64\",\"value\":\"-3\"},{\"kind\":\"i64\",\"value\":\"1\"}]}]}";
const string expected = "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",\"value\":{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[{\"kind\":\"i64\",\"value\":\"4\"},{\"kind\":\"i64\",\"value\":\"-3\"},{\"kind\":\"i64\",\"value\":\"1\"}]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"1\"}},{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"2\"}}]}}";

for (var index = 0; index < calls; index++)
{
    var actual = ModuleApi.Invoke(request);
    if (actual != expected) throw new InvalidOperationException($"diagnostic call {index}: {actual}");
}

Console.WriteLine($"PASS dotnet JIT diagnostic pid={Environment.ProcessId} calls={calls}");
