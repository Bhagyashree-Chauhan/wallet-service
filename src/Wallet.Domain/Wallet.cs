using System;
namespace Wallet.Domain;
public class Wallet
{
    public Guid Id { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public Wallet(Guid id, string currency)
    {
        Id = id;
        Currency = currency;
        CreatedAt = DateTimeOffset.UtcNow;
    }   

}