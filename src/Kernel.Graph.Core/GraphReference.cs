using System.Numerics;
namespace Kernel.Graph;

// Independent queue-based oracle. It does not grant admission.
public static class GraphReference {
    public static CloneOutcome Evaluate(CloneInput x) {
        var g=x.Graph;var all=g.Tasks.SelectMany(n=>new[]{n.Id}.Concat(n.Criteria.Select(c=>c.Id))).ToArray();
        CloneOutcome Reject(string code)=>CloneOutcome.Reject("domain",code);
        if(all.Distinct().Count()!=all.Length)return Reject("DuplicateEntityId");
        var nodes=g.Tasks.ToDictionary(n=>n.Id);var edges=g.Contains.Concat(g.Blocks).ToArray();
        if(edges.Any(e=>!nodes.ContainsKey(e.From)||!nodes.ContainsKey(e.To)))return Reject("DanglingRelation");
        if(g.Contains.Distinct().Count()!=g.Contains.Length||g.Blocks.Distinct().Count()!=g.Blocks.Length||edges.Any(e=>e.From==e.To))return Reject("DuplicateRelation");
        if(!nodes.ContainsKey(x.SourceRootId))return Reject("SourceRootMissing");
        var indegree=nodes.Keys.ToDictionary(id=>id,id=>g.Contains.Count(e=>e.To==id));
        var q=new Queue<string>(indegree.Where(p=>p.Value==0).Select(p=>p.Key));int count=0;
        while(q.TryDequeue(out var id)){count++;foreach(var e in g.Contains.Where(e=>e.From==id))if(--indegree[e.To]==0)q.Enqueue(e.To);}
        if(count!=nodes.Count)return Reject("ContainmentCycle");
        var reach=new HashSet<string>();q.Enqueue(x.SourceRootId);
        while(q.TryDequeue(out var id)){if(!reach.Add(id))continue;foreach(var e in g.Contains.Where(e=>e.From==id))q.Enqueue(e.To);}
        var cs=nodes.Values.Where(n=>reach.Contains(n.Id)).SelectMany(n=>n.Criteria).Select(c=>c.Id).ToHashSet();
        bool Exact(Binding[] b,HashSet<string> domain)=>b.Length==domain.Count&&b.Select(v=>v.OldId).ToHashSet().SetEquals(domain);
        if(!Exact(x.TaskIdMap,reach))return Reject("TaskIdMapDomainMismatch");
        if(!Exact(x.CriterionIdMap,cs))return Reject("CriterionIdMapDomainMismatch");
        var fresh=x.TaskIdMap.Concat(x.CriterionIdMap).Select(b=>b.FreshId).ToArray();
        if(fresh.Any(id=>!Codec.ValidId(id)))return Reject("FreshIdInvalid");
        if(fresh.Distinct().Count()!=fresh.Length||fresh.Intersect(all).Any())return Reject("FreshIdCollision");
        bool Fits(long? d)=>d is null||((BigInteger)d.Value+x.DateDeltaMinutes>=long.MinValue&&(BigInteger)d.Value+x.DateDeltaMinutes<=long.MaxValue);
        if(nodes.Values.Where(n=>reach.Contains(n.Id)).Any(n=>!Fits(n.StartMinute)||!Fits(n.DueMinute)))return Reject("DateOverflow");
        var tm=x.TaskIdMap.ToDictionary(b=>b.OldId,b=>b.FreshId);var cm=x.CriterionIdMap.ToDictionary(b=>b.OldId,b=>b.FreshId);
        long? Shift(long? d)=>d is null?null:checked(d.Value+x.DateDeltaMinutes);
        var copies=nodes.Values.Where(n=>reach.Contains(n.Id)).Select(n=>new TaskNode(tm[n.Id],n.Payload,Shift(n.StartMinute),Shift(n.DueMinute),
            "Planned",n.Criteria.Select(c=>new Criterion(cm[c.Id],c.Payload,false)).ToArray(),n.RepeatRule,[])).ToArray();
        Edge[] Remap(Edge[] es)=>es.Where(e=>reach.Contains(e.From)&&reach.Contains(e.To)).Select(e=>new Edge(tm[e.From],tm[e.To])).ToArray();
        return new("Accepted",Codec.Normalize(new TaskGraphPatch(copies,Remap(g.Contains),Remap(g.Blocks))),null);
    }
}
