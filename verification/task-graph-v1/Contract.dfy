module TaskGraphModel {
  datatype Criterion = Criterion(id: string, payload: string, completed: bool)
  datatype Date = Missing | Minute(value: int)
  datatype Node = Node(id: string, payload: string, start: Date, due: Date, status: int,
    criteria: seq<Criterion>, repeatRule: seq<string>, history: seq<string>)
  datatype Edge = Edge(src: string, dst: string)
  datatype Binding = Binding(oldId: string, freshId: string)
  datatype Input = Input(nodes: seq<Node>, contains: seq<Edge>, blocks: seq<Edge>, root: string,
    delta: int, taskIds: seq<Binding>, criterionIds: seq<Binding>, freshSyntaxValid: bool)
  datatype Patch = Patch(nodes: seq<Node>, contains: seq<Edge>, blocks: seq<Edge>)
  datatype Outcome = Accepted(patch: Patch) | Rejected(code: int)
function NodeIds(ns: seq<Node>): set<string> { set n | n in ns :: n.id }
  function Criteria(ns: seq<Node>): seq<Criterion>
    decreases |ns|
  { if |ns| == 0 then [] else ns[0].criteria + Criteria(ns[1..]) }
  function CriterionIds(ns: seq<Node>): set<string> { set n, c | n in ns && c in n.criteria :: c.id }
  function AllIds(ns: seq<Node>): seq<string>
    ensures forall x | x in AllIds(ns) :: x in NodeIds(ns)+CriterionIds(ns)
    decreases |ns|
  { if |ns| == 0 then [] else [ns[0].id] + seq(|ns[0].criteria|, i requires 0 <= i < |ns[0].criteria| => ns[0].criteria[i].id) + AllIds(ns[1..]) }
  predicate Unique<T(==)>(s: seq<T>) { forall i, j | 0 <= i < j < |s| :: s[i] != s[j] }
  predicate Endpoints(es: seq<Edge>, ids: set<string>) { forall e | e in es :: e.src in ids && e.dst in ids }
  function Next(es: seq<Edge>, seen: set<string>): set<string> { set e | e in es && e.src in seen :: e.dst }
  function Grow(ids: set<string>, es: seq<Edge>, seen: set<string>): set<string>
    requires seen <= ids && Endpoints(es, ids)
    ensures seen <= Grow(ids, es, seen) <= ids
    ensures Next(es, Grow(ids, es, seen)) <= Grow(ids, es, seen)
    ensures forall closed: set<string> :: seen <= closed && Next(es, closed) <= closed ==> Grow(ids, es, seen) <= closed
    decreases ids - seen
  {
    var more := seen + Next(es, seen);
    if more == seen then seen else Grow(ids, es, more)
  }
  function Reach(g: Input): set<string>
    requires Endpoints(g.contains, NodeIds(g.nodes)) && g.root in NodeIds(g.nodes)
  { Grow(NodeIds(g.nodes), g.contains, {g.root}) }
  predicate Cyclic(g: Input)
    requires Endpoints(g.contains, NodeIds(g.nodes))
  { exists e | e in g.contains :: e.src in Grow(NodeIds(g.nodes), g.contains, {e.dst}) }
  function Domain(bs: seq<Binding>): set<string> { set b | b in bs :: b.oldId }
  function Range(bs: seq<Binding>): seq<string> { seq(|bs|, i requires 0 <= i < |bs| => bs[i].freshId) }
  predicate ExactMap(bs: seq<Binding>, domain: set<string>)
  { Domain(bs) == domain && |bs| == |domain| }
  function Lookup(bs: seq<Binding>, id: string): string
    requires id in Domain(bs)
    decreases |bs|
  { if bs[0].oldId == id then bs[0].freshId else Lookup(bs[1..], id) }
  function Selected(ns: seq<Node>, reach: set<string>): seq<Node>
    ensures NodeIds(Selected(ns,reach)) <= reach
    ensures forall n :: n in Selected(ns,reach) <==> n in ns && n.id in reach
    decreases |ns|
  { if |ns| == 0 then [] else (if ns[0].id in reach then [ns[0]] else []) + Selected(ns[1..], reach) }
  predicate Fits(d: Date, delta: int)
  { d.Missing? || -9223372036854775808 <= d.value + delta <= 9223372036854775807 }
  function Shift(d: Date, delta: int): Date
  { if d.Missing? then Missing else Minute(d.value + delta) }
  function CloneCriteria(cs: seq<Criterion>, ids: seq<Binding>): seq<Criterion>
    requires forall c | c in cs :: c.id in Domain(ids)
    ensures |CloneCriteria(cs, ids)| == |cs|
    ensures forall i | 0 <= i < |cs| :: CloneCriteria(cs, ids)[i] == Criterion(Lookup(ids, cs[i].id), cs[i].payload, false)
  { seq(|cs|, i requires 0 <= i < |cs| => Criterion(Lookup(ids, cs[i].id), cs[i].payload, false)) }
  function CloneNodes(ns: seq<Node>, tids: seq<Binding>, cids: seq<Binding>, delta: int): seq<Node>
    requires NodeIds(ns) <= Domain(tids) && CriterionIds(ns) <= Domain(cids)
    ensures |CloneNodes(ns,tids,cids,delta)| == |ns|
    ensures forall i | 0 <= i < |ns| :: (CloneNodes(ns,tids,cids,delta)[i] ==
      Node(Lookup(tids, ns[i].id), ns[i].payload, Shift(ns[i].start,delta), Shift(ns[i].due,delta),
        0, CloneCriteria(ns[i].criteria,cids), ns[i].repeatRule, []))
  { seq(|ns|, i requires 0 <= i < |ns| => Node(Lookup(tids,ns[i].id),ns[i].payload,
      Shift(ns[i].start,delta),Shift(ns[i].due,delta),0,CloneCriteria(ns[i].criteria,cids),ns[i].repeatRule,[])) }
  function Internal(es: seq<Edge>, reach: set<string>): seq<Edge>
    ensures forall e | e in Internal(es,reach) :: e.src in reach && e.dst in reach
    decreases |es|
  { if |es| == 0 then [] else (if es[0].src in reach && es[0].dst in reach then [es[0]] else []) + Internal(es[1..],reach) }
  function Remap(es: seq<Edge>, ids: seq<Binding>): seq<Edge>
    requires Endpoints(es,Domain(ids))
    ensures |Remap(es,ids)| == |es|
    ensures forall i | 0 <= i < |es| :: Remap(es,ids)[i] == Edge(Lookup(ids,es[i].src),Lookup(ids,es[i].dst))
  { seq(|es|, i requires 0 <= i < |es| => Edge(Lookup(ids,es[i].src),Lookup(ids,es[i].dst))) }
  function FirstError(g: Input): int {
    if !Unique(AllIds(g.nodes)) then 4
    else if !Endpoints(g.contains + g.blocks, NodeIds(g.nodes)) then 5
    else if !Unique(g.contains) || !Unique(g.blocks) || (exists e | e in g.contains+g.blocks :: e.src == e.dst) then 6
    else if g.root !in NodeIds(g.nodes) then 7
    else if Cyclic(g) then 8
    else if !ExactMap(g.taskIds, Reach(g)) then 9
    else if !ExactMap(g.criterionIds, CriterionIds(Selected(g.nodes,Reach(g)))) then 10
    else if !g.freshSyntaxValid then 11
    else if !Unique(Range(g.taskIds)+Range(g.criterionIds)) ||
      (exists id | id in Range(g.taskIds)+Range(g.criterionIds) :: id in AllIds(g.nodes)) then 12
    else if exists n | n in Selected(g.nodes,Reach(g)) :: !Fits(n.start,g.delta) || !Fits(n.due,g.delta) then 13
    else 0
  }
  function ExpectedPatch(g: Input): Patch
    requires FirstError(g) == 0
  {
    var r := Reach(g);
    Patch(CloneNodes(Selected(g.nodes,r),g.taskIds,g.criterionIds,g.delta),
      Remap(Internal(g.contains,r),g.taskIds),Remap(Internal(g.blocks,r),g.taskIds))
  }
  function ModelOutcome(g: Input): Outcome {
    if FirstError(g) != 0 then Rejected(FirstError(g)) else Accepted(ExpectedPatch(g))
  }
lemma LookupFacts(bs: seq<Binding>, x: string)
    requires x in Domain(bs)
    ensures Binding(x, Lookup(bs,x)) in bs
    ensures Lookup(bs,x) in Range(bs)
    decreases |bs|
  {
    if bs[0].oldId != x { LookupFacts(bs[1..],x); }
    ghost var i :| 0 <= i < |bs| && bs[i] == Binding(x,Lookup(bs,x));
    assert Range(bs)[i] == Lookup(bs,x);
  }
  lemma LookupInjective(bs: seq<Binding>, x: string, y: string)
    requires x in Domain(bs) && y in Domain(bs) && Unique(Range(bs))
    ensures Lookup(bs,x) == Lookup(bs,y) ==> x == y
  {
    LookupFacts(bs,x); LookupFacts(bs,y);
    if Lookup(bs,x) == Lookup(bs,y) {
      ghost var i :| 0 <= i < |bs| && bs[i] == Binding(x,Lookup(bs,x));
      ghost var j :| 0 <= j < |bs| && bs[j] == Binding(y,Lookup(bs,y));
      assert Range(bs)[i] == Range(bs)[j];
      assert i == j;
    }
  }
  lemma InternalExact(es: seq<Edge>, r: set<string>)
    ensures forall e :: e in Internal(es,r) <==> e in es && e.src in r && e.dst in r
    ensures Unique(es) ==> Unique(Internal(es,r))
    decreases |es|
  {
    if |es| > 0 { InternalExact(es[1..],r); }
  }
  function Apply(g: Input, p: Patch): Input {
    Input(g.nodes+p.nodes,g.contains+p.contains,g.blocks+p.blocks,g.root,g.delta,g.taskIds,g.criterionIds,g.freshSyntaxValid)
  }
  lemma ApplyFramesOriginal(g: Input,p: Patch)
    ensures Apply(g,p).nodes[..|g.nodes|] == g.nodes
    ensures Apply(g,p).contains[..|g.contains|] == g.contains
    ensures Apply(g,p).blocks[..|g.blocks|] == g.blocks
  {}
function Image(bs: seq<Binding>, s: set<string>): set<string>
    requires s <= Domain(bs)
  { set x | x in s :: Lookup(bs,x) }
  lemma ImageFacts(bs: seq<Binding>, s: set<string>)
    requires s <= Domain(bs)
    ensures Image(bs,s) <= (set x | x in Range(bs))
  {
    forall x | x in s ensures Lookup(bs,x) in Range(bs) { LookupFacts(bs,x); }
  }
  lemma CloneNodeIds(ns: seq<Node>, tids: seq<Binding>, cids: seq<Binding>, delta: int)
    requires NodeIds(ns) <= Domain(tids) && CriterionIds(ns) <= Domain(cids)
    ensures NodeIds(CloneNodes(ns,tids,cids,delta)) == Image(tids,NodeIds(ns))
  {
    var out := CloneNodes(ns,tids,cids,delta);
    forall n | n in out ensures n.id in Image(tids,NodeIds(ns)) {
      ghost var i :| 0 <= i < |out| && out[i] == n;
      assert ns[i].id in NodeIds(ns);
    }
    forall id | id in NodeIds(ns) ensures Lookup(tids,id) in NodeIds(out) {
      var n :| n in ns && n.id == id;
      ghost var i :| 0 <= i < |ns| && ns[i] == n;
      assert out[i].id == Lookup(tids,id);
    }
  }
  lemma SelectedIds(ns: seq<Node>, r: set<string>)
    requires r <= NodeIds(ns)
    ensures NodeIds(Selected(ns,r)) == r
  {}
  lemma RemapFacts(es: seq<Edge>, bs: seq<Binding>)
    requires Endpoints(es,Domain(bs))
    ensures forall e :: e in Remap(es,bs) <==>
      exists o | o in es :: e == Edge(Lookup(bs,o.src),Lookup(bs,o.dst))
    ensures Endpoints(Remap(es,bs),Image(bs,Domain(bs)))
  {
    forall e | e in Remap(es,bs)
      ensures exists o | o in es :: e == Edge(Lookup(bs,o.src),Lookup(bs,o.dst))
    {
      ghost var i :| 0 <= i < |es| && Remap(es,bs)[i] == e;
      assert es[i] in es;
    }
    forall o | o in es ensures Edge(Lookup(bs,o.src),Lookup(bs,o.dst)) in Remap(es,bs) {
      ghost var i :| 0 <= i < |es| && es[i] == o;
      assert Remap(es,bs)[i] == Edge(Lookup(bs,o.src),Lookup(bs,o.dst));
    }
  }
  lemma PatchEndpoints(g: Input)
    requires FirstError(g) == 0
    ensures Endpoints(ExpectedPatch(g).contains+ExpectedPatch(g).blocks,NodeIds(ExpectedPatch(g).nodes))
    ensures NodeIds(ExpectedPatch(g).nodes) == Image(g.taskIds,Reach(g))
    ensures NodeIds(ExpectedPatch(g).nodes) * NodeIds(g.nodes) == {}
  {
    var r := Reach(g);
    SelectedIds(g.nodes,r);
    CloneNodeIds(Selected(g.nodes,r),g.taskIds,g.criterionIds,g.delta);
    RemapFacts(Internal(g.contains,r),g.taskIds);
    RemapFacts(Internal(g.blocks,r),g.taskIds);
    ImageFacts(g.taskIds,r);
    forall id | id in NodeIds(g.nodes) ensures id in AllIds(g.nodes) {
      NodeIdInAll(g.nodes,id);
    }
  }
  lemma NodeIdInAll(ns: seq<Node>, id: string)
    requires id in NodeIds(ns)
    ensures id in AllIds(ns)
    decreases |ns|
  { if ns[0].id != id { NodeIdInAll(ns[1..],id); } }
lemma ClosureInside(g: Input, start: string)
    requires FirstError(g) == 0 && start in Reach(g)
    ensures Grow(NodeIds(g.nodes),g.contains,{start}) <= Reach(g)
  {}
  lemma {:isolate_assertions} CombinedClosure(g: Input, c: set<string>)
    requires FirstError(g) == 0
    requires c <= Reach(g) && Next(g.contains,c) <= c
    ensures Next(Apply(g,ExpectedPatch(g)).contains, NodeIds(g.nodes)+Image(g.taskIds,c))
      <= NodeIds(g.nodes)+Image(g.taskIds,c)
  {
    UniqueParts(Range(g.taskIds),Range(g.criterionIds));
    var p := ExpectedPatch(g);
    PatchEndpoints(g);
    RemapFacts(Internal(g.contains,Reach(g)),g.taskIds);
    InternalExact(g.contains,Reach(g));
    ImageFacts(g.taskIds,c);
    forall e | e in g.contains+p.contains && e.src in NodeIds(g.nodes)+Image(g.taskIds,c)
      ensures e.dst in NodeIds(g.nodes)+Image(g.taskIds,c)
    {
      if e in p.contains {
        var o :| o in Internal(g.contains,Reach(g)) && e == Edge(Lookup(g.taskIds,o.src),Lookup(g.taskIds,o.dst));
        assert e.src !in NodeIds(g.nodes);
        var x :| x in c && Lookup(g.taskIds,x)==e.src;
        assert Unique(Range(g.taskIds)); LookupInjective(g.taskIds,x,o.src);
        assert o.src in c;
        assert o.dst in Next(g.contains,c);
      }
    }
  }
  lemma {:isolate_assertions} PatchAcyclic(g: Input)
    requires FirstError(g) == 0
    ensures Endpoints(Apply(g,ExpectedPatch(g)).contains,NodeIds(Apply(g,ExpectedPatch(g)).nodes))
    ensures !Cyclic(Apply(g,ExpectedPatch(g)))
  {
    var p := ExpectedPatch(g);
    UniqueParts(Range(g.taskIds),Range(g.criterionIds));
    var a := Apply(g,p);
    var originalIds := NodeIds(g.nodes);
    PatchEndpoints(g);
    assert NodeIds(a.nodes) == originalIds+NodeIds(p.nodes);
    RemapFacts(Internal(g.contains,Reach(g)),g.taskIds);
    InternalExact(g.contains,Reach(g));
    forall e | e in a.contains
      ensures e.src !in Grow(NodeIds(a.nodes),a.contains,{e.dst})
    {
      if e in g.contains {
        var c := Grow(originalIds,g.contains,{e.dst});
        assert Next(a.contains,c) <= c;
        assert Grow(NodeIds(a.nodes),a.contains,{e.dst}) <= c;
      } else {
        var o :| o in Internal(g.contains,Reach(g)) && e==Edge(Lookup(g.taskIds,o.src),Lookup(g.taskIds,o.dst));
        var c := Grow(originalIds,g.contains,{o.dst});
        ClosureInside(g,o.dst);
        CombinedClosure(g,c);
        var closed := originalIds+Image(g.taskIds,c);
        assert {e.dst} <= closed;
        assert Grow(NodeIds(a.nodes),a.contains,{e.dst}) <= closed;
        if e.src in Image(g.taskIds,c) {
          var x :| x in c && Lookup(g.taskIds,x)==e.src;
          assert Unique(Range(g.taskIds)); LookupInjective(g.taskIds,x,o.src);
          assert false;
        }
      }
    }
  }
  lemma UniqueParts<T>(a:seq<T>,b:seq<T>)
    requires Unique(a+b)
    ensures Unique(a) && Unique(b)
    ensures forall x | x in a :: x !in b
  {
    forall i,j | 0<=i<j<|a| ensures a[i]!=a[j] {
      assert (a+b)[i]==a[i] && (a+b)[j]==a[j];
    }
    forall i,j | 0<=i<j<|b| ensures b[i]!=b[j] {
      assert (a+b)[|a|+i]==b[i] && (a+b)[|a|+j]==b[j];
    }
    forall x | x in a ensures x !in b {
      var i :| 0<=i<|a| && a[i]==x;
      if x in b {
        var j :| 0<=j<|b| && b[j]==x;
        assert (a+b)[i]==(a+b)[|a|+j];
      }
    }
  }
  function MapStrings(s:seq<string>,bs:seq<Binding>):seq<string>
    requires forall x | x in s :: x in Domain(bs)
  { seq(|s|, i requires 0<=i<|s| => Lookup(bs,s[i])) }
  lemma MapUnique(s:seq<string>,bs:seq<Binding>)
    requires Unique(s) && Unique(Range(bs))
    requires forall x | x in s :: x in Domain(bs)
    ensures Unique(MapStrings(s,bs))
  {
    forall i,j | 0<=i<j<|s| ensures MapStrings(s,bs)[i]!=MapStrings(s,bs)[j] {
      LookupInjective(bs,s[i],s[j]);
    }
  }
  lemma LookupAppend(a:seq<Binding>,b:seq<Binding>,x:string)
    requires x in Domain(a+b)
    ensures Lookup(a+b,x)==(if x in Domain(a) then Lookup(a,x) else Lookup(b,x))
    decreases |a|
  {
    if |a|==0 { assert a+b==b; }
    else {
      assert (a+b)[0]==a[0]; assert (a+b)[1..]==a[1..]+b;
      assert Domain(a)=={a[0].oldId}+Domain(a[1..]);
      if a[0].oldId!=x { LookupAppend(a[1..],b,x); }
    }
  }
  lemma AllIdsFacts(ns:seq<Node>)
    ensures (set x | x in AllIds(ns))==NodeIds(ns)+CriterionIds(ns)
    ensures Unique(AllIds(ns)) ==> NodeIds(ns)*CriterionIds(ns)=={}
    decreases |ns|
  {
    if |ns|>0 {
      AllIdsFacts(ns[1..]);
      assert ns==[ns[0]]+ns[1..];
      var cs:=seq(|ns[0].criteria|, i requires 0<=i<|ns[0].criteria| => ns[0].criteria[i].id);
      assert AllIds(ns)==[ns[0].id]+cs+AllIds(ns[1..]);
      assert NodeIds(ns)=={ns[0].id}+NodeIds(ns[1..]);
      assert CriterionIds(ns)==(set c | c in ns[0].criteria :: c.id)+CriterionIds(ns[1..]);
      forall c | c in ns[0].criteria ensures c.id in cs {
        var i :| 0<=i<|ns[0].criteria| && ns[0].criteria[i]==c;
        assert cs[i]==c.id;
      }
      forall x | x in cs ensures x in (set c | c in ns[0].criteria :: c.id) {
        var i :| 0<=i<|cs| && cs[i]==x;
        assert ns[0].criteria[i].id==x;
      }
      assert (set x | x in cs)==(set c | c in ns[0].criteria :: c.id);
      if Unique(AllIds(ns)) {
        assert [ns[0].id]+cs+AllIds(ns[1..])==[ns[0].id]+(cs+AllIds(ns[1..]));
        UniqueParts([ns[0].id],cs+AllIds(ns[1..]));
        UniqueParts(cs,AllIds(ns[1..]));
        assert ns[0].id !in cs+AllIds(ns[1..]);
        assert forall x | x in cs :: x !in AllIds(ns[1..]);
      }
    }
  }
  lemma SelectedUnique(ns:seq<Node>,r:set<string>)
    requires Unique(AllIds(ns))
    ensures Unique(AllIds(Selected(ns,r)))
    ensures forall x | x in AllIds(Selected(ns,r)) :: x in AllIds(ns)
    decreases |ns|
  {
    if |ns|>0 {
      var head:=[ns[0].id]+seq(|ns[0].criteria|,i requires 0<=i<|ns[0].criteria| => ns[0].criteria[i].id);
      UniqueParts(head,AllIds(ns[1..]));
      SelectedUnique(ns[1..],r);
      assert ns==[ns[0]]+ns[1..];
      if ns[0].id in r {
        assert Selected(ns,r)==[ns[0]]+Selected(ns[1..],r);
        assert AllIds(Selected(ns,r))==head+AllIds(Selected(ns[1..],r));
        UniqueJoin(head,AllIds(Selected(ns[1..],r)));
      } else { assert Selected(ns,r)==Selected(ns[1..],r); }
    } else { assert Selected(ns,r)==[] && AllIds(Selected(ns,r))==[]; }
  }
  lemma CloneAllIds(ns:seq<Node>,t:seq<Binding>,c:seq<Binding>,d:int)
    requires NodeIds(ns)<=Domain(t) && CriterionIds(ns)<=Domain(c)
    requires Domain(t)*Domain(c)=={}
    ensures AllIds(CloneNodes(ns,t,c,d))==MapStrings(AllIds(ns),t+c)
    decreases |ns|
  {
    AllIdsFacts(ns);
    assert Domain(t+c)==Domain(t)+Domain(c);
    if |ns|>0 {
      CloneAllIds(ns[1..],t,c,d);
      LookupAppend(t,c,ns[0].id);
      forall k | k in ns[0].criteria
        ensures Lookup(t+c,k.id)==Lookup(c,k.id)
      {
        assert Domain(t)*Domain(c)=={};
        assert k.id in CriterionIds(ns); assert k.id in Domain(c);
        if k.id in Domain(t) { assert k.id in Domain(t)*Domain(c); assert false; }
        LookupAppend(t,c,k.id);
      }
      var out:=CloneNodes(ns,t,c,d);
      assert out[1..]==CloneNodes(ns[1..],t,c,d);
      assert AllIds(out)==[out[0].id]+seq(|out[0].criteria|,i requires 0<=i<|out[0].criteria| => out[0].criteria[i].id)+AllIds(out[1..]);
    }
  }
  lemma {:isolate_assertions} PatchIdsUnique(g:Input)
    requires FirstError(g)==0
    ensures Unique(AllIds(ExpectedPatch(g).nodes))
    ensures forall x | x in AllIds(ExpectedPatch(g).nodes) :: x !in AllIds(g.nodes)
    ensures Unique(AllIds(Apply(g,ExpectedPatch(g)).nodes))
  {
    var ns:=Selected(g.nodes,Reach(g));
    AllIdsFacts(g.nodes); AllIdsFacts(ns);
    SelectedUnique(g.nodes,Reach(g));
    assert NodeIds(ns)<=NodeIds(g.nodes) && CriterionIds(ns)<=CriterionIds(g.nodes);
    assert Domain(g.taskIds)*Domain(g.criterionIds)=={};
    CloneAllIds(ns,g.taskIds,g.criterionIds,g.delta);
    RangeAppend(g.taskIds,g.criterionIds);
    MapUnique(AllIds(ns),g.taskIds+g.criterionIds);
    forall x | x in AllIds(ns) ensures Lookup(g.taskIds+g.criterionIds,x) !in AllIds(g.nodes) {
      LookupFacts(g.taskIds+g.criterionIds,x);
    }
    AllIdsAppend(g.nodes,ExpectedPatch(g).nodes);
  }
  lemma AllIdsAppend(a:seq<Node>,b:seq<Node>)
    ensures AllIds(a+b)==AllIds(a)+AllIds(b)
    decreases |a|
  { if |a|>0 { assert (a+b)[0]==a[0]; assert (a+b)[1..]==a[1..]+b; AllIdsAppend(a[1..],b); } else { assert a+b==b; } }
  lemma UniqueJoin<T>(a:seq<T>,b:seq<T>)
    requires Unique(a) && Unique(b) && (forall x | x in a :: x !in b)
    ensures Unique(a+b)
  {
    forall i,j | 0<=i<j<|a+b| ensures (a+b)[i]!=(a+b)[j] {
      if i<|a| && j<|a| { assert (a+b)[i]==a[i] && (a+b)[j]==a[j]; }
      else if i<|a| { assert (a+b)[i] in a && (a+b)[j] in b; }
      else { assert (a+b)[i]==b[i-|a|] && (a+b)[j]==b[j-|a|]; }
    }
  }
lemma RangeAppend(a:seq<Binding>,b:seq<Binding>)
    ensures Range(a+b)==Range(a)+Range(b)
  {
    forall i | 0<=i<|a+b| ensures Range(a+b)[i]==(Range(a)+Range(b))[i] {
      if i<|a| { assert (a+b)[i]==a[i]; }
      else { assert (a+b)[i]==b[i-|a|]; }
    }
  }
  lemma RemapUnique(es:seq<Edge>,bs:seq<Binding>)
    requires Endpoints(es,Domain(bs)) && Unique(es) && Unique(Range(bs))
    ensures Unique(Remap(es,bs))
  {
    forall i,j | 0<=i<j<|es| ensures Remap(es,bs)[i]!=Remap(es,bs)[j] {
      LookupInjective(bs,es[i].src,es[j].src);
      LookupInjective(bs,es[i].dst,es[j].dst);
    }
  }
  lemma PatchGraphValidity(g:Input)
    requires FirstError(g)==0
    ensures Unique(AllIds(Apply(g,ExpectedPatch(g)).nodes))
    ensures Endpoints(Apply(g,ExpectedPatch(g)).contains+Apply(g,ExpectedPatch(g)).blocks,
      NodeIds(Apply(g,ExpectedPatch(g)).nodes))
    ensures !Cyclic(Apply(g,ExpectedPatch(g)))
    ensures Unique(ExpectedPatch(g).contains) && Unique(ExpectedPatch(g).blocks)
  {
    PatchIdsUnique(g); PatchEndpoints(g); PatchAcyclic(g);
    UniqueParts(Range(g.taskIds),Range(g.criterionIds));
    InternalExact(g.contains,Reach(g)); InternalExact(g.blocks,Reach(g));
    RemapUnique(Internal(g.contains,Reach(g)),g.taskIds);
    RemapUnique(Internal(g.blocks,Reach(g)),g.taskIds);
  }
  lemma DatesFit(g: Input)
    requires FirstError(g) == 0
    ensures forall n | n in ExpectedPatch(g).nodes :: Fits(n.start,0) && Fits(n.due,0)
  {
    var ns := Selected(g.nodes,Reach(g));
    forall i | 0 <= i < |ns|
      ensures Fits(ExpectedPatch(g).nodes[i].start,0) && Fits(ExpectedPatch(g).nodes[i].due,0)
    {}
  }
}
module CanonicalGraph {
  import opened TaskGraphModel
  function Lex(a: string,b: string): bool
    decreases |a|+|b|
  { |a|==0 || (|b|>0 && (a[0]<b[0] || (a[0]==b[0] && Lex(a[1..],b[1..])))) }
  lemma LexTotal(a:string,b:string)
    ensures Lex(a,b) || Lex(b,a)
    decreases |a|+|b|
  { if |a|>0 && |b|>0 && a[0]==b[0] { LexTotal(a[1..],b[1..]); } }
  lemma LexTrans(a:string,b:string,c:string)
    requires Lex(a,b) && Lex(b,c)
    ensures Lex(a,c)
    decreases |a|+|b|+|c|
  { if |a|>0 && |b|>0 && |c|>0 && a[0]==b[0]==c[0] { LexTrans(a[1..],b[1..],c[1..]); } }
  predicate Ordered<T>(s:seq<T>, key:T->string)
  { forall i,j | 0<=i<j<|s| :: Lex(key(s[i]),key(s[j])) }
  method Insert<T(==)>(x:T,s:seq<T>,key:T->string) returns(r:seq<T>)
    requires Ordered(s,key)
    ensures Ordered(r,key)
    ensures multiset(r)==multiset(s)+multiset{x}
    ensures |r|==|s|+1
    ensures forall y :: y in r <==> y==x || y in s
    decreases |s|
  {
    if |s|>0 { assert s==[s[0]]+s[1..]; }
    if |s|==0 || Lex(key(x),key(s[0])) {
      r:=[x]+s;
      if |s|>0 {
        forall j | 0<=j<|s| ensures Lex(key(x),key(s[j])) {
          if j>0 { LexTrans(key(x),key(s[0]),key(s[j])); }
        }
      }
    } else {
      LexTotal(key(x),key(s[0]));
      var tail:=Insert(x,s[1..],key);
      r:=[s[0]]+tail;
      forall y | y in tail ensures Lex(key(s[0]),key(y)) {
        assert forall j | 0<=j<|s[1..]| :: s[1..][j]==s[j+1] && Lex(key(s[0]),key(s[1..][j]));
      }
    }
  }
  method Sort<T(==)>(s:seq<T>,key:T->string) returns(r:seq<T>)
    ensures Ordered(r,key)
    ensures multiset(r)==multiset(s)
    ensures |r|==|s|
    ensures forall y :: y in r <==> y in s
    decreases |s|
  {
    if |s|==0 { r:=[]; }
    else { assert s==[s[0]]+s[1..]; var tail:=Sort(s[1..],key); r:=Insert(s[0],tail,key); }
  }
  function NodeKey(n:Node):string { n.id }
  function CriterionKey(c:Criterion):string { c.id }
  function EdgeKey(e:Edge):string { e.src+"!"+e.dst }
  predicate SameNode(a:Node,b:Node) {
    a.id==b.id && a.payload==b.payload && a.start==b.start && a.due==b.due &&
    a.status==b.status && a.repeatRule==b.repeatRule && a.history==b.history &&
    multiset(a.criteria)==multiset(b.criteria)
  }
  method OrderCriteria(ns:seq<Node>) returns(r:seq<Node>)
    ensures |r|==|ns|
    ensures forall i | 0<=i<|ns| :: SameNode(r[i],ns[i]) && Ordered(r[i].criteria,CriterionKey)
    decreases |ns|
  {
    if |ns|==0 { r:=[]; }
    else {
      var c:=Sort(ns[0].criteria,CriterionKey);
      var tail:=OrderCriteria(ns[1..]);
      var n:=ns[0];
      r:=[Node(n.id,n.payload,n.start,n.due,n.status,c,n.repeatRule,n.history)]+tail;
    }
  }
  method Wire(p:Patch) returns(r:Patch)
    ensures Ordered(r.nodes,NodeKey) && Ordered(r.contains,EdgeKey) && Ordered(r.blocks,EdgeKey)
    ensures forall n | n in r.nodes :: Ordered(n.criteria,CriterionKey)
    ensures |r.nodes|==|p.nodes|
    ensures forall n | n in r.nodes :: exists o | o in p.nodes :: SameNode(n,o)
    ensures forall o | o in p.nodes :: exists n | n in r.nodes :: SameNode(n,o)
    ensures multiset(r.contains)==multiset(p.contains) && multiset(r.blocks)==multiset(p.blocks)
  {
    var withCriteria:=OrderCriteria(p.nodes);
    var ns:=Sort(withCriteria,NodeKey);
    var cs:=Sort(p.contains,EdgeKey);
    var bs:=Sort(p.blocks,EdgeKey);
    r:=Patch(ns,cs,bs);
    forall i | 0<=i<|withCriteria|
      ensures withCriteria[i] in ns && p.nodes[i] in p.nodes && SameNode(withCriteria[i],p.nodes[i])
    {}
  }
}
