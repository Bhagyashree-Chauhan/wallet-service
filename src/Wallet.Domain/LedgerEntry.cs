using System;
using Wallet.Domain;

namespace Wallet.Domain;

public class LedgerEntry
{
    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid WalletId { get; private set; }
    public Money Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public LedgerEntry(Guid transactionId, Guid walletId, Money amount)
    {
        Id = Guid.NewGuid();
        WalletId = walletId;
        TransactionId = transactionId;
        Amount = amount;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}