using Hdos.Contracts.DataContracts;

namespace Hdos.LakehouseService.Application.DataContracts.Schemas.Finance;

public sealed class FinanceDailyContract : DataContract<FinanceDailyRow>
{
    public const string ContractCode = "finance.daily.row";

    public override string Code => ContractCode;
    public override string DisplayName => "Tài chính theo ngày × khoa (row-level)";
}
