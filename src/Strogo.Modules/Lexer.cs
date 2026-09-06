using System.Buffers;
using System.Collections.Immutable;
using System.Text.Json;

namespace Strogo.Modules;

public enum ModuleTokenKind
{
    StartObject,
    EndObject,
    StartArray,
    EndArray,
    PropertyName,
    String,
    Number,
    True,
    False,
    Null,
    Colon,
    Comma
}

public sealed record ModuleToken(ModuleTokenKind Kind, int Depth, string? Value, int ByteOffset);

public sealed class ModuleLexerException(string message, int byteOffset) : Exception($"{message} @ {byteOffset}");

public static class ModuleLexer
{
    private const int DefaultMaxBytes = 1_048_576;

    public static ImmutableArray<ModuleToken> Lex(ReadOnlySpan<byte> source, int maxBytes = DefaultMaxBytes)
    {
        if (source.Length > maxBytes)
            throw ModulesExceptionFactory.Error("transport", "TransportLimitExceeded",
                details: new { limit = maxBytes, actual = source.Length });

        var reader = new Utf8JsonReader(source, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth = 256
        });

        var tokens = ImmutableArray.CreateBuilder<ModuleToken>();
        int depth = 0;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    tokens.Add(new ModuleToken(ModuleTokenKind.StartObject, depth++, null, (int)reader.BytesConsumed));
                    break;
                case JsonTokenType.EndObject:
                    tokens.Add(new ModuleToken(ModuleTokenKind.EndObject, depth--, null, (int)reader.BytesConsumed));
                    break;
                case JsonTokenType.StartArray:
                    tokens.Add(new ModuleToken(ModuleTokenKind.StartArray, depth++, null, (int)reader.BytesConsumed));
                    break;
                case JsonTokenType.EndArray:
                    tokens.Add(new ModuleToken(ModuleTokenKind.EndArray, depth--, null, (int)reader.BytesConsumed));
                    break;
                case JsonTokenType.PropertyName:
                    tokens.Add(new ModuleToken(ModuleTokenKind.PropertyName, depth, reader.GetString(), (int)reader.BytesConsumed));
                    break;
                case JsonTokenType.String:
                    tokens.Add(new ModuleToken(ModuleTokenKind.String, depth, reader.GetString(), (int)reader.BytesConsumed));
                    break;
                case JsonTokenType.Number:
                    tokens.Add(new ModuleToken(ModuleTokenKind.Number, depth, reader.GetString(), (int)reader.BytesConsumed));
                    break;
                case JsonTokenType.True:
                    tokens.Add(new ModuleToken(ModuleTokenKind.True, depth, null, (int)reader.BytesConsumed));
                    break;
                case JsonTokenType.False:
                    tokens.Add(new ModuleToken(ModuleTokenKind.False, depth, null, (int)reader.BytesConsumed));
                    break;
                case JsonTokenType.Null:
                    tokens.Add(new ModuleToken(ModuleTokenKind.Null, depth, null, (int)reader.BytesConsumed));
                    break;
                default:
                    throw new ModuleLexerException($"Unexpected token {reader.TokenType}", (int)reader.BytesConsumed);
            }
        }

        if (depth != 0)
            throw new ModuleLexerException("Unbalanced JSON braces", (int)reader.BytesConsumed);

        return tokens.ToImmutable();
    }
}
