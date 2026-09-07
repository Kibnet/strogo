using System.Buffers;
using System.Collections.Immutable;
using System.Text;
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

public sealed class ModuleLexerException(string message, int byteOffset, Exception? innerException = null)
    : ModuleException("lex", "InvalidJson", $"{message} @ {byteOffset}", details: new { byteOffset }, innerException: innerException)
{
    public int ByteOffset { get; } = byteOffset;
}

public static class ModuleLexer
{
    private const int DefaultMaxBytes = 1_048_576;

    public static ImmutableArray<ModuleToken> Lex(ReadOnlySpan<byte> source, int maxBytes = DefaultMaxBytes, int maxDepth = 64)
    {
        if (source.Length > maxBytes)
            throw ModulesExceptionFactory.Error("transport", "TransportLimitExceeded",
                details: new { limit = maxBytes, actual = source.Length });

        var reader = new Utf8JsonReader(source, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth = maxDepth
        });

        var tokens = ImmutableArray.CreateBuilder<ModuleToken>();
        int depth = 0;

        try
        {
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
                        var number = reader.HasValueSequence
                            ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
                            : Encoding.UTF8.GetString(reader.ValueSpan);
                        tokens.Add(new ModuleToken(ModuleTokenKind.Number, depth, number, (int)reader.BytesConsumed));
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
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new ModuleLexerException("Malformed JSON", (int)reader.BytesConsumed, exception);
        }

        if (depth != 0)
            throw new ModuleLexerException("Unbalanced JSON braces", (int)reader.BytesConsumed);

        return tokens.ToImmutable();
    }
}
