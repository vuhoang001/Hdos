using Hdos.M01Service.Domain.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Hdos.M01Service.Infrastructure.Persistence;

public sealed class M01DbContext : DbContext
{
    public M01DbContext(DbContextOptions<M01DbContext> options) : base(options) { }

    public DbSet<PhongKham> PhongKhams => Set<PhongKham>();
    public DbSet<BenhNhan> BenhNhans => Set<BenhNhan>();
    public DbSet<CapCuuRecord> CapCuus => Set<CapCuuRecord>();
    public DbSet<NhanSuTruc> NhanSuTrucs => Set<NhanSuTruc>();
    public DbSet<ForecastEntry> ForecastEntries => Set<ForecastEntry>();
    public DbSet<ForecastMeta> ForecastMetas => Set<ForecastMeta>();
    public DbSet<DashboardSnapshot> DashboardSnapshots => Set<DashboardSnapshot>();
    public DbSet<KhoaDoanhThu> KhoaDoanhThus => Set<KhoaDoanhThu>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(M01DbContext).Assembly);

        // Outbox + Inbox tables cho MassTransit EF Outbox
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        base.OnModelCreating(modelBuilder);
    }
}
