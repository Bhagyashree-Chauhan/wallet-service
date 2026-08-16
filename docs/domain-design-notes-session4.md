# Domain Design Notes — Session 4 (Infrastructure, Tooling & First Migration)

Continuation of Sessions 1–3. Covers standing up Postgres in Docker, the
dependency-resolution problems hit along the way, wiring up dependency
injection, and generating the first EF Core migration.

This session was heavier on tooling than design — but most of the concepts
below are directly interview-relevant, and several came up only *because*
something broke, which makes them worth recording carefully.

---

## Part 1 — Docker

### What Docker actually is

| Term | Meaning |
|---|---|
| **Container** | A lightweight, isolated environment packaging an application with everything it needs to run. Not a full VM — it uses hardware virtualization support but shares the host kernel, so it's far lighter. |
| **Image** | A read-only template ("Postgres 16, pre-configured"). You don't install Postgres; you pull this template once and spin up containers from it. Delete the container, the image remains. |
| **Docker Hub** | A public registry of images — conceptually "npm/PyPI, but for container images". |
| **Docker Compose** | A tool for describing which containers you want and how they're configured, in one file, so you don't retype long `docker run` commands with a dozen flags. |
| **Volume** | Persistent storage that lives outside the container's lifecycle. Without one, stopping a container destroys its data — containers are ephemeral by design. |

**Why this matters practically:** when the project is done, `docker compose down`
removes Postgres from the machine completely — no leftover Windows services,
no registry entries, no background process forgotten about. And anyone cloning
the repo runs one command to get an identical database.

### The compose file, line by line

```yaml
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_USER: wallet
      POSTGRES_PASSWORD: wallet_dev_password
      POSTGRES_DB: wallet_service
    ports:
      - "5432:5432"
    volumes:
      - wallet_pgdata:/var/lib/postgresql/data

volumes:
  wallet_pgdata:
```

- `services:` — the list of containers Compose manages.
- `image: postgres:16` — which image and tag to pull. First run downloads it;
  later runs reuse the cache.
- `environment:` — variables passed into the container at startup. These three
  specific names are documented behaviour *of the official Postgres image* —
  it looks for them to auto-create a user, password and database on first boot.
  Not a Docker-wide convention.
- `ports: "5432:5432"` — **`host:container`** mapping. Traffic hitting
  `localhost:5432` on the host is forwarded into the container's `5432`. If
  something already occupied the host's 5432, you'd change only the left number
  (e.g. `"5433:5432"`).
- `volumes:` (under the service) — mounts the named volume at the exact path
  Postgres stores its data files inside the container.
- `volumes:` (top level) — declares the named volume for Docker to manage.

### Reading `docker ps` output

| Column | Example | Meaning |
|---|---|---|
| `CONTAINER ID` | `4dfe5b0886d4` | Unique id for this running instance — used to stop, inspect, or exec into it |
| `IMAGE` | `postgres:16` | Which template it was built from |
| `COMMAND` | `docker-entrypoint.s…` | The process running inside |
| `STATUS` | `Up 41 seconds` | Running, with uptime |
| `PORTS` | `0.0.0.0:5432->5432/tcp` | Confirms the host→container port mapping is live |
| `NAMES` | `wallet-service-postgres-1` | Auto-generated as `<folder>-<service>-<instance>` |

### Interview framing

> *"Why a named volume instead of default container storage?"*
> "Containers are meant to be disposable — I might rebuild or replace the
> container itself, but the data has to outlive that. A named volume decouples
> the data's lifecycle from the container's lifecycle."

> *"How do you run this project locally?"*
> "Clone the repo, `docker compose up -d`, done. No manual Postgres install."

---

## Part 2 — The infrastructure problems, and what each taught

### 2.1 WSL2 not installed

**Symptom:** `failed to connect to the docker API at npipe:...dockerDesktopLinuxEngine`,
then `wsl is not installed`.

**Concept:** Docker on Windows doesn't run containers natively — Linux
containers need a Linux kernel. Docker Desktop runs its engine inside a
lightweight **WSL2** (Windows Subsystem for Linux) VM; the Windows-side
`docker` CLI is just a client talking to that engine.

**Fix:** `wsl --install` from an admin PowerShell, then a full restart.

### 2.2 Virtualization disabled in firmware

**Symptom:** *"Docker Desktop failed to start because virtualisation support
wasn't detected."*

**Diagnosis — the useful part:** `systeminfo` has a **Hyper-V Requirements**
section:

```
VM Monitor Mode Extensions:          Yes   ← CPU supports it
Second Level Address Translation:    Yes   ← CPU supports it
Virtualization Enabled In Firmware:  No    ← but it's switched off in BIOS
```

Reading those three lines together proves it's a *firmware setting*, not a
hardware limitation — the CPU is capable, the BIOS just has it disabled.

**Fix:** BIOS → Configuration → Virtualization Technology (VTx) → Enabled.
(On the HP Spectre x360: tap `Esc` at boot, then `F10` for BIOS Setup.)

**Transferable lesson:** when a tool says "not supported", check whether the
capability exists but is disabled, before concluding the hardware can't do it.

---

## Part 3 — NuGet dependency resolution

Three distinct failures in sequence, each a different resolution concept. This
is unusually good interview material because most candidates have used NuGet
but never had to debug it.

### 3.1 Framework incompatibility (NU1202)

```
Package Microsoft.EntityFrameworkCore.Design 10.0.11 is not compatible
with net9.0. Package supports: net10.0
```

**Cause:** `dotnet add package` with no `--version` always grabs the *latest*
published version. .NET 10 packages exist; the project targets .NET 9.

**Fix:** pin explicitly with `--version`.

**Lesson:** never install EF Core packages unpinned. All packages within one
library family must share a major version.

### 3.2 Package downgrade conflict (NU1605)

```
Detected package downgrade: Microsoft.EntityFrameworkCore from 9.0.4 to 9.0.1
  Wallet.Api -> ...Design 9.0.4 -> ...Relational 9.0.4 -> ...EntityFrameworkCore (>= 9.0.4)
  Wallet.Api -> Microsoft.EntityFrameworkCore (>= 9.0.1)
```

**How to read it:** two paths through the dependency graph disagree. The direct
reference wants `9.0.1`; `.Design`'s own chain requires `>= 9.0.4`. NuGet
refuses to silently resolve a *downgrade* — it errors rather than guessing.

**Fix:** align **upward** — bump the direct reference to `9.0.4` — rather than
forcing `.Design` down.

### 3.3 Transitive version drift (MSB3277)

**Symptom:** a build *warning*, not an error:

```
Found conflicts between different versions of "EntityFrameworkCore.Relational"
9.0.1 was chosen because it was primary
```

**Term: transitive dependency** — a dependency of a dependency. `Relational`
was never referenced directly by either project; it arrived via other packages.

**Why it drifted:** NuGet resolves transitive dependencies to the **lowest
version satisfying all constraints**, not the highest. Something declared
`Relational >= 9.0.1`, nothing declared a higher floor, so 9.0.1 won — even
though every sibling EF package resolved to 9.0.4.

**Diagnosis command — the key tool:**

```powershell
dotnet list <project> package --include-transitive
```

This shows every package, including ones never explicitly added. It isolated
`Relational 9.0.1` sitting alone among a family of 9.0.4s.

**Fix — pinning a transitive dependency:** add a direct `PackageReference` to a
package you don't use directly, purely to raise its version floor. A direct
reference always overrides transitive resolution.

**Why it mattered, not just a cosmetic warning:** the test project was building
against `Relational 9.0.1` while the API compiled against `9.0.4`. Tests would
be exercising different code than the app ships — the classic route to "passes
locally, fails in production", or a runtime `MethodNotFoundException`.

### Interview framing

> *"How does NuGet resolve version conflicts?"*
> "Nearest-wins for direct references, lowest-applicable for transitive ones.
> If a transitive package resolves lower than I want, I add an explicit
> reference to pin it — a direct reference always overrides transitive
> resolution."

> *"You see MSB3277 in a build log — do you care?"*
> "Yes. Different projects resolved different versions of the same assembly and
> MSBuild silently picked one. It means my tests may not be running against the
> same code my app ships. I'd align versions rather than suppress the warning."

**Scaling note:** this class of drift is why teams adopt **central package
management** (`Directory.Packages.props` in .NET) — one file declaring every
version solution-wide so projects can't diverge. Overkill at three projects,
standard past ten.

---

## Part 4 — Design-time vs runtime packages

Three EF Core packages, three different jobs — commonly conflated:

| Package | Role |
|---|---|
| `Microsoft.EntityFrameworkCore` | The runtime library the application code compiles against |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | The database provider — translates EF's generic calls into Postgres-specific SQL |
| `Microsoft.EntityFrameworkCore.Design` | **Tooling only.** Contains what `dotnet-ef` needs to inspect the `DbContext` and generate migrations |

**Why `.Design` is separate rather than bundled:** a deployed application never
needs migration-generation code. Shipping it in the production binary would be
dead weight. This is a deliberate separation of *tooling* dependencies from
*runtime* dependencies — a pattern common well beyond .NET.

Also distinct: **`dotnet-ef`** itself is a *global CLI tool*
(`dotnet tool install --global dotnet-ef`), not a NuGet package reference. The
package is the library; the tool is the command line.

---

## Part 5 — Dependency Injection and the composition root

### The problem that surfaced it

```
Unable to create a 'DbContext' of type 'RuntimeType'.
Unable to resolve service for type 'DbContextOptions<WalletDbContext>'
```

**Root cause:** `Program.cs` was still the untouched `dotnet new webapi`
scaffold. `WalletDbContext` had never been registered with dependency
injection at all — so neither the tooling nor the running app could construct
it.

**Why it surfaced only now:** the project was deliberately built
domain → tests → EF configs, and `Program.cs` was never touched. Good ordering
for learning (invariants before infrastructure), but it meant the app was never
wired end-to-end until the tooling demanded it.

**Secondary lesson about AI-assisted work:** Claude Code generated the
`DbContext` and configurations exactly as prompted — but wiring into
`Program.cs` wasn't in the prompt, so it fell through the gap. Scoped prompts
give scoped results; integration steps need naming explicitly.

### The fix

```csharp
builder.Services.AddDbContext<WalletDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("WalletDb")));
```

| Term | Meaning |
|---|---|
| **Dependency Injection container** | A registry the framework uses to construct objects and supply them to whatever needs them. A controller declaring `WalletDbContext` in its constructor gets one built automatically — it never calls `new` itself. |
| **Composition root** | The single place where the application's object graph is wired up — `Program.cs` here. DI configuration belongs in one place, not scattered across the codebase. |
| **`DbContextOptions<T>`** | The configuration object a `DbContext` needs at construction: which provider, which connection string. Normally supplied by DI. |

### The design-time factory — and why it was NOT needed

`IDesignTimeDbContextFactory<T>` is a documented EF Core extension point for
telling `dotnet-ef` how to construct a context standalone, since the CLI runs
as a separate process that never executes `Program.cs`.

**But it turned out to be unnecessary here.** EF Core's tooling first tries to
invoke the app's own host builder to get a context through DI — which works
once registration exists. The factory is only needed when that fallback can't
work.

**The real lesson — process, not EF:** the minimal fix (registering the
`DbContext`) was sufficient, and was proven sufficient by trying it alone.
Bundling the factory in preemptively would have added permanent machinery to
the codebase to solve a problem that didn't exist. **Try the minimal fix first
and prove more is needed before adding abstraction.**

### Interview framing

> *"Singleton vs scoped vs transient?"*
> `AddDbContext` registers **scoped** by default — one instance per HTTP
> request. This is correct and important: a `DbContext` tracks changes to
> loaded entities, so sharing one across requests would leak state between
> users. Singleton would be actively dangerous; transient would break
> change-tracking within a single request.

---

## Part 6 — Secrets hygiene

`appsettings.Development.json` showed as **modified**, not untracked — meaning
it had been committed since the initial scaffold (the `dotnet new webapi`
template creates it, and .NET's generated `.gitignore` does *not* exclude it by
default). It was harmless while empty; adding a connection string changed that.

**Fix:**

```powershell
echo appsettings.*.json >> .gitignore
git rm --cached src/Wallet.Api/appsettings.Development.json
```

`git rm --cached` stops tracking the file **without deleting it from disk** —
the local file and its connection string stay intact.

**Is it serious here?** No — `wallet_dev_password` only unlocks a container on
one laptop. **But the habit is the point.** The identical carelessness with a
production connection string is exactly how real credential leaks happen.

### Interview framing

> *"How do you handle secrets and config across environments?"*
> "Environment-specific config files excluded from source control; real
> credentials injected via environment variables or a secrets manager at deploy
> time — never committed, even for 'just dev'."

---

## Part 7 — Code-first vs database-first

```
Code-first:      C# classes → migration → database schema
Database-first:  database schema → scaffold → C# classes
```

**This project is code-first** — the domain model was built, tested and proven
correct entirely in C# across Sessions 1–2, before a single table existed. The
schema is downstream of the code.

**Database-first** applies when the schema already exists and is owned by
something else — a legacy system, a shared enterprise database, a DBA-designed
schema. A scaffolding command reads the database and generates entity classes
to match.

### Interview framing

> *"Which would you use, and when?"*
> "Code-first for greenfield, where the domain model should drive the schema —
> it keeps business rules and validation living in the code. Database-first
> when the database already exists and I don't control it. A third pattern
> exists too: hand-written SQL migrations with EF models following them, common
> where DBAs require review over every schema change and 'the ORM decides my
> schema' isn't acceptable."

---

## Part 8 — Migrations

```powershell
dotnet ef migrations add InitialCreate --project src/Wallet.Api --startup-project src/Wallet.Api
```

| Flag | Meaning |
|---|---|
| `migrations add <Name>` | Name is chosen by you, PascalCase, descriptive of the change |
| `--project` | Where to **put** the generated files |
| `--startup-project` | Which project to **run** to read configuration. Same here; they differ in larger solutions |

### What gets generated

| File | Purpose |
|---|---|
| `<timestamp>_InitialCreate.cs` | The migration — `Up()` applies the change, `Down()` reverses it |
| `<timestamp>_InitialCreate.Designer.cs` | EF-internal metadata; never hand-edited |
| `WalletDbContextModelSnapshot.cs` | Snapshot of the current model state |

**Term: model snapshot.** EF Core does **not** inspect the live database to
determine what changed. It diffs the current C# model against this snapshot
file. That's how it knows what the *next* migration must contain — and why
deleting or corrupting it loses migration history.

**Term: `__EFMigrationsHistory`.** An EF-managed table in the database
recording which migrations have already been applied, so `database update`
knows what's pending and never runs the same migration twice.

### Verifying the generated schema against the ADRs

Every design decision from Sessions 1–3 showed up in the generated SQL, and
each was checked deliberately rather than assumed:

| Check | Evidence in the migration | Traces back to |
|---|---|---|
| `OwnsOne` flattened the value object | `amount_minor_units` (`bigint`) and `amount_currency` (`text`) are columns **on `ledger_entries`** — no separate `money` table anywhere | Session 1 entity/value-object distinction |
| Idempotency enforced by the database | `IX_ledger_transactions_idempotency_key`, `unique: true` | ADR 0002 |
| Ledger immutability protected | Both foreign keys carry `onDelete: ReferentialAction.Restrict` | ADR 0001 |
| Enum stored readably | `type = table.Column<string>(type: "text")`, not an integer | Session 3 audit-friendliness decision |
| Money as integer minor units | `amount_minor_units` is `bigint`, no float anywhere | ADR 0003 |

**One thing EF added unprompted:** indexes on both foreign key columns
(`IX_ledger_entries_transaction_id`, `IX_ledger_entries_wallet_id`). **EF Core
automatically indexes foreign keys** — sensible, since "all entries for this
wallet" would otherwise be a full table scan. Worth knowing so unexpected
indexes aren't a surprise.

---

## Part 9 — Summary of traps hit this session

| Trap | Signal | Fix |
|---|---|---|
| WSL2 missing | `wsl is not installed` | `wsl --install` + restart |
| Virtualization off in BIOS | `Virtualization Enabled In Firmware: No` in `systeminfo` | Enable VTx in BIOS |
| Unpinned package grabs wrong major version | NU1202, "supports net10.0" | Always `--version` for EF Core packages |
| Direct vs transitive version disagreement | NU1605 "package downgrade" | Align upward, not downward |
| Transitive resolving to lowest applicable | MSB3277 build warning | `dotnet list package --include-transitive`, then pin with a direct reference |
| `DbContext` never registered with DI | "Unable to resolve service for `DbContextOptions<T>`" | Register in `Program.cs` — the minimal fix, no factory needed |
| Config file tracked by git since scaffold | Shows as *modified*, not *untracked* | `.gitignore` + `git rm --cached` |
| Stale build artifacts masking a fix | Warning persists after correcting versions | `dotnet clean` + delete `bin`/`obj` |

---

## Part 10 — Open thread

Apply the migration to the running Postgres container:

```powershell
dotnet ef database update --project src/Wallet.Api --startup-project src/Wallet.Api
```

Then: the credit/debit API endpoints, where idempotency stops being a database
constraint and becomes request-handling logic.
