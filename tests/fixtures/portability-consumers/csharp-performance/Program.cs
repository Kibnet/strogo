using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Strogo.Portable.V01;

const string request = "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"summarize\",\"arguments\":[{\"kind\":\"sequence\",\"items\":[{\"kind\":\"i64\",\"value\":\"4\"},{\"kind\":\"i64\",\"value\":\"-3\"},{\"kind\":\"i64\",\"value\":\"1\"}]}]}";
const string expected = "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",\"value\":{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[{\"kind\":\"i64\",\"value\":\"4\"},{\"kind\":\"i64\",\"value\":\"-3\"},{\"kind\":\"i64\",\"value\":\"1\"}]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"1\"}},{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"2\"}}]}}";

if (args.Length != 1 || args[0] is not ("startup" or "throughput"))
    throw new ArgumentException("mode must be startup or throughput");

if (args[0] == "startup")
{
    InvokeChecked();
    Write(new
    {
        schemaVersion = "strogo.dotnet-performance-observation.v0.1",
        status = "Passed",
        mode = "startup",
        processId = Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
        calls = "1",
        peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64.ToString(CultureInfo.InvariantCulture)
    });
    return;
}

const int warmupCalls = 5_000;
const int repeats = 5;
const int callsPerRepeat = 10_000;
for (var index = 0; index < warmupCalls; index++) InvokeChecked();
var durations = new string[repeats];
var operationsPerSecond = new string[repeats];
for (var repeat = 0; repeat < repeats; repeat++)
{
    var start = Stopwatch.GetTimestamp();
    for (var index = 0; index < callsPerRepeat; index++) InvokeChecked();
    var elapsedNanoseconds = (long)Math.Round(Stopwatch.GetElapsedTime(start).TotalNanoseconds, MidpointRounding.AwayFromZero);
    durations[repeat] = elapsedNanoseconds.ToString(CultureInfo.InvariantCulture);
    operationsPerSecond[repeat] = ((long)Math.Round(callsPerRepeat * 1_000_000_000d / elapsedNanoseconds, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
}
Write(new
{
    schemaVersion = "strogo.dotnet-performance-observation.v0.1",
    status = "Passed",
    mode = "throughput",
    processId = Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
    warmupCalls = warmupCalls.ToString(CultureInfo.InvariantCulture),
    repeats = repeats.ToString(CultureInfo.InvariantCulture),
    callsPerRepeat = callsPerRepeat.ToString(CultureInfo.InvariantCulture),
    durationsNanoseconds = durations,
    operationsPerSecond,
    peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64.ToString(CultureInfo.InvariantCulture)
});

static void InvokeChecked()
{
    var actual = ModuleApi.Invoke(request);
    if (actual != expected) throw new InvalidOperationException($"performance observation changed outcome: {actual}");
}

static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value));
