using System.Runtime.CompilerServices;
using Hdos.Contracts.DataContracts;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Pharmacy;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Sources;

/// <summary>
/// Mock source cho <see cref="PharmacyDispenseDailyContract"/>: 7 khoa × 3-5 nhóm thuốc
/// để FE test render + verify shape. KHÔNG đụng DB.
///
/// Trigger: <c>?source=demo</c> trong URL. Mặc định: chỉ có source này → gateway pick auto.
///
/// Đa dạng tự nhiên trong mock:
///   - 7 khoa khác nhau (Nội tổng quát, Cấp cứu, Nhi, ICU, Sản, Ung bướu, Tim mạch)
///   - 5 nhóm thuốc: Kháng sinh, Tim mạch, Giảm đau, Tiêu hoá, Khác
///   - Tỉ lệ kháng sinh lệch theo khoa (Cấp cứu 55%, Sản 12%) — trigger color bucket + KPI alert
///   - 2 khoa có StockAlertLevel != null (ICU "low", Nhi "out") — verify AlertList render
///   - DoseCount range 80 (Sản) → 1240 (Cấp cứu) — verify scale ProgressList
///
/// Filter hỗ trợ:
///   - <c>date</c>: yyyy-MM-dd. Default = UTC today. Áp vào DispenseDate của mọi row.
///   - <c>department</c>: int. Filter 1 khoa cụ thể.
///   - <c>group</c>: string. Filter nhóm thuốc (Kháng sinh, Tim mạch,...).
/// </summary>
public sealed class PharmacyDispenseDailyDemoSource : IDataSource<PharmacyDispenseDailyRow>
{
    public string ContractCode => PharmacyDispenseDailyContract.ContractCode;
    public string SourceCode   => "demo";

    private sealed record GroupSeed(string Group, int Rx, int Dose, decimal Amount, int Patients);

    private sealed record DeptSeed(
        int     Id,
        string  Name,
        string? StockAlertLevel,
        GroupSeed[] Groups);

    private static readonly DeptSeed[] Seeds =
    {
        new(1, "Khoa Nội tổng quát", null, new GroupSeed[]
        {
            new("Kháng sinh", 24, 156, 18_400_000m, 58),
            new("Tim mạch",   18, 142, 31_200_000m, 62),
            new("Tiêu hoá",   12,  88, 11_700_000m, 41),
            new("Giảm đau",   16, 102,  7_500_000m, 54),
        }),
        new(2, "Khoa Cấp cứu", "low", new GroupSeed[]
        {
            new("Kháng sinh", 88, 612, 72_300_000m, 124),
            new("Giảm đau",   72, 488, 21_800_000m, 118),
            new("Tim mạch",   24, 140, 28_400_000m,  46),
            new("Khác",       18, 102, 12_900_000m,  38),
        }),
        new(3, "Khoa Nhi", "out", new GroupSeed[]
        {
            new("Kháng sinh", 56, 312, 19_800_000m,  98),
            new("Giảm đau",   42, 248,  9_400_000m,  92),
            new("Tiêu hoá",   38, 196, 11_200_000m,  84),
            new("Khác",       12,  64,  3_100_000m,  28),
        }),
        new(4, "Khoa Hồi sức ICU", "low", new GroupSeed[]
        {
            new("Kháng sinh", 36, 412, 84_500_000m,  18),
            new("Tim mạch",   28, 198, 56_700_000m,  16),
            new("Giảm đau",   24, 164, 18_300_000m,  17),
            new("Khác",       14,  82, 22_100_000m,  12),
        }),
        new(5, "Khoa Sản", null, new GroupSeed[]
        {
            new("Giảm đau",   24, 142,  8_900_000m,  64),
            new("Kháng sinh",  8,  48,  6_200_000m,  22),
            new("Tiêu hoá",   12,  72,  4_100_000m,  38),
            new("Khác",        6,  28,  2_400_000m,  18),
        }),
        new(6, "Khoa Ung bướu", null, new GroupSeed[]
        {
            new("Giảm đau",   42, 318, 28_700_000m,  46),
            new("Khác",       38, 286, 92_400_000m,  44),
            new("Kháng sinh", 18, 124, 14_600_000m,  31),
            new("Tiêu hoá",   16,  98,  6_800_000m,  28),
        }),
        new(7, "Khoa Tim mạch", null, new GroupSeed[]
        {
            new("Tim mạch",   62, 524, 78_200_000m,  86),
            new("Kháng sinh", 22, 148, 13_400_000m,  48),
            new("Giảm đau",   28, 196,  9_700_000m,  64),
            new("Khác",        9,  52,  3_800_000m,  18),
        }),
    };

    public async IAsyncEnumerable<PharmacyDispenseDailyRow> ReadAsync(
        DataContractQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;

        var date     = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var deptOnly = query.GetInt("department");
        var grpOnly  = query.Get("group");

        foreach (var d in Seeds)
        {
            ct.ThrowIfCancellationRequested();

            if (deptOnly is not null && d.Id != deptOnly) continue;

            foreach (var g in d.Groups)
            {
                if (!string.IsNullOrEmpty(grpOnly) &&
                    !string.Equals(g.Group, grpOnly, StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return new PharmacyDispenseDailyRow(
                    DispenseDate:        date,
                    DepartmentId:        d.Id,
                    DepartmentName:      d.Name,
                    DrugGroup:           g.Group,
                    PrescriptionCount:   g.Rx,
                    DoseCount:           g.Dose,
                    TotalAmount:         g.Amount,
                    PatientServedCount:  g.Patients,
                    StockAlertLevel:     d.StockAlertLevel);
            }
        }
    }
}
