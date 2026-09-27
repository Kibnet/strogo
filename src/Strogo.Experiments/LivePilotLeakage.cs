using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kernel.Core;

namespace Strogo.Experiments;

public static class LivePilotLeakage
{
    private const string ExpectedStrogo = "{ long available = input.resourceAvailable; long quantity = input.requestedQuantity; bool enough = quantity <= available; long zero = 0L; long debit = Select(enough, quantity, zero); long remaining = checked(available - debit); return (accepted: enough, available: remaining, reserved: debit); }";
    private const string ExpectedCSharp = "public static class Candidate { public static CandidateResult Execute(long resourceAvailable, long requestedQuantity) { bool accepted = requestedQuantity <= resourceAvailable; return accepted ? new CandidateResult(true, checked(resourceAvailable - requestedQuantity), requestedQuantity) : new CandidateResult(false, resourceAvailable, 0); } }";

    public static ForbiddenPromptInventory CreateInventory(PilotManifest pilot)
    {
        string hiddenCorpus = Encoding.UTF8.GetString(CanonicalJson.Encode(LivePilotCorpus.Hidden));
        var exact = new List<string>
        {
            "CalibrationCorpus",
            "CalibrationEvaluator",
            "ReserveOracle",
            "RunNegativeProbes",
            "e09-reserve.v0.1",
            "R01-baseline",
            "docs/evidence/e09-offline-calibration",
            "BuildHidden",
            "hidden-boundary-",
            "hidden-seeded-",
            "0x4d595df4d0f33173UL",
            "state ^= state << 13",
            "state ^= state >> 7",
            "state ^= state << 17",
            pilot.HiddenCorpusDigest,
            pilot.ImplementationIdentity.SourceClosureDigest,
            pilot.ImplementationIdentity.ExperimentCliAssemblyDigest,
            pilot.ImplementationIdentity.ExperimentsAssemblyDigest,
            pilot.ImplementationIdentity.NotationAssemblyDigest,
            pilot.ImplementationIdentity.CoreAssemblyDigest,
            CanonicalJson.RawDigest(Encoding.UTF8.GetBytes(ExpectedStrogo)),
            CanonicalJson.RawDigest(Encoding.UTF8.GetBytes(ExpectedCSharp))
        };
        foreach (var row in LivePilotCorpus.Hidden)
        {
            exact.Add(row.Id);
            exact.Add(Encoding.UTF8.GetString(CanonicalJson.Encode(row)));
        }
        var exactNeedles = exact.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        var normalized = new[] { hiddenCorpus, ExpectedStrogo, ExpectedCSharp }
            .Select(NormalizeWhitespace)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        var skeleton = new ForbiddenPromptInventory(pilot.Protocol, pilot.PilotId, exactNeedles, normalized, string.Empty);
        return skeleton with { Digest = CanonicalJson.RawDigest(CanonicalJson.Encode(skeleton)) };
    }

    public static ForbiddenPromptInventory LoadAndVerifyInventory(string pilotRoot, PilotManifest pilot)
    {
        var actual = LivePilotJson.ReadCanonical<ForbiddenPromptInventory>(Path.Combine(pilotRoot, "forbidden-prompt-inventory.json"));
        var expected = CreateInventory(pilot);
        if (!CanonicalJson.Encode(actual).AsSpan().SequenceEqual(CanonicalJson.Encode(expected)))
            throw LivePilotJson.Failure("ForbiddenPromptInventoryMismatch");
        return actual;
    }

    public static void ValidatePromptInput(string path, ForbiddenPromptInventory inventory)
    {
        string raw = File.ReadAllText(path, new UTF8Encoding(false, true));
        using var document = JsonDocument.Parse(raw);
        var strings = new List<string> { raw };
        CollectStrings(document.RootElement, strings);
        string combined = string.Join('\n', strings);
        if (inventory.ExactNeedles.Any(needle => combined.Contains(needle, StringComparison.Ordinal)))
            throw LivePilotJson.Failure("TrustedContentLeak");
        string normalized = NormalizeWhitespace(combined);
        if (inventory.NormalizedNeedles.Any(needle => normalized.Contains(needle, StringComparison.Ordinal)))
            throw LivePilotJson.Failure("TrustedContentLeak");
    }

    private static void CollectStrings(JsonElement value, List<string> strings)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject()) CollectStrings(property.Value, strings);
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray()) CollectStrings(item, strings);
                break;
            case JsonValueKind.String:
                strings.Add(value.GetString() ?? string.Empty);
                break;
        }
    }

    private static string NormalizeWhitespace(string value) => Regex.Replace(value, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
}
