using Hdos.Contracts.DataContracts;

namespace Hdos.LakehouseService.Application.DataContracts.Schemas.Clinical;

public sealed class PatientDailyNewContract : DataContract<PatientDailyNewRow>
{
    public const string ContractCode = "patient.daily.new";

    public override string Code => ContractCode;
    public override string DisplayName => "Bệnh nhân đăng ký mới theo ngày × khoa";
}
