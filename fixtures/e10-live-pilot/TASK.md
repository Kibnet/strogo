Implement a deterministic reservation operation for signed 64-bit integer inputs.

Inputs:
- `resourceAvailable: Int64`
- `requestedQuantity: Int64`

The host rejects `resourceAvailable < 0` with `StateInvariantFailed` and rejects `requestedQuantity <= 0` with `PreconditionFailed` before calling your candidate. Your candidate only receives admissible inputs.

Rules for admissible inputs:
1. Accept if and only if `requestedQuantity <= resourceAvailable`.
2. When accepted, return `accepted=true`, `reserved=requestedQuantity`, and `available=resourceAvailable-requestedQuantity`.
3. When insufficient, return `accepted=false`, `reserved=0`, and `available=resourceAvailable`.
4. The operation is deterministic and performs no I/O, external-state reads, or other effects.
5. The source must implement these rules for every admissible Int64 input.

Public examples:
- `(10, 3)` -> `(true, 7, 3)`
- `(3, 5)` -> `(false, 3, 0)`
- `(0, 1)` -> `(false, 0, 0)`
- `(9223372036854775807, 1)` -> `(true, 9223372036854775806, 1)`
