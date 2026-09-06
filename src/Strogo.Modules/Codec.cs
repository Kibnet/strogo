using System.Text.Json;
using Kernel.Core;

namespace Strogo.Modules;

public static class ModulesCodec
{
    private static object PayloadType(TypeRef type) => type.Kind switch
    {
        "I64" or "Bool" => type.Kind,
        "Record" => new { kind = "Record", name = type.Name },
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

    private static object PayloadNode(FunctionNode node) => node.Op switch
    {
        "i64.const" or "bool.const" => new
        {
            id = node.Id,
            op = node.Op,
            type = PayloadType(node.Type),
            args = node.Args,
            value = node.Value
        },
        _ => new
        {
            id = node.Id,
            op = node.Op,
            type = PayloadType(node.Type),
            args = node.Args,
            value = (string?)null
        }
    };

    private static object PayloadBody(FunctionBody body) => new
    {
        parameters = body.Parameters,
        nodes = body.Nodes.Select(PayloadNode).ToArray(),
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

    private static object PayloadFunctionIr(FunctionIr function) => new
    {
        id = function.Id,
        parameters = function.Parameters,
        returnType = PayloadType(function.ReturnType),
        instructions = function.Instructions.Select(i => new
        {
            destinationIndex = i.DestinationIndex,
            originNodeId = i.OriginNodeId,
            op = i.Op,
            operandIndices = i.OperandIndices,
            type = PayloadType(i.Type),
            value = i.Value
        }).ToArray(),
        resultIndex = function.ResultIndex
    };

    public static byte[] Canonicalize(ModuleSource source)
        => CanonicalJson.Encode(new
        {
            schemaVersion = source.SchemaVersion,
            moduleId = source.ModuleId,
            types = source.Types.Select(PayloadTypeDecl).ToArray(),
            imports = source.Imports.Select(PayloadImport).ToArray(),
            functions = source.Functions.Select(PayloadFunction).ToArray(),
            exports = source.Exports
        });

    public static string SourceDigest(byte[] canonicalSource) => CanonicalJson.RawDigest(canonicalSource);

    public static byte[] Canonicalize(ModuleIr moduleIr)
        => CanonicalJson.Encode(new
        {
            sourceSchema = moduleIr.SourceSchema,
            moduleId = moduleIr.ModuleId,
            sourceDigest = moduleIr.SourceDigest,
            irVersion = StrogoVersions.IrVersion,
            functions = moduleIr.Functions.Select(PayloadFunctionIr).ToArray()
        });

    public static string IrDigest(byte[] canonicalIr) => CanonicalJson.RawDigest(canonicalIr);
}