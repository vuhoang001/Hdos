using Hdos.M01Service.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.M01Service.Infrastructure.Persistence.Configurations;

public sealed class PhongKhamConfiguration : IEntityTypeConfiguration<PhongKham>
{
    public void Configure(EntityTypeBuilder<PhongKham> b)
    {
        b.ToTable("PhongKhams");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("MaPhong").HasMaxLength(32).ValueGeneratedNever();
        b.Property(x => x.TenPhong).HasMaxLength(120).IsRequired();
        b.Property(x => x.ChoTbPhut).IsRequired();
        b.Property(x => x.SoBenhNhan).IsRequired();
        b.Property(x => x.MucDoTai).HasConversion<int>().IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
    }
}

public sealed class BenhNhanConfiguration : IEntityTypeConfiguration<BenhNhan>
{
    public void Configure(EntityTypeBuilder<BenhNhan> b)
    {
        b.ToTable("BenhNhans");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("MaBn").HasMaxLength(32).ValueGeneratedNever();
        b.Property(x => x.HoTen).HasMaxLength(120).IsRequired();
        b.Property(x => x.Triage).HasConversion<int>().IsRequired();
        b.Property(x => x.TrangThai).HasConversion<int>().IsRequired();
        b.Property(x => x.MaPhongKham).HasMaxLength(32).IsRequired();
        b.Property(x => x.BacSi).HasMaxLength(120);
        b.Property(x => x.ChoPhut).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.MaPhongKham);
        b.HasIndex(x => x.TrangThai);
    }
}

public sealed class CapCuuConfiguration : IEntityTypeConfiguration<CapCuuRecord>
{
    public void Configure(EntityTypeBuilder<CapCuuRecord> b)
    {
        b.ToTable("CapCuus");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("MaCu").HasMaxLength(32).ValueGeneratedNever();
        b.Property(x => x.HoTen).HasMaxLength(120).IsRequired();
        b.Property(x => x.Triage).HasConversion<int>().IsRequired();
        b.Property(x => x.ChoPhut).IsRequired();
        b.Property(x => x.BacSi).HasMaxLength(120);
        b.Property(x => x.TrangThai).HasConversion<int>().IsRequired();
        b.Property(x => x.CanhBao).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
    }
}

public sealed class NhanSuTrucConfiguration : IEntityTypeConfiguration<NhanSuTruc>
{
    public void Configure(EntityTypeBuilder<NhanSuTruc> b)
    {
        b.ToTable("NhanSuTrucs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("Id").HasMaxLength(32).ValueGeneratedNever();
        b.Property(x => x.HoTen).HasMaxLength(120).IsRequired();
        b.Property(x => x.Khoa).HasMaxLength(60).IsRequired();
        b.Property(x => x.MaPhongKham).HasMaxLength(32).IsRequired();
        b.Property(x => x.SoBnDangKham).IsRequired();
        b.Property(x => x.TrangThai).HasConversion<int>().IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
    }
}

public sealed class ForecastEntryConfiguration : IEntityTypeConfiguration<ForecastEntry>
{
    public void Configure(EntityTypeBuilder<ForecastEntry> b)
    {
        b.ToTable("ForecastEntries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Gio).HasMaxLength(8).IsRequired();
        b.Property(x => x.DuBao).IsRequired();
        b.Property(x => x.ThucTe);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.Gio);
    }
}

public sealed class ForecastMetaConfiguration : IEntityTypeConfiguration<ForecastMeta>
{
    public void Configure(EntityTypeBuilder<ForecastMeta> b)
    {
        b.ToTable("ForecastMetas");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ModelVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.CaoDiemDuKien).HasMaxLength(8).IsRequired();
        b.Property(x => x.DoChinhXacMae).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
    }
}

public sealed class DashboardSnapshotConfiguration : IEntityTypeConfiguration<DashboardSnapshot>
{
    public void Configure(EntityTypeBuilder<DashboardSnapshot> b)
    {
        b.ToTable("DashboardSnapshots");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
    }
}

public sealed class KhoaDoanhThuConfiguration : IEntityTypeConfiguration<KhoaDoanhThu>
{
    public void Configure(EntityTypeBuilder<KhoaDoanhThu> b)
    {
        b.ToTable("KhoaDoanhThus");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("MaKhoa").HasMaxLength(32).ValueGeneratedNever();
        b.Property(x => x.TenKhoa).HasMaxLength(120).IsRequired();
        b.Property(x => x.SoBenhNhan).IsRequired();
        b.Property(x => x.TongThu).HasColumnType("decimal(18,2)").IsRequired();
        b.Property(x => x.BhytTra).HasColumnType("decimal(18,2)").IsRequired();
        b.Property(x => x.BnTra).HasColumnType("decimal(18,2)").IsRequired();
        b.Property(x => x.HaoPhiKhac).HasColumnType("decimal(18,2)").IsRequired();
        b.Property(x => x.NgayBaoCao).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.NgayBaoCao);
    }
}
