# Domain Design Notes — Session 1

Consolidated learning from building `Wallet.Domain` by hand. Covers the reasoning
behind the ledger model, the six ADRs, and the recurring design principles used
while writing `Money`, `Wallet`, `LedgerTransaction`, and `LedgerEntry`.

---

## 1. The core model

**Ledger over balance column.** Balance is never stored — it's derived:
`SUM(entries)`. One source of truth, always self-consistent.

```
transactions: 1 row per business event (has idempotency_key, UNIQUE)
entries:      2+ rows per transaction, signed amounts, sum to zero
wallets:      identity only — no balance field, ever
```

**Double-entry example.** Ali → Sara, 50 AED = 1 transaction, 2 entries:

```
transactions:  id=t_991, idempotency_key='order-123', type='transfer'

entries:  wallet_ali   amount = -5000
          wallet_sara  amount = +5000
                       ─────────────
                                  0
```

Amounts are always minor units (fils, cents) — never floats, never major units.

---

## 2. The six ADRs → six failure modes

| Decision | Defeats |
|---|---|
| Immutable double-entry ledger | Lost updates, no audit trail |
| Idempotency key (UNIQUE constraint, same transaction as the effect) | At-least-once delivery → duplicate charges |
| Money as `long` minor units + explicit currency | Float drift, currency confusion |
| Row lock (`FOR UPDATE`) before reading balance | Race condition → overdraft |
| Transactional outbox | Dual-write problem (DB commits, event lost) |
| Sagas, never two-phase commit | Coordinator blocking, held locks across services |

**The cache rule** (came up debating Redis in front of the idempotency table):

> A cache may only be consulted where a wrong answer is harmless.

Redis may say "seen → reject" — a false reject just falls through to Postgres
anyway. Redis may never be trusted to say "new → proceed" as the sole check. The
durable unique constraint is always the authority; anything else is an optimisation,
never a guarantee.

---

## 3. Where does a validation rule live?

The single biggest recurring lesson from this session:

> **An object can only enforce invariants over data it can see.**

| Rule | Lives where | Why |
|---|---|---|
| Currency must match on `Add` | `Money` itself | Both currencies are visible to the method |
| Balance must stay ≥ 0 | Domain service, inside the locked transaction | Needs the wallet's current balance — external state, invisible to `Money` alone |
| Entries of a transaction sum to zero | `LedgerTransaction` (the **aggregate root**) | A single `LedgerEntry` cannot see its siblings |

**Aggregate root** (DDD term): the one entity responsible for invariants that span
multiple child objects. External code mutates children *through* the root, never
by constructing them independently and hoping they stay consistent.

---

## 4. Entity vs. Value Object

| | Compared by | Mutable after creation? | Example |
|---|---|---|---|
| **Value object** | all fields (structural equality) | No | `Money` — `record` |
| **Entity** | `Id` alone (identity) | Data can change, identity can't | `Wallet`, `LedgerTransaction` — `class` |

**Why `record` is wrong for `Wallet`.** A record's generated `Equals` compares
every property. Two loads of the same wallet with a different `CreatedAt` read
would compare as *not equal* — backwards for an identity-based type. Entities
should be compared by `Id`, not by their current data.

---

## 5. Construction patterns used throughout

- **Private setters + all validation in the constructor.** An object can never
  exist in an invalid state — "protecting the invariant at construction."
- **Pass objects, not unpacked primitives.** `Add(Money other)`, not
  `Add(long minorUnits, string currency)` — prevents mismatched-field bugs
  (e.g. wallet A's currency paired with wallet B's amount) that the type system
  can't catch when fields are passed loose. Related term: **primitive obsession**
  — using raw primitives where a small type would enforce a relationship for you.
- **`enum` over raw `string`** for closed, small, domain-owned value sets
  (`TransactionType`). Turns a typo (`"trasnfer"`) from a silent runtime bug into
  a compile-time error.
- Fields the caller shouldn't control (`Id`, `CreatedAt`) are generated **inside**
  the constructor, never accepted as parameters.

---

## 6. Two engineering judgment calls

**`long` overflow in `Money.Add`.** Evaluated the actual magnitude —
`long.MaxValue` in fils is roughly 92 quintillion AED, about 10 orders of
magnitude past any realistic balance (UAE GDP is ~10¹² AED). Consciously chose
**not** to guard against it now. The lesson: "do you handle X?" isn't a yes/no —
it's reasoning about probability and cost before deciding whether X is worth
handling at all.

**`DateTime` vs `DateTimeOffset`.** Always `DateTimeOffset` in a system that may
span timezones or servers. Plain `DateTime` doesn't carry enough information to
be unambiguous — `2026-08-15 10:00` could be UTC, local, or server time, and
nothing in the type says which.

---

## 7. Code written this session

- `Money` — immutable `record`, `long MinorUnits` + `string Currency`, allows
  negative values, `Add(Money other)` throws on currency mismatch.
- `Wallet` — `class`, `Id` / `Currency` / `CreatedAt`, no balance field, private
  setters, constructor generates `Id` and `CreatedAt` internally.
- `LedgerTransaction` — `class`, `Id` / `IdempotencyKey` / `Type` (as
  `TransactionType` enum) / `CreatedAt`, same construction pattern.
- `LedgerEntry` — `class`, `Id` / `TransactionId` / `WalletId` / `Amount` (`Money`)
  / `CreatedAt`. Deliberately has **no** zero-sum check — it can't see its
  siblings, so that invariant belongs on the aggregate root instead.

---

## 8. Tooling covered

- Solution structure enforcing the **Dependency Rule**: `Wallet.Domain` has zero
  outward references. Architecture enforced by the compiler, not a wiki page.
- Git / GitHub auth: `gh auth status`, `gh auth login`, `gh auth switch`,
  execution-policy fix for `claude.ps1` (`Set-ExecutionPolicy -Scope CurrentUser
  -ExecutionPolicy RemoteSigned`).
- Claude Code workflow going forward: domain logic is written by hand for
  learning; Claude Code is reserved for boilerplate — EF configurations,
  controllers, migrations — not yet started.

---

## 9. Open thread for next session

Wire `LedgerTransaction` to hold its `LedgerEntry` collection and enforce the
zero-sum invariant — making the aggregate boundary real in code, not just in
theory.
