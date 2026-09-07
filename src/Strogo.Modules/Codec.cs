using System.Text.Json;
using Kernel.Core;

namespace Strogo.Modules;

public static class ModulesCodec
{
    private static object PayloadType(TypeRef type) => type.Kind switch
    {
        "I64" or "Bool" => type.Kind,
        "Record" => type.Name ?? throw new InvalidOperationException("Record type name is required"),
        "Seq" => new { kind = "Seq", elementType = PayloadType(type.Element!), capacity = type.Capacity },
        _ => throw new InvalidOperationException($"Unsupported type kind: {type.Kind}")
    };

    private static object PayloadField(RecordField field) => new
    {
        id = field.Id,
        type = PayloadType(field.Type)
    };

    private static object PayloadTypeDecl(TypeDecl typeDecl) => new
    {
        id = typeDecl.Id,
        fields = typeDecl.Fields.Select(PayloadField).ToArray()
    };

    private static object PayloadImport(ImportDecl import) => new
    {
        moduleId = import.ModuleId,
        sourceDigest = import.SourceDigest,
        contractDigest = import.ContractDigest
    };

    private static object PayloadParameter(FunctionParameter parameter) => new
    {
        id = parameter.Id,
        type = PayloadType(parameter.Type)
    };

    private static object PayloadNode(FunctionNode node)
    {
        IEnumerable<string> args = node.Args;
        IEnumerable<string> fieldIds = node.Metadata.FieldIds;
        if (node.Op == "record.make")
        {
            var pairs = node.Metadata.FieldIds
                .Select((fieldId, index) => (fieldId, arg: node.Args[index]))
                .OrderBy(pair => pair.fieldId, StringComparer.Ordinal)
                .ToArray();
            args = pairs.Select(pair => pair.arg);
            fieldIds = pairs.Select(pair => pair.fieldId);
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = node.Id,
            ["op"] = node.Op,
            ["type"] = PayloadType(node.Type),
            ["args"] = args.ToArray()
        };

        switch (node.Op)
        {
            case "i64.const": payload["value"] = node.Metadata.Value; break;
            case "bool.const": payload["value"] = bool.Parse(node.Metadata.Value!); break;
            case "record.make": payload["recordType"] = node.Metadata.RecordType; payload["fieldIds"] = fieldIds.ToArray(); break;
            case "record.get": payload["fieldId"] = node.Metadata.FieldId; break;
            case "seq.empty": payload["elementType"] = PayloadType(node.Metadata.ElementType!); payload["capacity"] = node.Metadata.Capacity; break;
            case "call": payload["functionRef"] = node.Metadata.FunctionRef; break;
        }

        return payload;
    }

    private static object PayloadBody(FunctionBody body) => new
    {
        parameters = body.Parameters,
        nodes = body.Nodes.OrderBy(node => node.Id, StringComparer.Ordinal).Select(PayloadNode).ToArray(),
        result = body.Result
    };

    private static object PayloadFunction(FunctionDecl function) => new
    {
        id = function.Id,
        parameters = function.Parameters.Select(PayloadParameter).ToArray(),
        returnType = PayloadType(function.ReturnType),
        contractRef = function.ContractRef,
        body = PayloadBody(function.Body)
    };

    private static object PayloadInstruction(IrInstruction instruction)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["destinationIndex"] = instruction.DestinationIndex,
            ["originNodeId"] = instruction.OriginNodeId,
            ["op"] = instruction.Op,
            ["operandIndices"] = instruction.OperandIndices,
            ["type"] = PayloadType(instruction.Type)
        };

        switch (instruction.Op)
        {
            case "i64.const": payload["value"] = instruction.Metadata.Value; break;
            case "bool.const": payload["value"] = bool.Parse(instruction.Metadata.Value!); break;
            case "record.make": payload["recordType"] = instruction.Metadata.RecordType; payload["fieldIds"] = instruction.Metadata.FieldIds; break;
            case "record.get": payload["fieldId"] = instruction.Metadata.FieldId; break;
            case "seq.empty": payload["elementType"] = PayloadType(instruction.Metadata.ElementType!); payload["capacity"] = instruction.Metadata.Capacity; break;
            case "call": payload["functionRef"] = instruction.Metadata.FunctionRef; break;
        }

        return payload;
    }

    private static object PayloadFunctionIr(FunctionIr function) => new
    {
        id = function.Id,
        parameters = function.Parameters.Select(PayloadParameter).ToArray(),
        returnType = PayloadType(function.ReturnType),
        contractRef = function.ContractRef,
        instructions = function.Instructions.Select(PayloadInstruction).ToArray(),
        resultIndex = function.ResultIndex
    };

    public static byte[] Canonicalize(ModuleSource source)
        => CanonicalJson.Encode(new
        {
            schemaVersion = source.SchemaVersion,
            moduleId = source.ModuleId,
            types = source.Types.OrderBy(type => type.Id, StringComparer.Ordinal).Select(type => new
            {
                id = type.Id,
                fields = type.Fields.OrderBy(field => field.Id, StringComparer.Ordinal).Select(PayloadField).ToArray()
            }).ToArray(),
            imports = source.Imports.OrderBy(importValue => importValue.ModuleId, StringComparer.Ordinal).Select(PayloadImport).ToArray(),
            functions = source.Functions.OrderBy(function => function.Id, StringComparer.Ordinal).Select(PayloadFunction).ToArray(),
            exports = source.Exports.OrderBy(id => id, StringComparer.Ordinal).ToArray()
        });

    public static string SourceDigest(byte[] canonicalSource) => CanonicalJson.RawDigest(canonicalSource);

    public static byte[] Canonicalize(ModuleIr moduleIr)
        => CanonicalJson.Encode(new
        {
            sourceSchema = moduleIr.SourceSchema,
            moduleId = moduleIr.ModuleId,
            sourceDigest = moduleIr.SourceDigest,
            irVersion = StrogoVersions.IrVersion,
            types = moduleIr.Types.OrderBy(type => type.Id, StringComparer.Ordinal).Select(type => new
            {
                id = type.Id,
                fields = type.Fields.OrderBy(field => field.Id, StringComparer.Ordinal).Select(PayloadField).ToArray()
            }).ToArray(),
            imports = moduleIr.Imports.OrderBy(importValue => importValue.ModuleId, StringComparer.Ordinal).Select(PayloadImport).ToArray(),
            functions = moduleIr.Functions.OrderBy(function => function.Id, StringComparer.Ordinal).Select(PayloadFunctionIr).ToArray(),
            exports = moduleIr.Exports.OrderBy(id => id, StringComparer.Ordinal).ToArray()
        });

    public static string IrDigest(byte[] canonicalIr) => CanonicalJson.RawDigest(canonicalIr);
}
