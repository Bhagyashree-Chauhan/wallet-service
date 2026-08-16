# Domain Design Notes — Session 3 (EF Core Persistence)

Continuation of `domain-design-notes.md` and `domain-design-notes-addendum.md`.
Covers moving the domain model into EF Core: `DbContext`, Fluent API
configuration, materialization constructors, foreign keys, relationship
cardinality, and owned types.

---

## 1. Where persistence code lives

`DbContext`, EF Core packages, and all configuration classes live in
**`Wallet.Api`**, never in `Wallet.Domain` — direct consequence of the
Dependency Rule established at project scaffolding. `Wallet.Domain` stays
framework-agnostic; `DbContext` is infrastructure, and infrastructure sits at
the edge.

**Term: Clean / Onion Architecture.** In larger systems this is often split
further into a dedicated `Wallet.Infrastructure` project sitting between
`Domain` and `Api` — concentric layers, dependencies always pointing inward.
Folding infrastructure into `Api` directly is a reasonable simplification at
this project's size.

---

## 2. Core EF Core vocabulary

| Term | Meaning |
|---|---|
| **ORM** | Object-Relational Mapper — translates between C# objects and relational rows |
| **`DbContext`** | Represents a session with the database; tracks loaded/changed objects, turns `SaveChanges()` into SQL |
| **`DbSet<T>`** | Represents one table, queryable like a C# collection |
| **Npgsql** | The low-level PostgreSQL driver for .NET; `EntityFrameworkCore.PostgreSQL` is EF Core's adapter on top of it |
| **Fluent API** | Code-based mapping via chained method calls (`.HasKey()`, `.Property()`, `.OwnsOne()`), used instead of `[Attribute]` annotations specifically to keep EF Core concerns out of `Wallet.Domain` |
| **`IEntityTypeConfiguration<T>`** | One class per entity holding its mapping rules, keeping `DbContext` itself from becoming a dumping ground |
| **`ApplyConfigurationsFromAssembly`** | One-line call in `OnModelCreating` that finds and applies every configuration class automatically |

---

## 3. Materialization — why some entities need a private constructor

**Term: materialization.** EF Core's process of turning a database row back
into a C# object.

The public constructor exists for **creating new domain objects**, enforcing
business rules (guard clauses, generating `Id`/`CreatedAt`). Materialization is
different — it's **restoring an already-validated fact** from storage, not
asserting a new one. Re-running validation, or regenerating `Id`/`CreatedAt`,
on load would be wrong: it would either reject legitimately old data or silently
overwrite the real stored identity/timestamp with new values.

**Constructor binding vs. private constructor — confirmed by testing, not
assumed:**

- `Wallet` and `LedgerTransaction` materialize fine through their **existing
  public constructors** — EF can bind scalar constructor parameters
  (`Guid`, `string`, enums) directly by name.
- `LedgerEntry` required a **private parameterless constructor** — EF Core
  cannot bind an **owned type** (`Money Amount`) through constructor
  parameters. It needs the object to exist first, then populates owned types
  via reflection afterward.

```csharp
private LedgerEntry()
{
    Amount = null!;
}
```

**On the `null!`:** the null-forgiving operator suppresses the compiler's
non-nullable warning. It's safe specifically because this constructor is only
ever invoked by EF's materialization machinery, which immediately overwrites
`Amount` via reflection before any other code can observe the object. This is
the standard, accepted idiom across EF Core + nullable-reference-types
codebases — a deliberate, narrowly-scoped exception to normal null safety, not
a loophole.

**General principle:** validation belongs at the point of creation/mutation,
not at the point of retrieval. A database row is a validated fact; loading it
is a re-reading of that fact, not a new assertion of it.

**This was verified, not assumed** — by building a throwaway scratch project
referencing the real domain/API projects, using EF Core's **InMemory
provider** (a fake, RAM-only database used purely for this kind of check) to
actually materialize objects and confirm the mapping worked, before running
any real migration against Postgres.

---

## 4. Backing fields — persisting an encapsulated collection

`LedgerTransaction.Entries` is `IReadOnlyList<LedgerEntry>` with no public
setter — deliberately, to prevent external code from mutating the collection
directly. EF Core still needs to populate it during materialization.

**Term: backing field configuration.** Tells EF Core to bypass the property
entirely and write directly into the private field via reflection:

```csharp
builder.Navigation(t => t.Entries)
    .HasField("_entries")
    .UsePropertyAccessMode(PropertyAccessMode.Field);
```

This is EF Core being *explicitly and intentionally* told to bypass
encapsulation, through reflection, while ordinary application code still only
ever sees the read-only interface. The alternative — making the setter public
"because EF needs it" — would quietly destroy the invariant the whole
encapsulation exercise was built to protect.

---

## 5. Foreign keys and relationship cardinality

**The single rule, always true, no exceptions:**

> Whichever table physically stores the foreign key column is the "many"
> side. The table it points to is the "one" side.

**Why it's always true:** a foreign key column holds exactly one value per
row, but nothing stops *multiple rows* from repeating the same value in that
column. The table where that value can repeat is, by definition, the "many"
side. The table it points to has a primary key, which can never repeat — the
"one" side.

**Worked example — `ledger_entries` and `ledger_transactions`:**

```
ledger_transactions:  id (PK) — cannot repeat — "one" side
ledger_entries:       transaction_id (FK) — can repeat across rows — "many" side
```

Two entries can carry the same `transaction_id` (`t_991`); that repeatability
*is* the entire mechanism by which "many entries belong to one transaction" is
recorded in a relational table.

**`ledger_entries` is the "many" side of two independent relationships at
once** — it holds two separate foreign keys:

| FK column | Points to | Relationship |
|---|---|---|
| `transaction_id` | `ledger_transactions.id` | many entries → one transaction |
| `wallet_id` | `wallets.id` | many entries → one wallet |

---

## 6. Navigation properties are optional — they are not the relationship

**Core distinction, easy to conflate:**

- The **foreign key** is a database-level fact. It fully defines the
  relationship (its existence and its cardinality) regardless of any C# code.
- A **navigation property** is an optional, code-only convenience for
  traversing that relationship without hand-writing a query. It changes
  nothing about the schema or cardinality.

**Proof, using the actual project:**

- `LedgerTransaction ↔ LedgerEntry` — **one** navigation (`Entries`, on the
  transaction side only). Still a fully valid one-to-many.
- `Wallet ↔ LedgerEntry` — **zero** navigations either direction. Still a
  fully valid one-to-many — the exact same cardinality, just without the
  C# convenience. The generated database schema is identical either way.

**Syntax tell, spotted directly in the config files:**

```csharp
// No navigation exists — type argument / empty parens instead of a lambda:
builder.HasOne<Wallet.Domain.Wallet>()
    .WithMany()

// If a navigation existed, it would instead read:
builder.HasOne(e => e.Wallet)
    .WithMany(w => w.Entries)
```

Empty parens or a generic type argument in place of a lambda is the fast way
to tell, just from the config file, whether a relationship is unidirectional
without needing to open both domain classes.

**Why `LedgerEntry` has no navigation back to `Wallet` or `LedgerTransaction`:**
no business logic in the domain layer ever needs to traverse in that
direction. Adding a navigation that's never used is unnecessary coupling —
extra surface area EF tracks, and data that could accidentally get loaded, for
no actual benefit. This is **YAGNI** (You Aren't Gonna Need It) applied
specifically to relationship modeling.

---

## 7. `DeleteBehavior.Restrict` — protecting immutability at the database level

```csharp
builder.HasMany(t => t.Entries)
    .WithOne()
    .HasForeignKey(e => e.TransactionId)
    .OnDelete(DeleteBehavior.Restrict);
```

**Term: cascade delete.** EF Core's common default on a required relationship
— deleting the parent automatically deletes its children too.
`DeleteBehavior.Restrict` instead **refuses the delete entirely** if any
child rows still reference the parent.

**Why `Restrict` is the only correct choice here, tying directly back to
ADR 0001:** the ledger is immutable and append-only. `Restrict` makes
accidental deletion of audit history a database-level impossibility, not just
an application-level convention someone could bypass with a careless script or
migration.

---

## 8. Many-to-many, derived from the same rule — not a separate concept

Worked through with a hypothetical (`Wallet` with multiple `User` owners):

A single FK column can only hold one value. If a wallet has two owners, no
single column on either `Wallet` or `User` can represent both relationships
at once. The relationship needs its own table.

**Term: join table** (also called link table / associative table / junction
table).

```
wallet_owners
├── wallet_id  (FK → wallets.id)
└── user_id    (FK → users.id)
```

**Key insight:** a many-to-many relationship is just **two one-to-many
relationships sharing a middle table** — not a new mechanism, a composition of
the same rule applied twice. `wallet_owners` is the "many" side of both
`wallets → wallet_owners` and `users → wallet_owners` simultaneously.

A composite unique constraint on `(wallet_id, user_id)` prevents the same pair
being recorded twice. If the join itself needs extra data (e.g. a `role`
column), it becomes an explicit domain entity rather than an EF-generated
implicit join table.

---

## 9. Value objects don't get their own table — `OwnsOne`

**Term: Owned Entity Type / `OwnsOne`.** Maps a value object's fields as
flattened columns directly on the owning entity's table, rather than creating
a separate table with its own foreign key.

```csharp
builder.OwnsOne(e => e.Amount, amount =>
{
    amount.Property(m => m.MinorUnits).HasColumnName("amount_minor_units");
    amount.Property(m => m.Currency).HasColumnName("amount_currency");
});
```

Result — one row, no join required to read the amount:

```
ledger_entries: id | transaction_id | wallet_id | amount_minor_units | amount_currency
```

**Why this is a different mechanism from foreign keys, not a variant of one:**
`Money` has no primary key and no independent identity — it never exists
except as part of a `LedgerEntry`. This is the same **entity vs. value
object** distinction from Session 1, now expressed as a persistence rule:

> Does the type have identity independent of its owner? If two instances with
> identical data are genuinely interchangeable, it's a value object — columns
> on the owner, never its own table.

---

## 10. Enum storage — int vs. string, and why fintech leans string

`LedgerTransaction.Type` is a C# `enum`. EF Core's default stores it as its
underlying **integer** ordinal (`Credit = 0`, `Debit = 1`, ...).

**The size argument, checked and found not decisive:** `int` = 4 bytes,
`text` ≈ 12–16 bytes — roughly 10 bytes/row difference. At 100 million rows,
about 1 GB total across the whole table — irrelevant on modern storage, far
cheaper than a single incident caused by a misread ordinal.

**Why string wins in a financial ledger specifically:**

1. **Regulatory/audit access** — compliance tooling, BI tools, and data
   warehouse exports query the raw table directly, outside the application
   layer. `type = 1` is meaningless without the C# enum definition in hand;
   `type = 'Debit'` is self-describing to any consumer, forever.
2. **Cross-service/cross-team drift** — once more than one system reads the
   table, the enum's meaning must travel with the data itself, not live only
   in one codebase.
3. **Money-specific caution culture** — a misread ordinal in a ledger context
   risks a materially worse failure (debit read as credit) than in most other
   domains.

**Decision applied:**

```csharp
builder.Property(t => t.Type)
    .HasColumnName("type")
    .HasConversion<string>()
    .IsRequired();
```

**Interview framing:** *"depends whether the table is ever read outside the
application boundary — audit tools, other services, data warehouses. If yes,
string; if truly internal-only and performance-critical, int is defensible."*
A conditional judgment, not a memorized rule.

---

## 11. `ValueGeneratedNever()` — a real bug this specifically prevents

```csharp
builder.Property(t => t.Id)
    .HasColumnName("id")
    .ValueGeneratedNever();
```

By default, EF assumes a `Guid` primary key should be generated by the
database or by EF itself on insert. But `Id = Guid.NewGuid()` already happens
inside the domain constructor, before the object ever reaches EF.
`ValueGeneratedNever()` tells EF not to touch it.

**Concrete failure this prevents:** without this line, EF could generate its
own `Id` and silently overwrite the one the constructor already assigned —
producing a different `Id` than the one already referenced elsewhere (e.g. in
a `LedgerEntry.TransactionId` created moments earlier from the same object).

---

## 12. Result

- `WalletDbContext` and three `IEntityTypeConfiguration<T>` classes created
  for `Wallet`, `LedgerTransaction`, `LedgerEntry` — Fluent API only, zero EF
  annotations on domain types.
- Unique index on `IdempotencyKey` — ADR 0002 made physical.
- `Entries` backing-field configuration — encapsulation preserved for
  application code, bypassed intentionally for EF materialization only.
- `Amount` mapped via `OwnsOne` — no separate `money` table.
- `Type` stored as string via `HasConversion<string>()` — audit-friendly by
  deliberate choice, not left at the int default.
- `DeleteBehavior.Restrict` on both foreign keys in `ledger_entries` —
  deletion of ledger history is a database-level impossibility.
- Mapping verified by materializing entities against EF Core's InMemory
  provider in a throwaway scratch project, before touching real Postgres.

---

## 13. Open thread for next session

Run the first real EF Core migration against Postgres (via Docker) — turning
these four configuration classes into actual tables for the first time.
