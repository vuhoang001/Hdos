using Hdos.DataMatchingService.Domain.Entities;
using Hdos.DataMatchingService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.DataMatchingService.Infrastructure.Persistence.Configurations;

public sealed class StagingRecordConfiguration : IEntityTypeConfiguration<StagingRecord>
{
    public void Configure(EntityTypeBuilder<StagingRecord> b)
    {
        b.ToTable("StagingRecords");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.SourceSystem).HasMaxLength(100).IsRequired();
        b.Property(x => x.RecordType).HasMaxLength(100).IsRequired();
        b.Property(x => x.RawPayload).HasColumnType("text").IsRequired();
        b.Property(x => x.CanonicalPayload).HasColumnType("text");
        b.Property(x => x.BusinessKey).HasMaxLength(500);
        b.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Status).HasMaxLength(30).HasConversion<string>().IsRequired();
        b.Property(x => x.MatchedKey).HasMaxLength(500);
        b.Property(x => x.FailureReason).HasMaxLength(1000);
        b.Property(x => x.ReceivedAt).IsRequired();
        b.Property(x => x.ProcessedAt);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.PayloadHash);
        b.HasIndex(x => new { x.SourceSystem, x.RecordType });   // filter phổ biến nhất
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.ReceivedAt);
    }
}
