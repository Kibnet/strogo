Target filename: `Candidate.cs`.

Return ordinary C# source defining exactly the public static candidate entry point below. `CandidateResult` is supplied by the trusted runner; do not redefine it.

```
public static class Candidate
{
    public static CandidateResult Execute(long resourceAvailable, long requestedQuantity)
    {
        // implementation
    }
}
```

The method is called only after host preconditions have accepted the inputs. It must be deterministic and pure: no console, file, network, process, environment, clock, randomness, reflection, thread, task, or mutable static-state access. Return a `CandidateResult` whose constructor arguments are `(bool Accepted, long Available, long Reserved)`. Do not return Markdown fences or explanatory text inside `candidateSource`.
