using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kernel.Core;

namespace Strogo.Notation;

public sealed record NotationCompileResult(
    string SchemaVersion,
    string FrontendRevision,
    string SourceDigest,
    string Status,
    string? ProgramRevision,
    KernelProgram? Program,
    string? ErrorCode,
    string? Locus)
{
    public bool Accepted => Status == "Accepted" && Program is not null && ErrorCode is null;

    public byte[] ReportBytes() => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schemaVersion = SchemaVersion,
        frontendRevision = FrontendRevision,
        sourceDigest = SourceDigest,
        programRevision = ProgramRevision,
        status = Status,
        errorCode = ErrorCode,
        locus = Locus
    }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
}

public static class NotationCompiler
{
    public const string SchemaVersion = "strogo.csharp-notation.v0.1";
    public const string GrammarRevision = "e08.1";
    public const string ParserPackage = "Microsoft.CodeAnalysis.CSharp/4.14.0";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] ReservedNames = ["input", "return", "checked", "Select"];

    public static NotationCompileResult Compile(ReadOnlySpan<byte> sourceUtf8)
    {
        string sourceDigest = CanonicalJson.RawDigest(sourceUtf8);
        string frontendRevision = FrontendIdentity.Revision;
        try
        {
            if (sourceUtf8.Length > 65536)
                throw Failure("SourceTooLarge", "offset:0");
            if (sourceUtf8.Length >= 3 && sourceUtf8[0] == 0xEF && sourceUtf8[1] == 0xBB && sourceUtf8[2] == 0xBF)
                throw Failure("SourceEncodingInvalid", "offset:0");

            string source;
            try { source = StrictUtf8.GetString(sourceUtf8); }
            catch (DecoderFallbackException) { throw Failure("SourceEncodingInvalid", "offset:0"); }

            Scanner.Scan(source);
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Script));
            var diagnostic = tree.GetDiagnostics().FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            if (diagnostic is not null)
                throw Failure("SyntaxInvalid", $"offset:{diagnostic.Location.SourceSpan.Start}");

            var root = tree.GetRoot();
            if (root is not CompilationUnitSyntax unit || unit.Members.Count != 1 || unit.Members[0] is not GlobalStatementSyntax global || global.Statement is not BlockSyntax block)
                throw Failure("GrammarInvalid", "offset:0");

            var compiler = new Builder(block);
            KernelProgram program = compiler.Build();
            string revision;
            try
            {
                GraphValidator.Validate(program);
                revision = ProgramCodec.Revision(program);
            }
            catch (KernelException exception)
            {
                throw Failure("GraphInvalid", exception.Error.EntityId is null ? "offset:0" : $"node:{exception.Error.EntityId}");
            }

            return new(SchemaVersion, frontendRevision, sourceDigest, "Accepted", revision, program, null, null);
        }
        catch (NotationFailure failure)
        {
            return new(SchemaVersion, frontendRevision, sourceDigest, "Refused", null, null, failure.Code, failure.Locus);
        }
    }

    private static NotationFailure Failure(string code, string locus) => new(code, locus);

    private sealed class NotationFailure(string code, string locus) : Exception
    {
        public string Code { get; } = code;
        public string Locus { get; } = locus;
    }

    private static class Scanner
    {
        public static void Scan(string source)
        {
            int tokens = 0, nesting = 0, statements = 0, unaryChain = 0;
            bool expressionStart = true;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (c > 126) throw Failure("UnsupportedSyntax", $"offset:{i}");
                if (c is ' ' or '\t' or '\r' or '\n') continue;
                if (c is '/' && i + 1 < source.Length && source[i + 1] is '/' or '*')
                    throw Failure("UnsupportedSyntax", $"offset:{i}");
                if (c is '#' or '\'' or '"' or '$' or '[' or ']')
                    throw Failure("UnsupportedSyntax", $"offset:{i}");

                if (char.IsAsciiLetter(c) || c == '_')
                {
                    int start = i++;
                    while (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_')) i++;
                    i--;
                    AddToken(ref tokens, i, source.Length, start);
                    expressionStart = false;
                    unaryChain = 0;
                    continue;
                }
                if (char.IsAsciiDigit(c))
                {
                    int start = i++;
                    while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
                    if (i < source.Length && (source[i] is 'L' or 'l')) i++;
                    i--;
                    AddToken(ref tokens, i, source.Length, start);
                    expressionStart = false;
                    unaryChain = 0;
                    continue;
                }

                int width = 1;
                if (c == '<' && i + 1 < source.Length && source[i + 1] == '=') width = 2;
                else if (c == '=' && i + 1 < source.Length && source[i + 1] == '=') width = 2;
                else if (c is '<' or '=' or '!' or '&' or '|' or '+' or '-' or '.' or '{' or '}' or '(' or ')' or ';' or ',' or ':') { }
                else throw Failure("UnsupportedSyntax", $"offset:{i}");

                if (c is '{' or '(') { nesting++; if (nesting > 32) throw Failure("DelimiterDepthExceeded", $"offset:{i}"); }
                if (c is '}' or ')') { nesting--; if (nesting < 0) throw Failure("SyntaxInvalid", $"offset:{i}"); }
                if (c == ';') { statements++; if (statements > 32) throw Failure("SyntaxInvalid", $"offset:{i}"); }
                if (c is '!' or '-' && expressionStart)
                {
                    unaryChain++;
                    if (unaryChain > 1) throw Failure("UnaryChainExceeded", $"offset:{i}");
                }
                else if (c is not '(' and not ',' and not '=' and not ':' && !(c is '!' or '-' && expressionStart)) unaryChain = 0;
                expressionStart = c is '(' or ',' or '=' or ':' or '!' or '-' || (c == '<' && width == 2);
                AddToken(ref tokens, i, source.Length, i, width);
                i += width - 1;
            }
            if (nesting != 0) throw Failure("SyntaxInvalid", $"offset:{source.Length}");
        }

        private static void AddToken(ref int tokens, int end, int length, int start, int width = 1)
        {
            tokens++;
            if (tokens > 4096) throw Failure("TokenLimitExceeded", $"offset:{Math.Min(start, length)}");
        }
    }

    private sealed class Builder
    {
        private readonly BlockSyntax _block;
        private readonly Dictionary<string, (KernelType Type, string NodeId)> _locals = new(StringComparer.Ordinal);
        private readonly HashSet<string> _declaredNames;
        private readonly Dictionary<string, KernelNode> _nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<long, string> _i64Constants = new();
        private readonly Dictionary<bool, string> _boolConstants = new();
        private string? _accepted;
        private string? _available;
        private string? _reserved;
        private bool _hasAvailableBinding;
        private bool _hasQuantityBinding;

        public Builder(BlockSyntax block)
        {
            _block = block;
            _declaredNames = block.Statements.OfType<LocalDeclarationStatementSyntax>()
                .SelectMany(s => s.Declaration.Variables.Select(v => v.Identifier.ValueText))
                .ToHashSet(StringComparer.Ordinal);
        }

        public KernelProgram Build()
        {
            if (_block.Statements.Count is < 2 or > 129 || _block.Statements[^1] is not ReturnStatementSyntax returnStatement)
                throw Failure("OutputInvalid", $"offset:{_block.SpanStart}");
            foreach (var statement in _block.Statements.Take(_block.Statements.Count - 1))
            {
                if (statement is not LocalDeclarationStatementSyntax local || local.Declaration.Variables.Count != 1)
                    throw Failure("UnsupportedSyntax", $"offset:{statement.SpanStart}");
                AddLocal(local);
            }
            AddOutputs(returnStatement);
            if (!_hasAvailableBinding || !_hasQuantityBinding || !_locals.ContainsKey("zero") || _available is null || _reserved is null)
                throw Failure("OutputInvalid", $"offset:{returnStatement.SpanStart}");
            return new(KernelVersions.Schema, "reserve", "reserve.v0", [.. _nodes.Values], new(_accepted!, _available, _reserved));
        }

        private void AddLocal(LocalDeclarationStatementSyntax statement)
        {
            string name = statement.Declaration.Variables[0].Identifier.ValueText;
            if (_locals.ContainsKey(name)) throw Failure("DuplicateLocal", $"offset:{statement.SpanStart}");
            if (!IsIdentifier(name) || ReservedNames.Contains(name, StringComparer.Ordinal))
                throw Failure("GrammarInvalid", $"offset:{statement.SpanStart}");
            var variable = statement.Declaration.Variables[0];
            if (variable.Initializer is null) throw Failure("GrammarInvalid", $"offset:{variable.SpanStart}");
            KernelType type = ParseType(statement.Declaration.Type);
            (KernelType Actual, string NodeId) value = BuildExpression(variable.Initializer.Value, type, name);
            if (value.Actual != type) throw Failure("TypeMismatch", $"offset:{variable.SpanStart}");
            if (value.NodeId is "n.available" && name != "available" || value.NodeId is "n.quantity" && name != "quantity")
                throw Failure("GrammarInvalid", $"offset:{variable.SpanStart}");
            string nodeId = value.NodeId;
            bool canonicalLiteral = value.NodeId == "n.zero" || value.NodeId.StartsWith("n.const.i64.", StringComparison.Ordinal) || value.NodeId.StartsWith("n.bool.", StringComparison.Ordinal);
            if (name is not ("available" or "quantity" or "zero") && !canonicalLiteral)
                nodeId = $"n.{name}";
            if (name is "available" && nodeId != "n.available" || name is "quantity" && nodeId != "n.quantity" || name is "zero" && nodeId != "n.zero")
                throw Failure("GrammarInvalid", $"offset:{variable.SpanStart}");
            if (name is not ("available" or "quantity" or "zero") && !canonicalLiteral)
            {
                var sourceNode = _nodes[value.NodeId];
                _nodes[nodeId] = sourceNode with { Id = nodeId };
            }
            _locals[name] = (type, nodeId);
            if (name == "available") _hasAvailableBinding = true;
            if (name == "quantity") _hasQuantityBinding = true;
        }

        private void AddOutputs(ReturnStatementSyntax statement)
        {
            if (statement.Expression is not TupleExpressionSyntax tuple || tuple.Arguments.Count != 3)
                throw Failure("OutputInvalid", $"offset:{statement.SpanStart}");
            var refs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var argument in tuple.Arguments)
            {
                string? label = argument.NameColon?.Name.Identifier.ValueText;
                if (label is null || !refs.TryAdd(label, ResolveIdentifier(argument.Expression, argument.SpanStart)))
                    throw Failure("OutputInvalid", $"offset:{argument.SpanStart}");
            }
            if (!refs.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(["accepted", "available", "reserved"]))
                throw Failure("OutputInvalid", $"offset:{statement.SpanStart}");
            _accepted = refs["accepted"];
            _available = refs["available"];
            _reserved = refs["reserved"];
            if (_nodes[_accepted].Type != KernelType.Bool || _nodes[_available].Type != KernelType.I64 || _nodes[_reserved].Type != KernelType.I64)
                throw Failure("TypeMismatch", $"offset:{statement.SpanStart}");
        }

        private (KernelType Actual, string NodeId) BuildExpression(ExpressionSyntax expression, KernelType expected, string targetName)
        {
            if (expression is IdentifierNameSyntax identifier)
            {
                var resolved = Resolve(identifier.Identifier.ValueText, identifier.SpanStart);
                return resolved;
            }
            if (expression is MemberAccessExpressionSyntax member)
            {
                if (member.Expression is not IdentifierNameSyntax input || input.Identifier.ValueText != "input")
                    throw Failure("UnsupportedSyntax", $"offset:{member.SpanStart}");
                string field = member.Name.Identifier.ValueText switch
                {
                    "resourceAvailable" => "state.available",
                    "requestedQuantity" => "event.quantity",
                    _ => throw Failure("UnsupportedSyntax", $"offset:{member.Name.SpanStart}")
                };
                string id = field == "state.available" ? "n.available" : "n.quantity";
                AddNode(new(id, "input", KernelType.I64, [], field));
                return (KernelType.I64, id);
            }
            if (expression is LiteralExpressionSyntax literal)
            {
                if (literal.IsKind(SyntaxKind.TrueLiteralExpression) || literal.IsKind(SyntaxKind.FalseLiteralExpression))
                {
                    bool value = literal.IsKind(SyntaxKind.TrueLiteralExpression);
                    string id = _boolConstants.TryGetValue(value, out var existing) ? existing : $"n.bool.{value.ToString().ToLowerInvariant()}";
                    AddNode(new(id, "bool.const", KernelType.Bool, [], BoolValue: value));
                    _boolConstants[value] = id;
                    return (KernelType.Bool, id);
                }
                if (literal.IsKind(SyntaxKind.NumericLiteralExpression))
                    return AddI64(ParsePositiveLiteral(literal.Token.Text, literal.SpanStart));
            }
            if (expression is PrefixUnaryExpressionSyntax unary)
            {
                if (unary.IsKind(SyntaxKind.LogicalNotExpression))
                {
                    var operand = BuildOperand(unary.Operand, KernelType.Bool);
                    string id = $"n.{targetName}";
                    AddNode(new(id, "bool.not", KernelType.Bool, [operand.NodeId]));
                    return (KernelType.Bool, id);
                }
                if (unary.IsKind(SyntaxKind.UnaryMinusExpression) && unary.Operand is LiteralExpressionSyntax numeric)
                    return AddI64(ParseNegativeLiteral(numeric.Token.Text, unary.SpanStart));
            }
            if (expression is CheckedExpressionSyntax checkedExpression && checkedExpression.Expression is BinaryExpressionSyntax checkedBinary)
            {
                string op = checkedBinary.Kind() switch
                {
                    SyntaxKind.AddExpression => "i64.add_checked",
                    SyntaxKind.SubtractExpression => "i64.sub_checked",
                    _ => throw Failure("UnsupportedSyntax", $"offset:{checkedBinary.SpanStart}")
                };
                var left = BuildOperand(checkedBinary.Left, KernelType.I64);
                var right = BuildOperand(checkedBinary.Right, KernelType.I64);
                string id = $"n.{targetName}";
                AddNode(new(id, op, KernelType.I64, [left.NodeId, right.NodeId]));
                return (KernelType.I64, id);
            }
            if (expression is BinaryExpressionSyntax binary)
            {
                string op = binary.Kind() switch
                {
                    SyntaxKind.LessThanOrEqualExpression => "i64.le",
                    SyntaxKind.EqualsExpression => "i64.eq",
                    SyntaxKind.BitwiseAndExpression => "bool.and",
                    SyntaxKind.BitwiseOrExpression => "bool.or",
                    _ => throw Failure("UnsupportedSyntax", $"offset:{binary.SpanStart}")
                };
                KernelType operandType = op.StartsWith("i64.", StringComparison.Ordinal) ? KernelType.I64 : KernelType.Bool;
                var left = BuildOperand(binary.Left, operandType);
                var right = BuildOperand(binary.Right, operandType);
                string id = $"n.{targetName}";
                AddNode(new(id, op, KernelType.Bool, [left.NodeId, right.NodeId]));
                return (KernelType.Bool, id);
            }
            if (expression is InvocationExpressionSyntax invocation && invocation.Expression is IdentifierNameSyntax select && select.Identifier.ValueText == "Select" && invocation.ArgumentList.Arguments.Count == 3)
            {
                var predicate = BuildOperand(invocation.ArgumentList.Arguments[0].Expression, KernelType.Bool);
                var whenTrue = BuildOperand(invocation.ArgumentList.Arguments[1].Expression, expected);
                var whenFalse = BuildOperand(invocation.ArgumentList.Arguments[2].Expression, expected);
                if (whenTrue.Actual != whenFalse.Actual) throw Failure("TypeMismatch", $"offset:{invocation.SpanStart}");
                string id = $"n.{targetName}";
                AddNode(new(id, "select", whenTrue.Actual, [predicate.NodeId, whenTrue.NodeId, whenFalse.NodeId]));
                return (whenTrue.Actual, id);
            }
            throw Failure("UnsupportedSyntax", $"offset:{expression.SpanStart}");
        }

        private (KernelType Actual, string NodeId) BuildOperand(ExpressionSyntax expression, KernelType expected)
        {
            if (expression is not IdentifierNameSyntax identifier)
            {
                if (expression is BinaryExpressionSyntax binary && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
                    throw Failure("UnsupportedSyntax", $"offset:{expression.SpanStart}");
                throw Failure("GrammarInvalid", $"offset:{expression.SpanStart}");
            }
            var result = Resolve(identifier.Identifier.ValueText, identifier.SpanStart);
            if (result.Type != expected) throw Failure("TypeMismatch", $"offset:{expression.SpanStart}");
            return result;
        }

        private (KernelType Type, string NodeId) Resolve(string name, int offset)
        {
            if (_locals.TryGetValue(name, out var local)) return local;
            throw Failure(_declaredNames.Contains(name) ? "ForwardReference" : "GrammarInvalid", $"offset:{offset}");
        }

        private string ResolveIdentifier(ExpressionSyntax expression, int offset)
        {
            if (expression is not IdentifierNameSyntax identifier) throw Failure("OutputInvalid", $"offset:{offset}");
            return Resolve(identifier.Identifier.ValueText, offset).NodeId;
        }

        private void AddNode(KernelNode node) => _nodes.TryAdd(node.Id, node);

        private (KernelType Actual, string NodeId) AddI64(long value)
        {
            string id = value == 0 ? "n.zero" : $"n.const.i64.{value.ToString(CultureInfo.InvariantCulture)}";
            if (!_i64Constants.ContainsKey(value))
            {
                AddNode(new(id, "i64.const", KernelType.I64, [], I64Value: value));
                _i64Constants[value] = id;
            }
            return (KernelType.I64, id);
        }

        private static KernelType ParseType(TypeSyntax syntax) => syntax is PredefinedTypeSyntax predefined ? predefined.Keyword.ValueText switch
        {
            "long" => KernelType.I64,
            "bool" => KernelType.Bool,
            _ => throw Failure("GrammarInvalid", $"offset:{syntax.SpanStart}")
        } : throw Failure("GrammarInvalid", $"offset:{syntax.SpanStart}");

        private static long ParsePositiveLiteral(string text, int offset)
        {
            if (text.Length < 2 || text[^1] is not ('L' or 'l')) throw Failure("GrammarInvalid", $"offset:{offset}");
            BigInteger value;
            if (!BigInteger.TryParse(text[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 0 || value > long.MaxValue)
                throw Failure("TypeMismatch", $"offset:{offset}");
            return (long)value;
        }

        private static long ParseNegativeLiteral(string text, int offset)
        {
            if (text.Length < 2 || text[^1] is not ('L' or 'l')) throw Failure("GrammarInvalid", $"offset:{offset}");
            BigInteger magnitude;
            if (!BigInteger.TryParse(text[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out magnitude) || magnitude <= 0 || magnitude > (BigInteger)long.MaxValue + 1)
                throw Failure("TypeMismatch", $"offset:{offset}");
            return magnitude == (BigInteger)long.MaxValue + 1 ? long.MinValue : (long)-magnitude;
        }

        private static bool IsIdentifier(string name) => name.Length is >= 1 and <= 48 && name[0] is >= 'a' and <= 'z' && name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
    }

    private static class FrontendIdentity
    {
        public static string Revision { get; } = Compute();

        private static string Compute()
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Strogo.Notation.packages.lock.json")
                ?? throw new InvalidOperationException("Embedded frontend packages.lock.json is missing");
            string dependencyLockDigest = Convert.ToHexStringLower(SHA256.HashData(stream));
            var descriptor = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["coreSchema"] = KernelVersions.Schema,
                ["coreSemantics"] = KernelVersions.Semantics,
                ["dependencyLockDigest"] = dependencyLockDigest,
                ["grammarRevision"] = GrammarRevision,
                ["parserPackage"] = ParserPackage,
                ["schemaVersion"] = SchemaVersion
            };
            return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(descriptor)));
        }
    }
}
