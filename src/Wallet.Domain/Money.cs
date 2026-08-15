namespace Wallet.Domain;

public record Money(long MinorUnits, string Currency)
{
    public Money Add(Money money)
    {
        if (Currency != money.Currency)
        {
            throw new InvalidOperationException("Cannot add money with different currencies.");
        }

        return new Money(this.MinorUnits + money.MinorUnits, Currency);
    }
}
