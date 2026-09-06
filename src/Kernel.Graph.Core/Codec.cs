using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Kernel.Graph;

public static class Codec {
    public const int MaxBytes = 65536;
    public static bool ValidId(string s) => Regex.IsMatch(s,@"\A[a-z][a-z0-9._-]{0,63}\z",RegexOptions.CultureInvariant);
    public static JsonDocument Parse(byte[] bytes,int maxBytes=MaxBytes) {
        if(bytes.Length>maxBytes)throw new GraphException("decode","LimitExceeded");
        try {
            _=new UTF8Encoding(false,true).GetString(bytes);
            var doc=JsonDocument.Parse(bytes,new(){MaxDepth=32});
            CheckTree(doc.RootElement); return doc;
        } catch(GraphException){throw;}
        catch(Exception e) when(e is JsonException or DecoderFallbackException){throw new GraphException("decode","TransportInvalid");}
    }
    private static void CheckTree(JsonElement e) {
        if(e.ValueKind==JsonValueKind.Object) {
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in e.EnumerateObject()){if(!keys.Add(p.Name))throw new GraphException("decode","SchemaInvalid"); CheckTree(p.Value);}
        } else if(e.ValueKind==JsonValueKind.Array)foreach(var x in e.EnumerateArray())CheckTree(x);
        else if(e.ValueKind==JsonValueKind.String) {
            try { _=new UTF8Encoding(false,true).GetBytes(e.GetString()!); }
            catch(Exception ex) when(ex is EncoderFallbackException or InvalidOperationException){throw new GraphException("decode","TransportInvalid");}
        }
    }
    public static void Shape(JsonElement e, params string[] fields) {
        if(e.ValueKind!=JsonValueKind.Object || e.EnumerateObject().Count()!=fields.Length ||
           fields.Any(f=>!e.TryGetProperty(f,out _)))throw new GraphException("decode","SchemaInvalid");
    }
    public static string Str(JsonElement e,int max=256) {
        if(e.ValueKind!=JsonValueKind.String)throw new GraphException("decode","SchemaInvalid");
        var s=e.GetString()!;if(Encoding.UTF8.GetByteCount(s)>max)throw new GraphException("decode","LimitExceeded"); return s;
    }
    public static JsonElement[] Arr(JsonElement e,int max) {
        if(e.ValueKind!=JsonValueKind.Array)throw new GraphException("decode","SchemaInvalid");
        if(e.GetArrayLength()>max)throw new GraphException("decode","LimitExceeded");return e.EnumerateArray().ToArray();
    }
    static string Id(JsonElement e){var s=Str(e,64);if(!ValidId(s))throw new GraphException("decode","SchemaInvalid");return s;}
    static long I64(JsonElement e) {
        var s=Str(e,20);
        if(!Regex.IsMatch(s,@"\A(?:0|-?[1-9][0-9]*)\z")||!long.TryParse(s,out var n))throw new GraphException("decode","SchemaInvalid");
        return n;
    }
    static long? Date(JsonElement e)=>e.ValueKind==JsonValueKind.Null?null:I64(e);
    static Criterion Criterion(JsonElement e) {
        Shape(e,"id","payload","completed"); var b=e.GetProperty("completed");
        if(b.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new GraphException("decode","SchemaInvalid");
        return new(Id(e.GetProperty("id")),Str(e.GetProperty("payload"),128),b.GetBoolean());
    }
    internal static TaskNode Node(JsonElement e) {
        Shape(e,"id","payload","startMinute","dueMinute","status","criteria","repeatRule","history");
        var status=Str(e.GetProperty("status"));if(status is not ("Planned" or "Active" or "Completed"))throw new GraphException("decode","SchemaInvalid");
        var repeat=e.GetProperty("repeatRule");
        return new(Id(e.GetProperty("id")),Str(e.GetProperty("payload")),Date(e.GetProperty("startMinute")),Date(e.GetProperty("dueMinute")),
            status,Arr(e.GetProperty("criteria"),8).Select(Criterion).ToArray(),repeat.ValueKind==JsonValueKind.Null?null:Str(repeat),
            Arr(e.GetProperty("history"),8).Select(v=>Str(v)).ToArray());
    }
    internal static Edge Edge(JsonElement e){Shape(e,"from","to");return new(Id(e.GetProperty("from")),Id(e.GetProperty("to")));}
    static Binding Binding(JsonElement e){Shape(e,"oldId","freshId");return new(Id(e.GetProperty("oldId")),Str(e.GetProperty("freshId"),64));}
    public static CloneInput Input(byte[] bytes) {
        using var d=Parse(bytes);var e=d.RootElement;
        Shape(e,"graph","sourceRootId","dateDeltaMinutes","taskIdMap","criterionIdMap");
        var g=e.GetProperty("graph");Shape(g,"tasks","contains","blocks");
        return new(new(Arr(g.GetProperty("tasks"),16).Select(Node).ToArray(),Arr(g.GetProperty("contains"),64).Select(Edge).ToArray(),
            Arr(g.GetProperty("blocks"),64).Select(Edge).ToArray()),Id(e.GetProperty("sourceRootId")),I64(e.GetProperty("dateDeltaMinutes")),
            Arr(e.GetProperty("taskIdMap"),32).Select(Binding).ToArray(),Arr(e.GetProperty("criterionIdMap"),256).Select(Binding).ToArray());
    }
    public static CloneInput Normalize(CloneInput x) => x with {
        Graph=new(x.Graph.Tasks.OrderBy(n=>n.Id,StringComparer.Ordinal).Select(n=>n with{Criteria=n.Criteria.OrderBy(c=>c.Id,StringComparer.Ordinal).ToArray()}).ToArray(),
            Sort(x.Graph.Contains),Sort(x.Graph.Blocks)),
        TaskIdMap=x.TaskIdMap.OrderBy(b=>b.OldId,StringComparer.Ordinal).ThenBy(b=>b.FreshId,StringComparer.Ordinal).ToArray(),
        CriterionIdMap=x.CriterionIdMap.OrderBy(b=>b.OldId,StringComparer.Ordinal).ThenBy(b=>b.FreshId,StringComparer.Ordinal).ToArray()
    };
    public static Edge[] Sort(IEnumerable<Edge> es)=>es.OrderBy(e=>e.From,StringComparer.Ordinal).ThenBy(e=>e.To,StringComparer.Ordinal).ToArray();
    public static TaskGraphPatch Normalize(TaskGraphPatch p)=>new(p.AddTasks.OrderBy(n=>n.Id,StringComparer.Ordinal)
        .Select(n=>n with{Criteria=n.Criteria.OrderBy(c=>c.Id,StringComparer.Ordinal).ToArray()}).ToArray(),Sort(p.AddContains),Sort(p.AddBlocks));
}
