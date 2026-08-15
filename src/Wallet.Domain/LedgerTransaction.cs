using System;
using System.Collections.Generic;
using System.Linq;
using Wallet.Domain;
namespace Wallet.Domain;

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
        Type = type;;
        CreatedAt = DateTimeOffset.UtcNow;

    }

    public void AddEntry(LedgerEntry entry)
    {
        if(entry.TransactionId != Id)
        {
            throw new InvalidOperationException("Entry transaction ID does not match the transaction ID.");
        }
        if(_entries.Count > 0 && _entries[0].Amount.Currency != entry.Amount.Currency)
        {
            throw new InvalidOperationException("Cannot add entries with different currencies.");
        }
        _entries.Add(entry);
    }

    public void Post()
    {
        if (_entries.Count == 0)
        {
            throw new InvalidOperationException("Cannot post a transaction with no entries.");
        }

        var sum = _entries.Sum(e => e.Amount.MinorUnits);
        if (sum != 0)
        {
            throw new InvalidOperationException("Cannot post a transaction with unbalanced entries.");
        }
    }
}

public enum TransactionType
{
    Credit,
    Debit,
    Transfer
}