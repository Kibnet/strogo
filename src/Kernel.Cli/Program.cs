using System.Text;
using System.Text.Json;
using Kernel.Core;
using Kernel.Host;

namespace Kernel.Cli;

internal static class Program
{
    private sealed record Options(string Command, string Directory, string? Event, bool Json);

    internal static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false, true);
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help")
            {
                Console.WriteLine("""
                    Ядро языка для агента v0 — локальный эксперимент Reserve

                    demo       --directory artifacts/demo [--json]
                    initialize --directory artifacts/session [--json]
                    serve      --directory artifacts/session
                    replay     --directory artifacts/demo --event evt-001 [--json]

                    initialize — команда владельца: новая локальная база с остатком 10.
                    serve — закрытый API агента через stdin/stdout JSON Lines.
                    Допустимые методы: Snapshot, Explain, ProposePatch, Prepare, Commit, Replay.
                    Настройки solver, политика и raw state отсутствуют в этом API.
                    """);
                return 0;
            }
            var root = RuntimeSetup.FindRoot();
            var options = ParseOptions(args, root);
            var solver = await RuntimeSetup.CreateSolverAsync(root);
            var database = Path.Combine(options.Directory, "kernel.db");
            if (options.Command is "initialize" or "demo") Directory.CreateDirectory(options.Directory);
            else if (!File.Exists(database)) throw new InvalidOperationException("База не найдена. Сначала выполните initialize или demo в этой папке.");
            using var host = await KernelHost.OpenAsync(database, solver);
            var client = host.Bind("agent");
            switch (options.Command)
            {
                case "initialize":
                    await host.InitializeAsync(await File.ReadAllTextAsync(Path.Combine(root, "fixtures", "reserve.json")));
                    var initial = client.Snapshot();
                    Print(options, initial, "Создан ресурс item-001. Остаток: 10. Начальная программа допущена.");
                    return 0;
                case "demo":
                    return await DemoAsync(host, client, root, options);
                case "serve":
                    return await ServeAsync(client);
                case "replay":
                    return await ReplayAsync(client, options);
                default:
                    throw new ArgumentException("Неизвестная команда. Используйте --help.");
            }
        }
        catch (KernelException ex)
        {
            Console.Error.WriteLine(Protocol.Serialize(new { status = "Error", error = ex.Error }));
            return 1;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or JsonException)
        {
            Console.Error.WriteLine(Protocol.Serialize(new { status = "Error", error = KernelError.Create("cli", "SetupOrUsageError", details: new { message = ex.Message }) }));
            return 2;
        }
        catch (Exception)
        {
            Console.Error.WriteLine(Protocol.Serialize(new { status = "Error", error = KernelError.Create("cli", "InternalError") }));
            return 2;
        }
    }

    private static Options ParseOptions(string[] args, string root)
    {
        var command = args[0];
        if (command is not ("initialize" or "demo" or "serve" or "replay")) throw new ArgumentException("Неизвестная команда.");
        string? directory = null, eventId = null;
        bool json = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Length; i++)
        {
            var flag = args[i];
            if (!seen.Add(flag)) throw new ArgumentException("Параметр указан повторно: " + flag);
            if (flag == "--json") { json = true; continue; }
            if (flag is not ("--directory" or "--event") || ++i >= args.Length) throw new ArgumentException("Неизвестный или незавершённый параметр: " + flag);
            if (flag == "--directory") directory = args[i]; else eventId = args[i];
        }
        if (command == "replay" && eventId is null) throw new ArgumentException("Укажите --event.");
        if (command != "replay" && eventId is not null) throw new ArgumentException("--event разрешён только для replay.");
        if (command == "serve" && json) throw new ArgumentException("serve всегда использует JSON Lines.");
        directory ??= Path.Combine(root, "artifacts", "demo");
        return new(command, Path.GetFullPath(directory), eventId, json);
    }

    private static async Task<int> DemoAsync(KernelHost host, KernelClient client, string root, Options options)
    {
        // The command only bootstraps an empty directory; repeated runs never reset a live state.
        var marker = Path.Combine(options.Directory, "demo.json");
        if (!File.Exists(marker))
        {
            var source = await File.ReadAllTextAsync(Path.Combine(root, "fixtures", "reserve.json"));
            await host.InitializeAsync(source);
            var graph = GraphValidator.Validate(ProgramCodec.Parse(source));
            await File.WriteAllBytesAsync(Path.Combine(options.Directory, "program.json"), ProgramCodec.CanonicalBytes(graph.Program));
            await File.WriteAllBytesAsync(Path.Combine(options.Directory, "program.ir.json"), IrCodec.CanonicalBytes(Lowerer.Lower(graph)));
            var before = client.Snapshot();
            var ev = new ReserveEvent("evt-001", before.ResourceId, "reserve", 3);
            var request = new PrepareRequest(ev, before.StateRevision, before.ProgramRevision, before.PolicyRevision);
            var prepared = await client.PrepareAsync(request);
            if (prepared.Prepared is null) throw new InvalidOperationException("Demo ожидал новый план.");
            var preview = client.Explain(prepared.Prepared.PrepareId);
            var committed = await client.CommitAsync(prepared.Prepared.PrepareId);
            var repeated = await client.PrepareAsync(request);
            var after = client.Snapshot();
            var replay = client.Replay(committed.Receipt.ReceiptId);
            if (after.Available != 7 || repeated.Receipt?.ReceiptId != committed.Receipt.ReceiptId || replay.Output.Available != 7)
                throw new InvalidOperationException("Demo contract mismatch");
            var program = client.Explain(before.ProgramRevision);
            var output = new
            {
                schemaVersion = KernelVersions.Schema, status = "Completed", eventId = ev.EventId,
                receiptId = committed.Receipt.ReceiptId, before, prepared, preview,
                committed, repeated, after, replay, program,
                externalEffects = Array.Empty<object>()
            };
            await File.WriteAllTextAsync(marker, Protocol.Serialize(output) + "\n", new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(options.Directory, "preview.txt"), preview.Text + "\n", new UTF8Encoding(false));
            Print(options, output, $"Резерв: 3. Остаток: 10 → {after.Available}.\nПовтор события: прежний receipt, повторного списания нет.\nReplay: {replay.Status}. Внешние действия: 0.\nПодробности: {marker}");
        }
        else
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(marker));
            var receiptId = doc.RootElement.GetProperty("receiptId").GetString()!;
            var replay = client.Replay(receiptId);
            Print(options, replay, $"Демонстрация уже записана. Replay: {replay.Status}.\nРезультат события: остаток {replay.Output.Available}. Состояние не сбрасывалось.");
        }
        return 0;
    }

    private static async Task<int> ReplayAsync(KernelClient client, Options options)
    {
        var marker = Path.Combine(options.Directory, "demo.json");
        if (!File.Exists(marker)) throw new InvalidOperationException("Нет demo.json. В agent API Replay принимает receiptId напрямую.");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(marker));
        if (doc.RootElement.GetProperty("eventId").GetString() != options.Event)
            throw new ArgumentException("Событие отсутствует в демонстрационном журнале. Используйте Replay(receiptId) через serve.");
        var result = client.Replay(doc.RootElement.GetProperty("receiptId").GetString()!);
        Print(options, result, $"Replay {options.Event}: {result.Status}. Резерв: {result.Output.Reserved}, остаток: {result.Output.Available}. Запись состояния не выполнялась.");
        return 0;
    }

    private static async Task<int> ServeAsync(KernelClient client)
    {
        // Each line is a request. Errors do not terminate the session or reset pending plans.
        while (true)
        {
            var line = await ReadBoundedLineAsync(Console.In);
            if (line is null) return 0;
            try
            {
                if (line.Length == 0) throw Protocol.Error("TransportLimitExceeded");
                var result = await Protocol.DispatchAsync(client, line);
                Console.WriteLine(Protocol.Serialize(new { schemaVersion = KernelVersions.Schema, status = "Ok", result }));
            }
            catch (KernelException ex)
            {
                Console.WriteLine(Protocol.Serialize(new { schemaVersion = KernelVersions.Schema, status = "Error", error = ex.Error }));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
            {
                Console.WriteLine(Protocol.Serialize(new { schemaVersion = KernelVersions.Schema, status = "Error", error = KernelError.Create("protocol", "SchemaInvalid") }));
            }
            catch (Exception)
            {
                Console.WriteLine(Protocol.Serialize(new { schemaVersion = KernelVersions.Schema, status = "Error", error = KernelError.Create("protocol", "InternalError") }));
            }
            await Console.Out.FlushAsync();
        }
    }

    private static async Task<string?> ReadBoundedLineAsync(TextReader reader)
    {
        var builder = new StringBuilder();
        var character = new char[1];
        bool oversized = false, any = false;
        while (await reader.ReadAsync(character.AsMemory()) != 0)
        {
            any = true;
            if (character[0] == '\n') break;
            if (!oversized && builder.Length < 65536) builder.Append(character[0]);
            else oversized = true;
        }
        if (!any) return null;
        if (oversized) return string.Empty;
        return builder.ToString().TrimEnd('\r');
    }

    private static void Print(Options options, object result, string text) => Console.WriteLine(options.Json ? Protocol.Serialize(result) : text);
}
