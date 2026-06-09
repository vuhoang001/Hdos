using System.Runtime.CompilerServices;
using Hdos.Contracts.DataContracts;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Clinical;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Sources;

/// <summary>
/// Mock source cho <see cref="PatientDailyNewContract"/>: 8 khoa với data hợp lý
/// để FE test render + verify shape. KHÔNG đụng DB.
///
/// Trigger: <c>?source=demo</c> trong URL. Mặc định: nếu chỉ có source này
/// đăng ký, gateway sẽ pick nó luôn (đầu tiên trong list).
///
/// Đa dạng tự nhiên trong mock:
///   - 8 khoa khác nhau (Tim mạch, Nhi, Sản, Cấp cứu,...)
///   - AgeAvg span từ 6.4 (Nhi) → 62.1 (Hồi sức) — đủ trigger color bucket Consumer
///   - MalePct lệch theo khoa (Sản 5%, Cấp cứu 58%) — verify FE filter/sort
///   - Department có count cao (Cấp cứu 124) + thấp (Da liễu 15) — verify scale chart
///
/// Filter hỗ trợ:
///   - <c>date</c>: yyyy-MM-dd. Default = UTC today. Áp vào RegisterDate của mọi row.
///   - <c>department</c>: int. Filter 1 khoa cụ thể (test FE filter dropdown).
/// </summary>
public sealed class PatientDailyNewDemoSource : IDataSource<PatientDailyNewRow>
{
    public string ContractCode => PatientDailyNewContract.ContractCode;
    public string SourceCode   => "demo";

    public async IAsyncEnumerable<PatientDailyNewRow> ReadAsync(
        DataContractQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;

        var date     = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var deptOnly = query.GetInt("department");

        var seed = new (int Id, string Name, int Count, double Avg, double MalePct)[]
        {
            (1, "Khoa Tim mạch",          42,  56.3,  52.0),
            (2, "Khoa Hồi sức tích cực",  18,  62.1,  61.0),
            (3, "Khoa Nhi",               87,   6.4,  48.5),
            (4, "Khoa Sản",               35,  28.5,   5.0),
            (5, "Khoa Cấp cứu",          124,  41.2,  58.0),
            (6, "Khoa Ngoại thần kinh",   12,  58.7,  66.0),
            (7, "Khoa Nội tiết",          28,  52.4,  40.0),
            (8, "Khoa Da liễu",           15,  32.1,  35.0),
        };

        foreach (var s in seed)
        {
            ct.ThrowIfCancellationRequested();

            if (deptOnly is not null && s.Id != deptOnly) continue;

            yield return new PatientDailyNewRow(
                RegisterDate:    date,
                DepartmentId:    s.Id,
                DepartmentName:  s.Name,
                NewPatientCount: s.Count,
                AgeAvg:          s.Avg,
                MalePct:         s.MalePct);
        }
    }
}
