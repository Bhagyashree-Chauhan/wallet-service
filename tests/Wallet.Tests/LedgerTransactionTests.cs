using System;
using Xunit;
using Wallet.Domain;

namespace Wallet.Tests;

public class LedgerTransactionTests
{
    [Fact]
    public void Post_WithBalancedEntries_ShouldSucceed()
    {
        var txn = new LedgerTransaction("key1", TransactionType.Credit);
        var e1 =   new LedgerEntry(txn.Id, Guid.NewGuid(), new Money(100, "AED"));
        var e2 =   new LedgerEntry(txn.Id, Guid.NewGuid(), new Money(-100, "AED"));
        txn.AddEntry(e1);
        txn.AddEntry(e2);
        txn.Post();
    }

    [Fact]
    public void Post_WithUnbalancedEntries_ShouldThrowInvalidOperationException()
    {
        var txn = new LedgerTransaction("key2", TransactionType.Credit);
        var e1 = new LedgerEntry(txn.Id, Guid.NewGuid(), new Money(100, "AED"));
        var e2 = new LedgerEntry(txn.Id, Guid.NewGuid(), new Money(-50, "AED"));
        txn.AddEntry(e1);
        txn.AddEntry(e2);

        Assert.Throws<InvalidOperationException>(() => txn.Post());
    }

    [Fact]
    public void AddEntry_WithDifferentCurrency_ShouldThrowInvalidOperationException()
    {
        var txn = new LedgerTransaction("key3", TransactionType.Credit);
        var e1 = new LedgerEntry(txn.Id, Guid.NewGuid(), new Money(100, "AED"));
        var e2 = new LedgerEntry(txn.Id, Guid.NewGuid(), new Money(-100, "USD"));
        txn.AddEntry(e1);

        Assert.Throws<InvalidOperationException>(() => txn.AddEntry(e2));
    }
    
    [Fact]
    public void AddEntry_MismatchedTxnId_ShouldThrowInvalidOperationException()
    {
        var txn = new LedgerTransaction("key4", TransactionType.Credit);
        var e1 = new LedgerEntry(Guid.NewGuid(), Guid.NewGuid(), new Money(100, "AED"));
        Assert.Throws<InvalidOperationException>(() => txn.AddEntry(e1));
    }
}
