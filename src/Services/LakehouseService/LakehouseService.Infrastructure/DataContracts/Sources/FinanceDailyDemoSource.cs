using System.Runtime.CompilerServices;
using Hdos.Contracts.DataContracts;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Finance;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Sources;

/// <summary>
/// Source demo: in-memory fake rows, không đụng DB. Dùng để:
///   - FE test chart shape khi raw tables chưa setup
///   - QA reproduce edge case bằng cách edit constants
///   - Smoke test endpoint mới trước khi prod
///
/// Trigger: <c>?source=demo</c> trong URL chart.
/// </summary>
public sealed class FinanceDailyDemoSource : IDataSource<FinanceDailyRow>
{
    public string ContractCode => FinanceDailyContract.ContractCode;
    public string SourceCode   => "demo";

    public async IAsyncEnumerable<FinanceDailyRow> ReadAsync(
        DataContractQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        var date = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // Mỗi tuple ~ (dept, bucket). Tổng cộng ~16 rows = 8 dept × 2 bucket trung bình.
        var seed = new (int Id, string Name, string Bucket, decimal Total, decimal Discount, int Inv, int Enc)[]
        {
            (1, "Khoa Tim mạch",            "BHYT",       780_000_000m, 120_000_000m,  90, 100),
            (1, "Khoa Tim mạch",            "Dịch vụ",    460_000_000m,  60_000_000m,  40,  56),
            (2, "Khoa Hồi sức tích cực",    "BHYT",       620_000_000m, 220_000_000m,  60,  60),
            (2, "Khoa Hồi sức tích cực",    "Dịch vụ",    360_000_000m, 100_000_000m,  30,  38),
            (3, "Khoa Nhi",                 "BHYT",       480_000_000m,  55_000_000m,  88,  92),
            (3, "Khoa Nhi",                 "Dịch vụ",    240_000_000m,  30_000_000m,  42,  50),
            (4, "Khoa Sản",                 "BHYT",       420_000_000m,  48_000_000m,  76,  80),
            (4, "Khoa Sản",                 "Yêu cầu",    230_000_000m,  24_000_000m,  38,  44),
            (5, "Khoa Cấp cứu",             "BHYT",       350_000_000m, 200_000_000m, 120, 130),
            (5, "Khoa Cấp cứu",             "Dịch vụ",    170_000_000m,  80_000_000m,  50,  56),
            (6, "Khoa Ngoại thần kinh",     "BHYT",       320_000_000m,  20_000_000m,  28,  30),
            (6, "Khoa Ngoại thần kinh",     "Bảo hiểm tư",160_000_000m,  15_000_000m,  18,  17),
            (7, "Khoa Nội tiết",            "BHYT",       300_000_000m,  12_000_000m,  35,  38),
            (7, "Khoa Nội tiết",            "Dịch vụ",    120_000_000m,   6_000_000m,  18,  13),
            (8, "Khoa Da liễu",             "Dịch vụ",    150_000_000m,   5_000_000m,  22,  18),
            (8, "Khoa Da liễu",             "Yêu cầu",     80_000_000m,   3_000_000m,  12,  10),
        };

        foreach (var s in seed)
        {
            ct.ThrowIfCancellationRequested();
            yield return new FinanceDailyRow(
                InvoiceDate:            date,
                DepartmentId:           s.Id,
                DepartmentName:         s.Name,
                TotalInvoiceAmount:     s.Total,
                TotalDiscountAmount:    s.Discount,
                InvoiceCount:           s.Inv,
                DistinctEncounterCount: s.Enc,
                FinanceBucket:          s.Bucket);
        }
    }
}
