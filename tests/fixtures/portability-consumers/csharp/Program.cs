using Strogo.Portable.V01;

var cases = new (string Id, string Input, string Expected)[]
{
    ("summarize", "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"summarize\",\"arguments\":[{\"kind\":\"sequence\",\"items\":[{\"kind\":\"i64\",\"value\":\"4\"},{\"kind\":\"i64\",\"value\":\"-3\"},{\"kind\":\"i64\",\"value\":\"1\"}]}]}", "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",\"value\":{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[{\"kind\":\"i64\",\"value\":\"4\"},{\"kind\":\"i64\",\"value\":\"-3\"},{\"kind\":\"i64\",\"value\":\"1\"}]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"1\"}},{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"2\"}}]}}"),
    ("head-empty", "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"headOrZero\",\"arguments\":[{\"kind\":\"sequence\",\"items\":[]}]}", "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}}"),
    ("echo-summary", "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"echoSummary\",\"arguments\":[{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}},{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}}]}]}", "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",\"value\":{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}},{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}}]}}"),
    ("owner-refusal", "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"adjust\",\"arguments\":[{\"kind\":\"bool\",\"value\":true},{\"kind\":\"i64\",\"value\":\"9223372036854775807\"}]}", "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"refusal\",\"code\":\"OwnerPreconditionFailed\",\"locus\":\"function/adjust/requires\",\"details\":{}}"),
    ("wrong-type", "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\",\"arguments\":[{\"kind\":\"bool\",\"value\":true}]}", "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"refusal\",\"code\":\"RuntimeTypeMismatch\",\"locus\":\"$/arguments/0\",\"details\":{}}"),
    ("leading-zero", "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\",\"arguments\":[{\"kind\":\"i64\",\"value\":\"01\"}]}", "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"refusal\",\"code\":\"InvalidI64Encoding\",\"locus\":\"$/arguments/0/value\",\"details\":{\"value\":\"01\"}}"),
    ("noncanonical", "{ \"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\",\"arguments\":[]}", "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"refusal\",\"code\":\"NonCanonicalTransport\",\"locus\":\"$\",\"details\":{}}"),
    ("malformed", "{", "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"refusal\",\"code\":\"MalformedJson\",\"locus\":\"$\",\"details\":{}}")
};

foreach (var item in cases)
{
    var actual = ModuleApi.Invoke(item.Input);
    if (actual != item.Expected) throw new InvalidOperationException($"{item.Id}: {actual}");
}

var requests = new Dictionary<string, (string? Input, string Code)>(StringComparer.Ordinal)
{
    ["wrong-parameter-type"] = ("{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\",\"arguments\":[{\"kind\":\"bool\",\"value\":true}]}", "RuntimeTypeMismatch"),
    ["missing-record-field"] = ("{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"echoSummary\",\"arguments\":[{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}}]}]}", "RuntimeTypeMismatch"),
    ["extra-record-field"] = ("{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"echoSummary\",\"arguments\":[{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}},{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}},{\"fieldId\":\"x\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}}]}]}", "RuntimeTypeMismatch"),
    ["reordered-record-fields"] = ("{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"echoSummary\",\"arguments\":[{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}},{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[]}}]}]}", "RuntimeTypeMismatch"),
    ["over-capacity-sequence"] = (Request("summarize", Sequence(Enumerable.Repeat("{\"kind\":\"i64\",\"value\":\"0\"}", 9))), "RuntimeTypeMismatch"),
    ["null-input"] = (null, "NullRequest"),
    ["malformed-json"] = ("{", "MalformedJson"),
    ["duplicate-property"] = ("{\"schema\":\"strogo.invoke.v0.1\",\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\",\"arguments\":[]}", "DuplicateProperty"),
    ["unknown-property"] = ("{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\",\"arguments\":[],\"extra\":false}", "UnknownProperty"),
    ["missing-property"] = ("{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\"}", "MissingProperty"),
    ["wrong-schema"] = ("{\"schema\":\"strogo.invoke.v9\",\"functionId\":\"increment\",\"arguments\":[]}", "UnsupportedInvokeSchema"),
    ["array-root"] = ("[]", "InvalidRoot"),
    ["unknown-wire-kind"] = (Request("increment", "{\"kind\":\"wat\"}"), "UnknownWireKind"),
    ["i64-leading-zero"] = (Request("increment", "{\"kind\":\"i64\",\"value\":\"01\"}"), "InvalidI64Encoding"),
    ["i64-invalid"] = (Request("increment", "{\"kind\":\"i64\",\"value\":\"x\"}"), "InvalidI64Encoding"),
    ["noncanonical-property-order"] = ("{\"functionId\":\"increment\",\"schema\":\"strogo.invoke.v0.1\",\"arguments\":[]}", "NonCanonicalTransport"),
    ["utf8-65536"] = (SizedUnknownFunction(65536), "UnknownFunction"),
    ["utf8-65537"] = (SizedUnknownFunction(65537), "InputTooLarge"),
    ["depth-32"] = (Request("increment", NestedArrays(29)), "InvalidRoot"),
    ["depth-33"] = (Request("increment", NestedArrays(30)), "TransportDepthLimitExceeded"),
    ["nodes-2048"] = (RequestWithArguments(Enumerable.Repeat("false", 2044)), "InvalidRoot"),
    ["nodes-2049"] = (RequestWithArguments(Enumerable.Repeat("false", 2045)), "TransportValueLimitExceeded"),
    ["lone-surrogate"] = ("\ud800", "InvalidUnicode"),
    ["lone-surrogate-over-limit"] = ("\ud800" + new string('x', 65537), "InvalidUnicode")
};

foreach (var item in requests)
{
    var output = ModuleApi.Invoke(item.Value.Input!);
    using var document = System.Text.Json.JsonDocument.Parse(output);
    var root = document.RootElement;
    var actual = root.GetProperty("kind").GetString() == "refusal" ? root.GetProperty("code").GetString() : root.GetProperty("kind").GetString() == "success" ? "success" : null;
    if (actual != item.Value.Code) throw new InvalidOperationException($"{item.Key}: expected {item.Value.Code}, actual {output}");
}

Console.WriteLine($"PASS standalone C# consumer cases={cases.Length} transport={requests.Count}");

static string Request(string functionId, string argument) => $"{{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"{functionId}\",\"arguments\":[{argument}]}}";
static string RequestWithArguments(IEnumerable<string> arguments) => $"{{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\",\"arguments\":[{string.Join(',', arguments)}]}}";
static string Sequence(IEnumerable<string> items) => $"{{\"kind\":\"sequence\",\"items\":[{string.Join(',', items)}]}}";
static string NestedArrays(int count) => new string('[', count) + "false" + new string(']', count);
static string SizedUnknownFunction(int bytes)
{
    const string prefix = "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"";
    const string suffix = "\",\"arguments\":[]}";
    return prefix + new string('x', bytes - prefix.Length - suffix.Length) + suffix;
}
