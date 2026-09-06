using System.Text.Json;
using System.Security.Cryptography;
namespace Kernel.Graph;
public sealed record GraphProgram(string ProgramId, byte[] CanonicalBytes, string Digest);
public static class Programs {
    public const string Profile="task-graph.clone.v1";
    static readonly string[] Ops=["task.reachable_contains","ids.require_fresh_bijection","task.clone_reachable","edge.remap_internal","edge.remap_internal","graph.additions"];
    static readonly string[] Types=["ReachableTaskSet","ValidatedFreshIds","ClonedTaskSet","ContainsEdgeSet","BlocksEdgeSet","TaskGraphPatch"];
    public static string Hash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    public static GraphProgram Parse(byte[] source) {
        using var d=Codec.Parse(source);var e=d.RootElement;Codec.Shape(e,"schemaVersion","programId","profileId","steps","outputs");
        string S(string f)=>Codec.Str(e.GetProperty(f));
        if(S("schemaVersion")!="kernel.graph.v1"||S("profileId")!=Profile||!Codec.ValidId(S("programId")))throw new GraphException("program","SchemaInvalid");
        var steps=Codec.Arr(e.GetProperty("steps"),6);if(steps.Length!=6)throw new GraphException("program","PipelineInvalid");
        var ids=new List<string>();var used=new HashSet<string>();
        for(int i=0;i<6;i++){
            var s=steps[i];var fields=new List<string>{"id","op","type","args"};if(i==2)fields.Add("policy");if(i is 3 or 4)fields.Add("kind");Codec.Shape(s,fields.ToArray());
            var id=Codec.Str(s.GetProperty("id"),64);if(!Codec.ValidId(id)||!used.Add(id))throw new GraphException("program","PipelineInvalid");
            if(Codec.Str(s.GetProperty("op"))!=Ops[i]||Codec.Str(s.GetProperty("type"))!=Types[i])throw new GraphException("program","PipelineInvalid");
            string[] expected=i switch {
                0=>["input.graph","input.sourceRootId"],
                1=>[ids[0],"input.taskIdMap","input.criterionIdMap"],
                2=>["input.graph",ids[0],ids[1],"input.dateDeltaMinutes"],
                3 or 4=>["input.graph",ids[0],ids[1]], _=>[ids[2],ids[3],ids[4]]
            };
            if(!Codec.Arr(s.GetProperty("args"),4).Select(x=>Codec.Str(x)).SequenceEqual(expected))throw new GraphException("program","PipelineInvalid");
            if(i is 3 or 4 && Codec.Str(s.GetProperty("kind"))!=(i==3?"contains":"blocks"))throw new GraphException("program","PipelineInvalid");
            if(i==2){
                var p=s.GetProperty("policy");
                string[] keys=["copyPayload","copyRepeatRule","shiftStart","shiftDue","resetStatus","resetHistory","resetCriteriaCompletion"];
                Codec.Shape(p,keys);
                foreach(var k in keys)if(k=="resetStatus"?Codec.Str(p.GetProperty(k))!="Planned":p.GetProperty(k).ValueKind!=JsonValueKind.True)
                    throw new GraphException("program","PolicyInvalid");
            } ids.Add(id);
        }
        var output=e.GetProperty("outputs");Codec.Shape(output,"result");
        if(Codec.Str(output.GetProperty("result"))!=ids[5])throw new GraphException("program","PipelineInvalid");
        var canonical=Json.Canonical(source);return new(S("programId"),canonical,Hash(canonical));
    }
    // Only this closed AST reaches the emitter. Candidate identifiers are source-map data.
    public static string Emit(GraphProgram p) => Candidate;
    public static object SourceMap(GraphProgram p) {
        using var d=JsonDocument.Parse(p.CanonicalBytes);
        return d.RootElement.GetProperty("steps").EnumerateArray().Select((s,i)=>new {
            stepId=s.GetProperty("id").GetString(),operation=Ops[i],generatedValue=$"s{i:00}",source="Candidate.dfy"
        }).ToArray();
    }
    public const string Candidate = """
module Candidate {
  import opened TaskGraphModel
  method Run(g: Input) returns (result: Outcome)
    ensures result == ModelOutcome(g)
    ensures result.Accepted? <==> FirstError(g) == 0
    ensures result.Accepted? ==> Unique(AllIds(Apply(g,result.patch).nodes))
    ensures result.Accepted? ==> Endpoints(Apply(g,result.patch).contains+Apply(g,result.patch).blocks,NodeIds(Apply(g,result.patch).nodes))
    ensures result.Accepted? ==> !Cyclic(Apply(g,result.patch))
  {
    var error := FirstError(g);
    if error != 0 { result := Rejected(error); return; }
    var s00 := Reach(g);
    var s01 := g.taskIds;
    var s02 := CloneNodes(Selected(g.nodes,s00),s01,g.criterionIds,g.delta);
    var s03 := Remap(Internal(g.contains,s00),s01);
    var s04 := Remap(Internal(g.blocks,s00),s01);
    var s05 := Patch(s02,s03,s04);
    PatchGraphValidity(g);
    result := Accepted(s05);
  }
}
""";
}
