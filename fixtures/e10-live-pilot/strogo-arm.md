Target filename: `candidate.strogo`.

Return one block in the following restricted notation. Every local is declared before use. Types are `long` and `bool`. Inputs are available only as `input.resourceAvailable` and `input.requestedQuantity`. Allowed expressions are identifiers, `true`, `false`, signed Int64 literals with an `L` suffix, `<=`, `==`, boolean `&`, boolean `|`, unary `!`, `checked(a + b)`, `checked(a - b)`, and `Select(predicate, whenTrue, whenFalse)`. Operands of operators must be previously declared local identifiers. The final statement is exactly a labeled tuple return with labels `accepted`, `available`, and `reserved`.

General shape:

```
{
  long available = input.resourceAvailable;
  long quantity = input.requestedQuantity;
  long zero = 0L;
  // additional typed locals using only the operations above
  return (accepted: some_bool, available: some_long, reserved: some_long);
}
```

Comments are shown only in this explanation and are forbidden in the returned source. Do not return C#, JSON program graphs, Markdown fences, or explanatory text inside `candidateSource`.
