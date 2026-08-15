using System;
namespace Wallet.Domain;
public class Wallet
{
    public Guid Id { get; private set; }
    public string Currency { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public Wallet(Guid id, string currency)
    {
        Id = id;
        Currency = currency;
        CreatedAt = DateTime.UtcNow;
    }   

}