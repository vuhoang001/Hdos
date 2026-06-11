using Hdos.Contracts.DataContracts;

namespace Hdos.LakehouseService.Application.DataContracts.Schemas.Pharmacy;

public sealed class PharmacyDispenseDailyValidator : IDataContractValidator<PharmacyDispenseDailyRow>
{
    public string ContractCode => PharmacyDispenseDailyContract.ContractCode;

    private static readonly HashSet<string> AllowedStockLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "low", "out"
    };

    public ValueTask<DataContractValidationResult> ValidateAsync(
        PharmacyDispenseDailyRow row, CancellationToken ct)
    {
        var errors = new List<string>();

        if (row.DepartmentId <= 0)
            errors.Add($"{nameof(row.DepartmentId)} must be positive (got {row.DepartmentId}).");

        if (string.IsNullOrWhiteSpace(row.DepartmentName))
            errors.Add($"{nameof(row.DepartmentName)} cannot be empty.");

        if (string.IsNullOrWhiteSpace(row.DrugGroup))
            errors.Add($"{nameof(row.DrugGroup)} cannot be empty.");

        if (row.PrescriptionCount < 0)
            errors.Add($"{nameof(row.PrescriptionCount)} cannot be negative (got {row.PrescriptionCount}).");

        if (row.DoseCount < 0)
            errors.Add($"{nameof(row.DoseCount)} cannot be negative (got {row.DoseCount}).");

        if (row.TotalAmount < 0)
            errors.Add($"{nameof(row.TotalAmount)} cannot be negative (got {row.TotalAmount}).");

        if (row.PatientServedCount < 0)
            errors.Add($"{nameof(row.PatientServedCount)} cannot be negative (got {row.PatientServedCount}).");

        if (row.StockAlertLevel is not null && !AllowedStockLevels.Contains(row.StockAlertLevel))
            errors.Add($"{nameof(row.StockAlertLevel)} '{row.StockAlertLevel}' invalid (allowed: null, low, out).");

        return ValueTask.FromResult(DataContractValidationResult.FromMessages(errors));
    }
}
