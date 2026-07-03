using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SampleApp.Orders.Domain.Entities;

namespace SampleApp.Orders.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.CustomerName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Total).IsRequired();
    }
}
