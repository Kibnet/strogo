using Kernel.Graph;
using D=TaskGraphModel;
using System.Numerics;
using System.Text;
using System.Text.Json;
using RString=Dafny.ISequence<Dafny.Rune>;
using GNode=Kernel.Graph.TaskNode;

static class Adapter {
    static RString S(string x)=>Dafny.Sequence<char>.UnicodeFromString(x);
    static string S(RString x)=>string.Concat(x.CloneAsArray().Select(c=>char.ConvertFromUtf32(c.Value)));
    static Dafny.ISequence<T> Seq<T>(IEnumerable<T> xs)=>Dafny.Sequence<T>.FromArray(xs.ToArray());
    static D._IDate Date(long? d)=>d is null?D.Date.create_Missing():D.Date.create_Minute(d.Value);
    static long? Date(D._IDate d)=>d.is_Missing?null:checked((long)d.dtor_value);
    static D._IEdge Edge(Kernel.Graph.Edge e)=>D.Edge.create(S(e.From),S(e.To));
    static Kernel.Graph.Edge Edge(D._IEdge e)=>new(S(e.dtor_src),S(e.dtor_dst));
    static D._IBinding Bind(Binding b)=>D.Binding.create(S(b.OldId),S(b.FreshId));
    static D._ICriterion Criterion(Kernel.Graph.Criterion c)=>D.Criterion.create(S(c.Id),S(c.Payload),c.Completed);
    static D._INode Node(GNode n)=>D.Node.create(S(n.Id),S(n.Payload),Date(n.StartMinute),Date(n.DueMinute),
        n.Status=="Planned"?0:n.Status=="Active"?1:2,Seq(n.Criteria.Select(Criterion)),
        Seq(n.RepeatRule is null?Array.Empty<RString>():new[]{S(n.RepeatRule)}),Seq(n.History.Select(S)));
    static GNode Node(D._INode n)=>new(S(n.dtor_id),S(n.dtor_payload),Date(n.dtor_start),Date(n.dtor_due),
        (int)n.dtor_status switch{0=>"Planned",1=>"Active",2=>"Completed",_=>throw new InvalidOperationException("Invalid status")},
        n.dtor_criteria.CloneAsArray().Select(c=>new Kernel.Graph.Criterion(S(c.dtor_id),S(c.dtor_payload),c.dtor_completed)).ToArray(),
        n.dtor_repeatRule.Count==0?null:S(n.dtor_repeatRule.Select(0)),n.dtor_history.CloneAsArray().Select(S).ToArray());
    public static CloneOutcome Evaluate(CloneInput raw){
        var x=Codec.Normalize(raw);var g=x.Graph;
        var input=D.Input.create(Seq(g.Tasks.Select(Node)),Seq(g.Contains.Select(Edge)),Seq(g.Blocks.Select(Edge)),S(x.SourceRootId),
            x.DateDeltaMinutes,Seq(x.TaskIdMap.Select(Bind)),Seq(x.CriterionIdMap.Select(Bind)),
            x.TaskIdMap.Concat(x.CriterionIdMap).All(b=>Codec.ValidId(b.FreshId)));
        var result = Candidate.__default.Run(input);
        if(result.is_Rejected){
            string code=(int)result.dtor_code switch {
                4=>"DuplicateEntityId",5=>"DanglingRelation",6=>"DuplicateRelation",7=>"SourceRootMissing",8=>"ContainmentCycle",
                9=>"TaskIdMapDomainMismatch",10=>"CriterionIdMapDomainMismatch",11=>"FreshIdInvalid",12=>"FreshIdCollision",13=>"DateOverflow",
                _=>throw new InvalidOperationException("Unknown compiled result")
            };
            return CloneOutcome.Reject("domain",code);
        }
        var p=CanonicalGraph.__default.Wire(result.dtor_patch);
        return new("Accepted",new TaskGraphPatch(p.dtor_nodes.CloneAsArray().Select(Node).ToArray(),
            p.dtor_contains.CloneAsArray().Select(Edge).ToArray(),p.dtor_blocks.CloneAsArray().Select(Edge).ToArray()),null);
    }
    static int Main(){
        // Bounded byte reader rejects before allocating an unbounded input string.
        using var input=Console.OpenStandardInput(); using var ms=new MemoryStream();int b;
        while((b=input.ReadByte())!=-1){if(ms.Length>=Codec.MaxBytes){Console.WriteLine(Json.Text(CloneOutcome.Reject("decode","LimitExceeded")));return 0;}ms.WriteByte((byte)b);}
        CloneOutcome result;
        try{var parsed=Codec.Input(ms.ToArray());result=Evaluate(parsed);RuntimeValidation.AgainstInput(parsed,result);}
        catch(GraphException e){result=e.Outcome;}
        catch(Exception){result=CloneOutcome.Reject("runtime","InternalInvariantViolation");}
        Console.WriteLine(Json.Text(result));return result.Error?.Code=="InternalInvariantViolation"?2:0;
    }
}
