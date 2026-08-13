# Wallet Service

A ledger-backed wallet API built with .NET 9, EF Core and PostgreSQL.

It answers two questions safely, under concurrency and network retries:

- How much does a wallet hold?
- Can I move value in, out, or between wallets without ever creating or destroying money?

---

## Status

| Milestone | State |
|---|---|
| 1. Project scaffold | done |
| 2. Ledger schema + balance derivation | pending |
| 3. Credit / debit with idempotency | pending |
| 4. Concurrency safety | pending |
| 5. Atomic transfers | pending |
| 6. Outbox + reconciliation | pending |

---

## Scope

**In scope**

- `credit` — money in (top-up, refund)
- `debit` — money out (purchase, withdrawal)
- `transfer` — atomic movement between two wallets
- `getBalance` — current balance and statement

**Out of scope (deliberately)**

Authentication, KYC, currency conversion, payment-gateway integration. Each is a
separate bounded context. Keeping them out keeps the correctness story provable.

---

## Architecture

```
                 ┌──────────────────┐
   HTTP ────────▶│   Wallet.Api     │  endpoints, EF Core, Postgres
                 └────────┬─────────┘
                          │ depends on
                          ▼
                 ┌──────────────────┐
                 │  Wallet.Domain   │  entities, invariants, no frameworks
                 └──────────────────┘
                          ▲
                          │
                 ┌──────────────────┐
                 │  Wallet.Tests    │
                 └──────────────────┘
```

**The Dependency Rule.** Dependencies point inward, toward the domain. `Wallet.Domain`
references no web framework, no ORM, no database driver. The rule is what makes the
invariant "a wallet may not go negative" testable in milliseconds without Docker.

| Project | Responsibility | Depends on |
|---|---|---|
| `Wallet.Domain` | Ledger entities, money type, business invariants | nothing |
| `Wallet.Api` | HTTP surface, persistence, composition root | Domain |
| `Wallet.Tests` | Unit and integration tests | both |

---

## Core model

The ledger is the source of truth. Balance is a **projection** derived from it, never
an independently mutable field.

```
transaction  t_991   idempotency_key = "order-123"
  ├─ entry  wallet_ali    -50
  └─ entry  wallet_sara   +50
                          ───
                            0   ← invariant: entries of a transaction sum to zero
```

A transfer is one transaction with two entries. A credit is one transaction with an
entry against a system account. Every row is append-only; corrections are new
reversing entries, never edits.

---

## Design decisions

Full rationale for each lives in [`docs/adr/`](docs/adr).

| ADR | Decision | Distributed-systems failure it defeats |
|---|---|---|
| [0001](docs/adr/0001-immutable-double-entry-ledger.md) | Immutable double-entry ledger as source of truth | Lost updates, unauditable balances |
| [0002](docs/adr/0002-idempotency-keys.md) | Idempotency key required on every mutating request | At-least-once delivery causing duplicate money movement |
| [0003](docs/adr/0003-money-as-integer-minor-units.md) | Money stored as `long` minor units with explicit currency | Floating-point drift and rounding loss |
| [0004](docs/adr/0004-concurrency-control.md) | Pessimistic row lock on the wallet during debit | Race condition allowing overdraft |
| [0005](docs/adr/0005-transactional-outbox.md) | Transactional outbox for event publication | Dual-write problem losing events |
| [0006](docs/adr/0006-no-two-phase-commit.md) | Single local transaction now, sagas later — never 2PC | Coordinator failure blocking on held locks |

---

## Getting started

### Prerequisites

```bash
dotnet --version   # 9.x
docker -v
git --version
```

### Build

```bash
git clone <your-repo-url> wallet-service
cd wallet-service
dotnet build
```

### Run

```bash
docker compose up -d          # Postgres
dotnet run --project src/Wallet.Api
```

### Test

```bash
dotnet test
```

---

## Repository layout

```
wallet-service/
├── src/
│   ├── Wallet.Api/          HTTP endpoints, EF Core, migrations
│   └── Wallet.Domain/       entities, money type, invariants
├── tests/
│   └── Wallet.Tests/        unit + integration tests
├── docs/
│   └── adr/                 architecture decision records
├── WalletService.sln
└── README.md
```

---

## Conventions

- Every architectural decision gets an ADR before the code that implements it.
- No `UPDATE` on balances. Ever. New rows only.
- Every mutating endpoint requires an `Idempotency-Key` header.
- Amounts crossing a process boundary carry their currency alongside them.
