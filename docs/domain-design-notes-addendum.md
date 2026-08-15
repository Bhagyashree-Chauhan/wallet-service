# Domain Design Notes — Session 2 (Aggregate Root + Testing)

Continuation of `domain-design-notes.md`. Covers finishing the `LedgerTransaction`
aggregate root and the first xUnit test suite.

---

## 1. The reference-leak trap

A property with no public setter is **not** the same as a property that can't be
mutated from outside the class — if its type is a mutable collection.

```csharp
public List<LedgerEntry> Entries { get; } // no setter, but still unsafe

txn.Entries.Add(someEntry); // compiles, bypasses every guard clause
```

No setter was touched. The internal list itself was mutated directly through the
reference the getter handed out. This is a **broken encapsulation via reference
leak** — locking the front door while handing out a key through the window.

**Fix — expose a read-only view, keep the real list private:**

```csharp
private readonly List<LedgerEntry> _entries = new();
public IReadOnlyList<LedgerEntry> Entries => _entries;
```

`IReadOnlyList<T>` has no `.Add` / `.Remove` — mutation through that reference is a
compile error, not a runtime bug. Internally, `_entries` remains a normal mutable
list the owning class can freely modify.

**General rule:** never expose an internal mutable collection directly. Expose a
read-only interface over it; mutate only through methods the class controls.

---

## 2. Transient state vs. finalized state

Not every invariant is checked on every mutation. Some only make sense at a
defined "commit point."

Tracing why the zero-sum check can't live inside `AddEntry`:

```csharp
txn.AddEntry(aliEntry);   // sum = -5000 here — not zero, and that's fine, mid-build
txn.AddEntry(saraEntry);  // sum = 0 now
```

If `AddEntry` demanded a zero sum after every call, the very first call would
always throw — no transaction could ever be built one entry at a time. The
zero-sum check has to live in a separate method invoked once the caller
deliberately signals completion:

| Method | When it runs | What it checks |
|---|---|---|
| `AddEntry` | Once per entry, as each arrives | Per-entry checks: belongs to this transaction, currency consistent with entries already added |
| `Post` | Once, when the caller is done adding entries | Whole-group check: all entries sum to zero |

**General principle:** check as early as possible, at the point where you have
exactly the information needed — no earlier, no later. Per-entry checks belong on
`AddEntry` because that's the only place currency mismatches can be caught *before*
a bad entry is admitted. The zero-sum check belongs on `Post` because it requires
seeing the whole group, which no single `AddEntry` call has.

---

## 3. The finished `LedgerTransaction` (aggregate root)

```csharp
public class LedgerTransaction
{
    private readonly List<LedgerEntry> _entries = new();
    public IReadOnlyList<LedgerEntry> Entries => _entries;
    public Guid Id { get; private set; }
    public string IdempotencyKey { get; private set; }
    public TransactionType Type { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public LedgerTransaction(string idempotencyKey, TransactionType type)
    {
        Id = Guid.NewGuid();
        IdempotencyKey = idempotencyKey;
        Type = type;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void AddEntry(LedgerEntry entry)
    {
        if (entry.TransactionId != Id)
            throw new InvalidOperationException("Entry transaction ID does not match the transaction ID.");

        if (_entries.Count > 0 && _entries[0].Amount.Currency != entry.Amount.Currency)
            throw new InvalidOperationException("Cannot add entries with different currencies.");

        _entries.Add(entry);
    }

    public void Post()
    {
        if (_entries.Count == 0)
            throw new InvalidOperationException("Cannot post a transaction with no entries.");

        var sum = _entries.Sum(e => e.Amount.MinorUnits);
        if (sum != 0)
            throw new InvalidOperationException("Cannot post a transaction with unbalanced entries.");
    }
}
```

Note `Type` stays a real `TransactionType` enum all the way through — resisting
the earlier instinct to convert it to `string` at construction. That conversion,
if needed at all, belongs in the persistence layer (EF Core can map an enum to a
string or int column directly), not in the domain type.

The `_entries.Count == 0` empty-transaction guard in `Post()` was added
independently — an empty transaction trivially sums to zero (vacuous truth), so
without the guard a completely empty transaction would pass silently.

---

## 4. Testing fundamentals — first exposure

A test is ordinary code you write, calling your own code, checking the result
automatically instead of by hand in a debugger.

**Vocabulary introduced:**

| Term | Meaning |
|---|---|
| `[Fact]` | Attribute marking a method as a test xUnit should run |
| `Arrange-Act-Assert (AAA)` | Standard test structure: set up objects, call the method under test, check the outcome |
| `Assert.Throws<TException>(() => ...)` | Asserts that calling the wrapped lambda throws exactly `TException` |
| Lambda `() => ...` | An inline, unnamed function — used here to **defer** execution so `Assert.Throws` controls when the call happens, rather than it running immediately and crashing the test |

**Why the lambda wrapping matters**, traced explicitly:

```csharp
Assert.Throws<InvalidOperationException>(txn.Post());     // wrong — Post() runs immediately,
                                                             // throws before Assert.Throws gets control
Assert.Throws<InvalidOperationException>(() => txn.Post()); // right — Assert.Throws decides when
                                                              // to invoke it, inside its own try/catch
```

A method with no `[Fact]`-triggered assertion at all still counts as passing if it
completes without throwing — you only need an explicit `Assert` when checking a
*value*, not merely "did this crash."

**Naming convention used:** `MethodUnderTest_Scenario_ExpectedResult` —
e.g. `Post_WithUnbalancedEntries_Throws`. Reads as a sentence describing exactly
what's being verified.

---

## 5. The four tests written

1. **Happy path** — two balanced entries, same currency, `Post()` does not throw.
2. **Unbalanced sum** — entries that don't sum to zero; `Post()` throws.
   Confirms the whole-group check lives on `Post`, not `AddEntry`.
3. **Currency mismatch** — second entry in a different currency; `AddEntry` throws
   on the *second* call specifically, not `Post`. Only that one call is wrapped in
   `Assert.Throws` — the first `AddEntry` call runs unwrapped, keeping the test
   unambiguous about which call is expected to fail.
4. **Wrong transaction ID** — a `LedgerEntry` constructed with an unrelated
   `Guid.NewGuid()` as its `TransactionId`; `AddEntry` throws. Simpler than
   constructing a second real `LedgerTransaction` just to get a mismatched id —
   any unrelated guid triggers the same guard clause.

All four wrap only the single call expected to throw — never multiple calls in
one `Assert.Throws`, since that would leave the test unable to say which call
actually failed.

**Term: guard clause** — a check at the top of a method that rejects invalid
input before any real work happens. `AddEntry`'s transaction-id and currency
checks are both guard clauses; tests 3 and 4 exist specifically to prove each one
actually guards.

---

## 6. Result

- `LedgerTransaction` aggregate root complete: encapsulated collection, per-entry
  guard clauses on `AddEntry`, whole-group invariant on `Post`.
- 4 passing xUnit tests in `Wallet.Tests/LedgerTransactionTests.cs`, covering the
  happy path and every guard clause.
- Domain layer (`Money`, `Wallet`, `LedgerTransaction`, `LedgerEntry`) is now
  complete and test-covered — no database, no web framework touched yet.

---

## 7. Open thread for next session

Persistence: EF Core mapping these domain types to Postgres tables, and the first
migration. This is the boilerplate-heavy layer earmarked for Claude Code — output
to be reviewed critically, not hand-typed line by line like the domain layer was.
