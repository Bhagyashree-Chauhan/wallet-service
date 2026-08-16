using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Api.Persistence.Configurations;

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(e => e.TransactionId)
            .HasColumnName("transaction_id")
            .IsRequired();

        builder.Property(e => e.WalletId)
            .HasColumnName("wallet_id")
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasOne<Wallet.Domain.Wallet>()
            .WithMany()
            .HasForeignKey(e => e.WalletId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.OwnsOne(e => e.Amount, amount =>
        {
            amount.Property(m => m.MinorUnits)
                .HasColumnName("amount_minor_units")
                .IsRequired();

            amount.Property(m => m.Currency)
                .HasColumnName("amount_currency")
                .IsRequired();
        });
    }
}
