using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Hdos.DataMatchingService.API.Swagger;

/// <summary>
/// Ẩn <see cref="System.Threading.CancellationToken"/> khỏi danh sách parameter trên Swagger UI.
/// CancellationToken do framework tự bind, FE không cần biết tới.
/// </summary>
internal sealed class HideCancellationTokenFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Parameters is null) return;

        var ctParamNames = context.ApiDescription.ParameterDescriptions
            .Where(p => p.ParameterDescriptor?.ParameterType == typeof(CancellationToken))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        if (ctParamNames.Count == 0) return;

        for (var i = operation.Parameters.Count - 1; i >= 0; i--)
        {
            if (ctParamNames.Contains(operation.Parameters[i].Name))
                operation.Parameters.RemoveAt(i);
        }
    }
}
