using Hdos.Contracts.DataContracts;

namespace Hdos.LakehouseService.Application.DataContracts.Schemas.Clinical;

public sealed class PatientDailyNewValidator : IDataContractValidator<PatientDailyNewRow>
{
    public string ContractCode => PatientDailyNewContract.ContractCode;

    public ValueTask<DataContractValidationResult> ValidateAsync(
        PatientDailyNewRow row, CancellationToken ct)
    {
        var errors = new List<string>();

        if (row.DepartmentId <= 0)
            errors.Add($"{nameof(row.DepartmentId)} must be positive (got {row.DepartmentId}).");

        if (string.IsNullOrWhiteSpace(row.DepartmentName))
            errors.Add($"{nameof(row.DepartmentName)} cannot be empty.");

        if (row.NewPatientCount < 0)
            errors.Add($"{nameof(row.NewPatientCount)} cannot be negative (got {row.NewPatientCount}).");

        if (row.AgeAvg < 0 || row.AgeAvg > 150)
            errors.Add($"{nameof(row.AgeAvg)} {row.AgeAvg} unrealistic (expected 0-150).");

        if (row.MalePct < 0 || row.MalePct > 100)
            errors.Add($"{nameof(row.MalePct)} must be 0-100 (got {row.MalePct}).");

        return ValueTask.FromResult(DataContractValidationResult.FromMessages(errors));
    }
}
