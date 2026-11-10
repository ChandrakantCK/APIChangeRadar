using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Radar.Domain.Changes;

namespace Radar.Infrastructure.Persistence.Configurations
{
    internal sealed class ApiChangeConfiguration : IEntityTypeConfiguration<ApiChange>
    {
        public void Configure(EntityTypeBuilder<ApiChange> builder)
        {
            builder.ToTable("api_changes");
            builder.HasKey(c => c.Id);

            builder.Property(c => c.RuleId).HasMaxLength(ApiChange.RuleIdMaxLength).IsRequired();
            builder.Property(c => c.Severity).HasConversion<string>().HasMaxLength(10);
            builder.Property(c => c.Method).HasMaxLength(ApiChange.MethodMaxLength).IsRequired();
            builder.Property(c => c.Path).HasMaxLength(ApiChange.PathMaxLength).IsRequired();
            builder.Property(c => c.Location).HasMaxLength(ApiChange.LocationMaxLength).IsRequired();
            builder.Property(c => c.Message).HasMaxLength(ApiChange.MessageMaxLength).IsRequired();

            builder.HasIndex(c => new { c.ChangeSetId, c.Position });
        }
    }
}
