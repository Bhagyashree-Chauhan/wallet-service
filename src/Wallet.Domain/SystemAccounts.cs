namespace Wallet.Domain;
public static class SystemAccounts
{
    public static IReadOnlyDictionary<string, Guid> FundingAccountsReadOnly => FundingAccounts;
    public static Dictionary<string,Guid> FundingAccounts = new Dictionary<string, Guid>
    {
        ["AED"] = Guid.Parse("00000000-0000-0000-0000-000000000001"),
    };

    public static Guid GetFundingAccountId(string currency)
    {
        if(!FundingAccounts.TryGetValue(currency, out var accountId))
        {
            throw new InvalidOperationException($"Invalid currency: {currency}");
        }

        return accountId;
    }
}