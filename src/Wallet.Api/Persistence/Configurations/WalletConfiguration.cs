using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wallet.Api.Persistence.Configurations;

public class WalletConfiguration : IEntityTypeConfiguration<Wallet.Domain.Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet.Domain.Wallet> builder)
    {
        builder.ToTable("wallets");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(w => w.Currency)
            .HasColumnName("currency")
            .IsRequired();

        builder.Property(w => w.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
    }
}
