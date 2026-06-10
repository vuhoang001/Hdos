using Hdos.Contracts.DataContracts;

namespace Hdos.LakehouseService.Application.DataContracts.Schemas.Finance;

public sealed class FinanceMonthlyValidator : IDataContractValidator<FinanceMonthlyRow>
{
    public string ContractCode => FinanceMonthlyContract.ContractCode;

    public ValueTask<DataContractValidationResult> ValidateAsync(FinanceMonthlyRow row, CancellationToken ct)
    {
        var errors = new List<string>();

        if (row.Year < 2000 || row.Year > 9999)
            errors.Add($"{nameof(row.Year)} out of range (got {row.Year}).");

        if (row.Month < 1 || row.Month > 12)
            errors.Add($"{nameof(row.Month)} must be 1..12 (got {row.Month}).");

        if (row.DepartmentId <= 0)
            errors.Add($"{nameof(row.DepartmentId)} must be positive.");

        if (string.IsNullOrWhiteSpace(row.DepartmentName))
            errors.Add($"{nameof(row.DepartmentName)} cannot be empty.");

        if (row.TotalRevenue < 0)
            errors.Add($"{nameof(row.TotalRevenue)} cannot be negative.");

        if (row.TotalCost < 0)
            errors.Add($"{nameof(row.TotalCost)} cannot be negative.");

        if (row.PatientCount < 0)
            errors.Add($"{nameof(row.PatientCount)} cannot be negative.");

        return ValueTask.FromResult(DataContractValidationResult.FromMessages(errors));
    }
}
