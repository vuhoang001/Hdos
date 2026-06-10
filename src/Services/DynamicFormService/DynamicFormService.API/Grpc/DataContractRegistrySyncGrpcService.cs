using Grpc.Core;
using Hdos.Contracts.Grpc.DataContractRegistry;
using Hdos.DynamicFormService.Application.Features.Providers.SyncRegistry;
using MediatR;

namespace Hdos.DynamicFormService.API.Grpc;

// Adapter mỏng: nhận SyncRegistryRequest từ gRPC, dispatch sang MediatR command.
// Mọi business logic ở SyncProviderRegistryCommandHandler. Idempotent.
public sealed class DataContractRegistrySyncGrpcService(
    IMediator                                          mediator,
    ILogger<DataContractRegistrySyncGrpcService>        logger)
    : DataContractRegistrySyncService.DataContractRegistrySyncServiceBase
{
    public override async Task<SyncRegistryReply> SyncRegistry(
        SyncRegistryRequest request, ServerCallContext context)
    {
        if (request.Provider is null)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "provider is required"));

        var operations = request.Operations
            .Select(op => new OperationPayload(
                op.OperationKey,
                op.DisplayName,
                op.Pattern,
                string.IsNullOrWhiteSpace(op.SchemaPath) ? null : op.SchemaPath,
                op.RequiredParams.ToList(),
                op.Kind))
            .ToList();

        var command = new SyncProviderRegistryCommand(
            request.Provider.Code,
            request.Provider.DisplayName,
            request.Provider.BaseUrl,
            operations);

        var result = await mediator.Send(command, context.CancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("SyncRegistry failed for provider {Code}: {Error}",
                request.Provider.Code, result.Error.Message);
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"{result.Error.Code}: {result.Error.Message}"));
        }

        logger.LogInformation(
            "SyncRegistry OK provider={Code} upsertedOps={Ops} deactivated={Dead}",
            request.Provider.Code,
            result.Value.UpsertedOperationCount,
            result.Value.DeactivatedOperationCount);

        return new SyncRegistryReply
        {
            UpsertedProviderCount    = result.Value.UpsertedProviderCount,
            UpsertedOperationCount   = result.Value.UpsertedOperationCount,
            DeactivatedOperationCount = result.Value.DeactivatedOperationCount
        };
    }
}
