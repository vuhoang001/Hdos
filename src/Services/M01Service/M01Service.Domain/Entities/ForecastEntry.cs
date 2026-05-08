using Hdos.SharedKernel;

namespace Hdos.M01Service.Domain.Entities;

public sealed class ForecastEntry : BaseEntity<int>
{
    public string Gio { get; private set; } = default!;
    public int DuBao { get; private set; }
    public int? ThucTe { get; private set; }

    private ForecastEntry() { }

    public ForecastEntry(int id, string gio, int duBao, int? thucTe)
    {
        Id = id;
        Gio = gio;
        DuBao = duBao;
        ThucTe = thucTe;
    }
}

public sealed class ForecastMeta : BaseEntity<int>
{
    public string ModelVersion { get; private set; } = default!;
    public string CaoDiemDuKien { get; private set; } = default!;
    public double DoChinhXacMae { get; private set; }

    private ForecastMeta() { }

    public ForecastMeta(string modelVersion, string caoDiemDuKien, double doChinhXacMae)
    {
        Id = 1;
        ModelVersion = modelVersion;
        CaoDiemDuKien = caoDiemDuKien;
        DoChinhXacMae = doChinhXacMae;
    }
}
