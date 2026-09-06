using System.Text.Json;
namespace Kernel.Graph;

// Defense in depth for conversion/serialization faults; never grants program admission.
public static class RuntimeValidation {
    static readonly HashSet<string> DomainErrors=["DuplicateEntityId","DanglingRelation","DuplicateRelation","SourceRootMissing",
        "ContainmentCycle","TaskIdMapDomainMismatch","CriterionIdMapDomainMismatch","FreshIdInvalid","FreshIdCollision","DateOverflow"];
    static void Need(bool value){if(!value)throw new GraphException("runtime","InternalInvariantViolation");}
    public static CloneOutcome ReadOutcome(byte[] bytes){
        try {
            using var doc=Codec.Parse(bytes,1048576);var e=doc.RootElement;
            Codec.Shape(e,"status","patch","error");var status=Codec.Str(e.GetProperty("status"));
            if(status=="Accepted"){
                Need(e.GetProperty("error").ValueKind==JsonValueKind.Null);
                var p=e.GetProperty("patch");Codec.Shape(p,"addTasks","addContains","addBlocks");
                var patch=new TaskGraphPatch(Codec.Arr(p.GetProperty("addTasks"),16).Select(Codec.Node).ToArray(),
                    Codec.Arr(p.GetProperty("addContains"),64).Select(Codec.Edge).ToArray(),Codec.Arr(p.GetProperty("addBlocks"),64).Select(Codec.Edge).ToArray());
                var result=new CloneOutcome(status,patch,null);Basic(result);return result;
            }
            Need(status=="Rejected"&&e.GetProperty("patch").ValueKind==JsonValueKind.Null);
            var err=e.GetProperty("error");Codec.Shape(err,"stage","code","involvedIds","witness","allowedRepairs");
            Need(err.GetProperty("witness").ValueKind==JsonValueKind.Null);
            var stage=Codec.Str(err.GetProperty("stage"));var code=Codec.Str(err.GetProperty("code"));
            var ids=Codec.Arr(err.GetProperty("involvedIds"),144).Select(x=>Codec.Str(x,64)).ToArray();
            var repairs=Codec.Arr(err.GetProperty("allowedRepairs"),16).Select(x=>Codec.Str(x)).ToArray();
            Need(ids.All(Codec.ValidId)&&ids.SequenceEqual(ids.Distinct().Order(StringComparer.Ordinal)));
            Need(repairs.Length==1&&repairs[0]==CloneOutcome.Repair(code));
            Need(stage=="domain"?DomainErrors.Contains(code):stage=="decode"&&code is "TransportInvalid" or "SchemaInvalid" or "LimitExceeded");
            return new(status,null,new(stage,code,ids,null,repairs));
        }catch(Exception e) when(e is not OutOfMemoryException){throw new GraphException("runtime","InternalInvariantViolation");}
    }
    public static void Basic(CloneOutcome result){
        Need(result.Status=="Accepted"&&result.Error is null&&result.Patch is not null);
        var p=result.Patch!;
        Need(p.AddTasks.Length is >0 and <=16&&p.AddContains.Length<=64&&p.AddBlocks.Length<=64);
        var ids=p.AddTasks.SelectMany(n=>n.Criteria.Select(c=>c.Id).Prepend(n.Id)).ToArray();
        Need(ids.All(Codec.ValidId)&&ids.Distinct(StringComparer.Ordinal).Count()==ids.Length);
        Need(p.AddTasks.All(n=>n.Status=="Planned"&&n.History.Length==0&&n.Criteria.All(c=>!c.Completed)));
        Need(Json.Bytes(p).SequenceEqual(Json.Bytes(Codec.Normalize(p))));
        var taskIds=p.AddTasks.Select(n=>n.Id).ToHashSet(StringComparer.Ordinal);
        Need(p.AddContains.Concat(p.AddBlocks).All(e=>e.From!=e.To&&taskIds.Contains(e.From)&&taskIds.Contains(e.To)));
        Need(p.AddContains.Distinct().Count()==p.AddContains.Length&&p.AddBlocks.Distinct().Count()==p.AddBlocks.Length);
        Acyclic(taskIds,p.AddContains);
    }
    static void Acyclic(HashSet<string> ids,Edge[] edges){
        var degree=ids.ToDictionary(x=>x,_=>0,StringComparer.Ordinal);
        foreach(var e in edges)degree[e.To]++;
        var q=new Queue<string>(degree.Where(p=>p.Value==0).Select(p=>p.Key));int visited=0;
        while(q.TryDequeue(out var id)){visited++;foreach(var e in edges.Where(e=>e.From==id))if(--degree[e.To]==0)q.Enqueue(e.To);}
        Need(visited==ids.Count);
    }
    public static void AgainstInput(CloneInput input,CloneOutcome result){
        try {
            if(result.Status=="Rejected"){Need(result.Patch is null&&result.Error is not null);return;}
            Basic(result);var p=result.Patch!;
            var tasks=input.TaskIdMap.ToDictionary(b=>b.OldId,b=>b.FreshId,StringComparer.Ordinal);
            var criteria=input.CriterionIdMap.ToDictionary(b=>b.OldId,b=>b.FreshId,StringComparer.Ordinal);
            Need(p.AddTasks.Length==tasks.Count);
            var source=input.Graph.Tasks.ToDictionary(n=>n.Id,StringComparer.Ordinal);
            var output=p.AddTasks.ToDictionary(n=>n.Id,StringComparer.Ordinal);
            var usedCriteria=new HashSet<string>();
            foreach(var (oldId,freshId) in tasks){
                Need(source.ContainsKey(oldId)&&output.ContainsKey(freshId));
                var before=source[oldId];var after=output[freshId];
                Need(before.Payload==after.Payload&&before.RepeatRule==after.RepeatRule);
                Need(after.StartMinute==(before.StartMinute is null?null:checked(before.StartMinute.Value+input.DateDeltaMinutes)));
                Need(after.DueMinute==(before.DueMinute is null?null:checked(before.DueMinute.Value+input.DateDeltaMinutes)));
                Need(before.Criteria.Length==after.Criteria.Length);
                foreach(var c in before.Criteria){
                    Need(criteria.TryGetValue(c.Id,out var fresh));usedCriteria.Add(c.Id);
                    Need(after.Criteria.Any(a=>a.Id==fresh&&a.Payload==c.Payload&&!a.Completed));
                }
            }
            Need(usedCriteria.SetEquals(criteria.Keys));
            Edge[] Mapped(Edge[] edges)=>Codec.Sort(edges.Where(e=>tasks.ContainsKey(e.From)&&tasks.ContainsKey(e.To)).Select(e=>new Edge(tasks[e.From],tasks[e.To])));
            Need(p.AddContains.SequenceEqual(Mapped(input.Graph.Contains))&&p.AddBlocks.SequenceEqual(Mapped(input.Graph.Blocks)));
            var all=input.Graph.Tasks.Concat(p.AddTasks).ToArray();var allIds=all.SelectMany(n=>n.Criteria.Select(c=>c.Id).Prepend(n.Id)).ToArray();
            Need(allIds.Distinct(StringComparer.Ordinal).Count()==allIds.Length);
            Acyclic(all.Select(n=>n.Id).ToHashSet(StringComparer.Ordinal),input.Graph.Contains.Concat(p.AddContains).ToArray());
        }catch(Exception e) when(e is not OutOfMemoryException){throw new GraphException("runtime","InternalInvariantViolation");}
    }
}
