using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Api.Persistence.Configurations;

public class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> builder)
    {
        builder.ToTable("ledger_transactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .IsRequired();

        builder.HasIndex(t => t.IdempotencyKey)
            .IsUnique();

        builder.Property(t => t.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasMany(t => t.Entries)
            .WithOne()
            .HasForeignKey(e => e.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(t => t.Entries)
            .HasField("_entries")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
