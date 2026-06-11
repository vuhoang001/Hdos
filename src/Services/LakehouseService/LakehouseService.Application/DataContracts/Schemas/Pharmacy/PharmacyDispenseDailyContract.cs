using Hdos.Contracts.DataContracts;

namespace Hdos.LakehouseService.Application.DataContracts.Schemas.Pharmacy;

public sealed class PharmacyDispenseDailyContract : DataContract<PharmacyDispenseDailyRow>
{
    public const string ContractCode = "pharmacy.dispense.daily.row";

    public override string Code => ContractCode;
    public override string DisplayName => "Phát thuốc theo ngày × khoa × nhóm thuốc";
}
