namespace Hdos.Contracts.DataContracts.Finance;

/// <summary>
/// Validator schema-level cho <see cref="FinanceDailyRow"/>. Chạy ở RANH GIỚI:
/// khi caller push row từ external source (API, RMQ event). Source internal đọc DB internal
/// KHÔNG cần qua validator — Gateway.ValidateAsync là optional cho caller.
/// </summary>
public sealed class FinanceDailyValidator : IDataContractValidator<FinanceDailyRow>
{
    public string ContractCode => FinanceDailyContract.ContractCode;

    public ValueTask<DataContractValidationResult> ValidateAsync(FinanceDailyRow row, CancellationToken ct)
    {
        var errors = new List<string>();

        if (row.DepartmentId <= 0)
            errors.Add($"{nameof(row.DepartmentId)} must be positive (got {row.DepartmentId}).");

        if (string.IsNullOrWhiteSpace(row.DepartmentName))
            errors.Add($"{nameof(row.DepartmentName)} cannot be empty.");

        if (row.TotalInvoiceAmount < 0)
            errors.Add($"{nameof(row.TotalInvoiceAmount)} cannot be negative (got {row.TotalInvoiceAmount}).");

        if (row.TotalDiscountAmount < 0)
            errors.Add($"{nameof(row.TotalDiscountAmount)} cannot be negative (got {row.TotalDiscountAmount}).");

        if (row.TotalDiscountAmount > row.TotalInvoiceAmount)
            errors.Add($"Discount ({row.TotalDiscountAmount}) cannot exceed total invoice ({row.TotalInvoiceAmount}).");

        if (row.InvoiceCount < 0)
            errors.Add($"{nameof(row.InvoiceCount)} cannot be negative (got {row.InvoiceCount}).");

        if (row.DistinctEncounterCount < 0)
            errors.Add($"{nameof(row.DistinctEncounterCount)} cannot be negative (got {row.DistinctEncounterCount}).");

        return ValueTask.FromResult(DataContractValidationResult.FromMessages(errors));
    }
}
