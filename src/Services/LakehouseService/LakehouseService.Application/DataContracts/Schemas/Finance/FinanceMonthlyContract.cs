using Hdos.Contracts.DataContracts;

namespace Hdos.LakehouseService.Application.DataContracts.Schemas.Finance;

public sealed class FinanceMonthlyContract : DataContract<FinanceMonthlyRow>
{
    public const string ContractCode = "finance.monthly.row";

    public override string Code        => ContractCode;
    public override string DisplayName => "Tài chính theo tháng × khoa (row-level)";
}
