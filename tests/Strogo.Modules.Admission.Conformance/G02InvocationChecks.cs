using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Core;
using Strogo.Modules;

internal static class G02InvocationChecks
{
    internal static int Run()
    {
        var module = ModulesCompiler.Compile(ModulesParser.ParseModule("""
        {"schemaVersion":"strogo.module.v0.2","moduleId":"wire.profile","types":[{"id":"Envelope","fields":[{"id":"omega","type":"I64"},{"id":"alpha","type":"Bool"}]}],"imports":[],"exports":["Zeta.export-17"],"functions":[{"id":"Zeta.export-17","parameters":[{"id":"z","type":"I64"},{"id":"a","type":"Bool"},{"id":"m","type":"Envelope"},{"id":"n","type":{"kind":"Seq","elementType":"I64","capacity":"256"}}],"returnType":"Envelope","contractRef":"opaque.contract","body":{"parameters":["z","a","m","n"],"nodes":[],"result":"m"}}]}
        """));
        const string text = """
        {"schemaVersion":"strogo.invoke.v0.1","functionId":"Zeta.export-17","arguments":[{"parameterId":"z","value":{"type":"I64","value":"-9223372036854775808"}},{"parameterId":"a","value":{"type":"Bool","value":false}},{"parameterId":"m","value":{"type":"Envelope","value":[{"fieldId":"omega","value":{"type":"I64","value":"9223372036854775807"}},{"fieldId":"alpha","value":{"type":"Bool","value":true}}]}},{"parameterId":"n","value":{"type":{"kind":"Seq","elementType":"I64","capacity":"256"},"value":[]}}]}
        """;
        var checks = 0;
        void Check(bool condition) { if (!condition) throw new Exception("wire check " + checks); checks++; }
        var bytes = Encoding.UTF8.GetBytes(text);
        var parsed = G02InvocationCodec.Parse(module, bytes);
        Check(parsed.Arguments[0] is ModuleI64 { Value: long.MinValue } && parsed.Arguments[1] is ModuleBool { Value: false });
        var calls = 0;
        ModuleValue Spy(string id, IReadOnlyList<ModuleValue> arguments) { calls++; Check(id == "Zeta.export-17"); return arguments[2]; }
        var output = G02InvocationCodec.Invoke(module, bytes, Spy);
        using (var doc = JsonDocument.Parse(output)) Check(doc.RootElement.GetProperty("status").GetString() == "Returned");
        Check(calls == 1);
        var permuted = JsonNode.Parse(text)!;
        var args = permuted["arguments"]!.AsArray();
        var reversed = args.Reverse().Select(value => value!.DeepClone()).ToArray();
        permuted["arguments"] = new JsonArray(reversed);
        var record = permuted["arguments"]!.AsArray().Single(value => (string?)value!["parameterId"] == "m")!["value"]!["value"]!.AsArray();
        var recordReversed = record.Reverse().Select(value => value!.DeepClone()).ToArray();
        permuted["arguments"]!.AsArray().Single(value => (string?)value!["parameterId"] == "m")!["value"]!["value"] = new JsonArray(recordReversed);
        var reversedRoot = new JsonObject(permuted.AsObject().Reverse().Select(value => KeyValuePair.Create(value.Key, value.Value?.DeepClone())));
        var reordered = Encoding.UTF8.GetBytes(reversedRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Check(G02InvocationCodec.Parse(module, reordered).CanonicalBytes.SequenceEqual(parsed.CanonicalBytes));
        Check(G02InvocationCodec.Invoke(module, reordered, Spy).SequenceEqual(output));
        foreach (var value in new[] { long.MinValue, 0, long.MaxValue })
        {
            var node = JsonNode.Parse(text)!;
            node["arguments"]![0]!["value"]!["value"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Check(G02InvocationCodec.Parse(module, Encoding.UTF8.GetBytes(node.ToJsonString())).Arguments[0] is ModuleI64 integer && integer.Value == value);
        }
        var full = JsonNode.Parse(text)!;
        full["arguments"]![3]!["value"]!["value"] = new JsonArray(Enumerable.Range(0, 256).Select(value => JsonSerializer.SerializeToNode(new { type = "I64", value = value.ToString(System.Globalization.CultureInfo.InvariantCulture) })!).ToArray());
        Check(G02InvocationCodec.Parse(module, Encoding.UTF8.GetBytes(full.ToJsonString())).Arguments[3] is ModuleSequence { Items.Length: 256 });
        void Reject(byte[] input, string code)
        {
            var before = calls;
            using var result = JsonDocument.Parse(G02InvocationCodec.Invoke(module, input, Spy));
            Check(result.RootElement.GetProperty("status").GetString() == "Refused" && result.RootElement.GetProperty("error").GetProperty("code").GetString() == code && calls == before);
        }
        void Mutate(Action<JsonNode> mutation, string code)
        {
            var node = JsonNode.Parse(text)!; mutation(node); Reject(Encoding.UTF8.GetBytes(node.ToJsonString()), code);
        }
        Reject([0xff], "InvalidUtf8");
        Reject(null!, "SchemaInvalid");
        Reject(Encoding.UTF8.GetBytes("{}"), "SchemaInvalid");
        Reject(Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\":", "\"schemaVersion\":\"x\",\"schemaVersion\":")), "DuplicateField");
        Reject(Encoding.UTF8.GetBytes(text.Replace("\"type\":\"I64\"", "\"type\":\"I64\",\"type\":\"I64\"")), "DuplicateField");
        Reject(Encoding.UTF8.GetBytes(text + " garbage"), "SchemaInvalid");
        Reject(Encoding.UTF8.GetBytes(text.Replace("\"arguments\":", "/*comment*/\"arguments\":")), "SchemaInvalid");
        Reject(Encoding.UTF8.GetBytes(text.Replace("\"arguments\":", "\"arguments\":" )[..^1] + ",}"), "SchemaInvalid");
        Reject(Encoding.UTF8.GetBytes(new string(' ', G02InvocationCodec.MaxBytes + 1)), "TransportLimitExceeded");
        Reject(Encoding.UTF8.GetBytes(new string('[', 33) + "0" + new string(']', 33)), "SchemaInvalid");
        Mutate(node => node["schemaVersion"] = "strogo.invoke.v9", "SchemaVersionMismatch");
        Mutate(node => node["functionId"] = "Unknown", "CompiledExportMissing");
        Mutate(node => node["unexpected"] = true, "SchemaInvalid");
        Mutate(node => node.AsObject().Remove("arguments"), "SchemaInvalid");
        Mutate(node => node["arguments"]![0]!["unexpected"] = true, "SchemaInvalid");
        Mutate(node => node["arguments"]!.AsArray().Add(node["arguments"]![0]!.DeepClone()), "DuplicateArgument");
        Mutate(node => node["arguments"]![0]!["parameterId"] = "unknown", "ArgumentsMismatch");
        Mutate(node => node["arguments"]!.AsArray().RemoveAt(0), "ArgumentsMismatch");
        Mutate(node => node["arguments"]![0]!["value"] = JsonNode.Parse("{\"type\":\"Bool\",\"value\":true}"), "InputTypeMismatch");
        Mutate(node => node["arguments"]![1]!["value"]!["value"] = "true", "SchemaInvalid");
        foreach (var invalid in new[] { "-0", "01", "+1", "9223372036854775808", "-9223372036854775809" })
            Mutate(node => node["arguments"]![0]!["value"]!["value"] = invalid, "InvalidI64");
        Mutate(node => node["arguments"]![0]!["value"]!["value"] = 0, "SchemaInvalid");
        Mutate(node => node["arguments"]![0]!["value"]!["value"] = "\u0410", "SchemaInvalid");
        Mutate(node => node["arguments"]![2]!["value"]!["type"] = "DifferentRecord", "InputTypeMismatch");
        Mutate(node => node["arguments"]![2]!["value"]!["value"]!.AsArray().RemoveAt(0), "RecordFieldMismatch");
        Mutate(node => node["arguments"]![2]!["value"]!["value"]!.AsArray().Add(JsonNode.Parse("{\"fieldId\":\"extra\",\"value\":{\"type\":\"Bool\",\"value\":false}}")), "RecordFieldMismatch");
        Mutate(node => node["arguments"]![2]!["value"]!["value"]![1]!["value"]!["type"] = "I64", "InputTypeMismatch");
        Mutate(node => node["arguments"]![2]!["value"]!["value"]!.AsArray().Add(node["arguments"]![2]!["value"]!["value"]![0]!.DeepClone()), "DuplicateRecordField");
        Mutate(node => node["arguments"]![3]!["value"]!["type"]!["capacity"] = "255", "InputTypeMismatch");
        Mutate(node => node["arguments"]![3]!["value"]!["type"]!["capacity"] = "257", "InvalidCapacity");
        var over = full.DeepClone(); over["arguments"]![3]!["value"]!["value"]!.AsArray().Add(JsonNode.Parse("{\"type\":\"I64\",\"value\":\"0\"}"));
        Reject(Encoding.UTF8.GetBytes(over.ToJsonString()), "SequenceCapacityExceeded");
        foreach (var wrong in new ModuleValue[] { new ModuleBool(true), new ModuleRecord("DifferentRecord", ((ModuleRecord)parsed.Arguments[2]).Fields), new ModuleRecord("Envelope", [KeyValuePair.Create<string, ModuleValue>("omega", new ModuleI64(1)), KeyValuePair.Create<string, ModuleValue>("alpha", new ModuleI64(0))]), new ModuleRecord("Envelope", ((ModuleRecord)parsed.Arguments[2]).Fields.Remove("omega")), new ModuleRecord("Envelope", ((ModuleRecord)parsed.Arguments[2]).Fields.Add("extra", new ModuleBool(true))) })
        {
            using var result = JsonDocument.Parse(G02InvocationCodec.Invoke(module, bytes, (_, _) => { calls++; return wrong; }));
            Check(result.RootElement.GetProperty("error").GetProperty("code").GetString() == "CompiledOutputMismatch");
        }
        using (var result = JsonDocument.Parse(G02InvocationCodec.Invoke(module, bytes, (_, _) => throw ModulesExceptionFactory.Error("package", "CompiledPreconditionFailed"))))
            Check(result.RootElement.GetProperty("error").GetProperty("stage").GetString() == "package" && result.RootElement.GetProperty("error").GetProperty("code").GetString() == "CompiledPreconditionFailed");
        var resourceSource = JsonNode.Parse("""
        {"schemaVersion":"strogo.module.v0.2","moduleId":"wire.resources","types":[{"id":"Large","fields":[]}],"imports":[],"exports":["Echo"],"functions":[{"id":"Echo","parameters":[{"id":"r","type":"Large"}],"returnType":"Large","contractRef":"unused","body":{"parameters":["r"],"nodes":[],"result":"r"}}]}
        """);
        var seqType = new { kind = "Seq", elementType = "I64", capacity = "256" };
        foreach (var index in Enumerable.Range(0, 16)) resourceSource!["types"]![0]!["fields"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { id = "field" + index, type = seqType }));
        var resourceModule = ModulesCompiler.Compile(ModulesParser.ParseModule(resourceSource!.ToJsonString()));
        var emptyRecord = new ModuleRecord("Large", Enumerable.Range(0, 16).Select(index => KeyValuePair.Create<string, ModuleValue>("field" + index, new ModuleSequence(new TypeRef("I64"), 256, Array.Empty<ModuleValue>()))));
        var resourceRequest = CanonicalJson.Encode(new { schemaVersion = G02InvocationCodec.RequestVersion, functionId = "Echo", arguments = new[] { new { parameterId = "r", value = OwnerBundleCodec.ValuePayload(emptyRecord, new TypeRef("Record", Name: "Large")) } } });
        foreach (var fullFields in new[] { 8, 16 })
        {
            var huge = new ModuleRecord("Large", Enumerable.Range(0, 16).Select(index => KeyValuePair.Create<string, ModuleValue>("field" + index, new ModuleSequence(new TypeRef("I64"), 256, index < fullFields ? Enumerable.Repeat<ModuleValue>(new ModuleI64(long.MinValue), 256) : Array.Empty<ModuleValue>()))));
            var executions = 0;
            using var result = JsonDocument.Parse(G02InvocationCodec.Invoke(resourceModule, resourceRequest, (_, _) => { executions++; return huge; }));
            Check(executions == 1 && result.RootElement.GetProperty("error").GetProperty("code").GetString() == "OutputLimitExceeded");
        }
        var sequenceModuleSource = JsonNode.Parse("""
        {"schemaVersion":"strogo.module.v0.2","moduleId":"wire.seq","types":[],"imports":[],"exports":["Echo"],"functions":[{"id":"Echo","parameters":[{"id":"s","type":{"kind":"Seq","elementType":"I64","capacity":"256"}}],"returnType":{"kind":"Seq","elementType":"I64","capacity":"256"},"contractRef":"unused","body":{"parameters":["s"],"nodes":[],"result":"s"}}]}
        """);
        var sequenceModule = ModulesCompiler.Compile(ModulesParser.ParseModule(sequenceModuleSource!.ToJsonString()));
        var sequenceRequest = CanonicalJson.Encode(new { schemaVersion = G02InvocationCodec.RequestVersion, functionId = "Echo", arguments = new[] { new { parameterId = "s", value = OwnerBundleCodec.ValuePayload(new ModuleSequence(new TypeRef("I64"), 256, Array.Empty<ModuleValue>()), new TypeRef("Seq", Element: new TypeRef("I64"), Capacity: 256)) } } });
        foreach (var wrong in new[] { new ModuleSequence(new TypeRef("I64"), 255, Array.Empty<ModuleValue>()), new ModuleSequence(new TypeRef("Bool"), 256, Array.Empty<ModuleValue>()), new ModuleSequence(new TypeRef("I64"), 256, Enumerable.Repeat<ModuleValue>(new ModuleI64(0), 257)) })
        {
            using var result = JsonDocument.Parse(G02InvocationCodec.Invoke(sequenceModule, sequenceRequest, (_, _) => wrong));
            Check(result.RootElement.GetProperty("error").GetProperty("code").GetString() == "CompiledOutputMismatch");
        }
        var deepSource = JsonNode.Parse("""
        {"schemaVersion":"strogo.module.v0.2","moduleId":"wire.depth","types":[],"imports":[],"exports":["Build"],"functions":[{"id":"Build","parameters":[{"id":"x","type":"I64"}],"returnType":"Layer11","contractRef":"unused","body":{"parameters":["x"],"nodes":[],"result":"node11"}}]}
        """);
        ModuleValue deepValue = new ModuleI64(1);
        foreach (var index in Enumerable.Range(0, 12))
        {
            var type = index == 0 ? "I64" : "Layer" + (index - 1);
            deepSource!["types"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { id = "Layer" + index, fields = new[] { new { id = "child", type } } }));
            deepSource["functions"]![0]!["body"]!["nodes"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { id = "node" + index, op = "record.make", type = "Layer" + index, recordType = "Layer" + index, args = new[] { index == 0 ? "x" : "node" + (index - 1) }, fieldIds = new[] { "child" } }));
            deepValue = new ModuleRecord("Layer" + index, [KeyValuePair.Create("child", deepValue)]);
        }
        var deepModule = ModulesCompiler.Compile(ModulesParser.ParseModule(deepSource!.ToJsonString()));
        var deepRequest = Encoding.UTF8.GetBytes("""
        {"schemaVersion":"strogo.invoke.v0.1","functionId":"Build","arguments":[{"parameterId":"x","value":{"type":"I64","value":"1"}}]}
        """);
        using (var result = JsonDocument.Parse(G02InvocationCodec.Invoke(deepModule, deepRequest, (_, _) => deepValue)))
            Check(result.RootElement.GetProperty("error").GetProperty("code").GetString() == "OutputLimitExceeded");
        return checks;
    }
}
