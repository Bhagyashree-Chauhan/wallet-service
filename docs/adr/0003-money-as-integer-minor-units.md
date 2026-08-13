# ADR 0003 — Money as integer minor units with explicit currency

**Status:** Accepted

## Context

Binary floating point cannot represent most decimal fractions. In .NET:

```csharp
0.1 + 0.2 == 0.3   // false
```

Across millions of operations the error accumulates into real, unexplainable
discrepancies. Separately, an amount without a currency is meaningless: `100` is not a
quantity of money until you know whether it is fils, cents or yen.

## Decision

- Amounts are stored and transported as `long`, counting **minor units** — fils for AED,
  cents for USD.
- Currency travels with the amount as an ISO 4217 code. A `Money` value object in
  `Wallet.Domain` carries both.
- Arithmetic between different currencies throws. There is no implicit conversion.
- The database column is `BIGINT`, never `FLOAT` or `REAL`.
- Formatting into a human-readable decimal happens at the presentation edge only.

## Consequences

**Gains**

- Exact arithmetic. Addition and subtraction of integers are lossless.
- The zero-sum ledger invariant becomes exactly checkable rather than
  checkable-within-epsilon.
- Mixed-currency bugs fail loudly at the type level instead of silently producing
  nonsense.
- Integers serialise identically across JSON, Protobuf and every client language, which
  matters once other services consume this API.

**Costs**

- Callers must send `5000`, not `50.00`. This must be prominent in the API docs.
- Currencies with other exponents (JPY has zero decimal places, KWD has three) need an
  exponent lookup rather than a hardcoded factor of 100.
- `long` caps near 9.2 × 10^18 minor units. Ample, but worth stating rather than assuming.

## Alternatives considered

**`decimal`.** It is base-10 and would be correct arithmetically. Rejected as the wire
and storage format because serialisation across languages is inconsistent and it invites
silent rounding at boundaries. Integers admit no ambiguity.

**`double`.** Rejected outright. Unsuitable for money under any circumstances.
