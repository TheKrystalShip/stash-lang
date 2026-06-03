using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Stash.Registry.OpenApi;

/// <summary>
/// An <see cref="IOpenApiOperationTransformer"/> that fills in a stable
/// <c>{ControllerName}_{ActionName}</c> operation ID for every controller
/// action that does not already carry one.
/// </summary>
/// <remarks>
/// Minimal-API operations (e.g., the health endpoint) do NOT pass through this transformer's
/// conditional — their <c>OperationId</c> is set via <c>.WithName("Health_Check")</c> in the
/// endpoint definition. The transformer therefore silently skips operations whose description
/// is not backed by a <see cref="ControllerActionDescriptor"/>.
/// </remarks>
public sealed class OpenApiOperationIdTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (operation.OperationId is null)
        {
            var descriptor = context.Description.ActionDescriptor as ControllerActionDescriptor;
            if (descriptor is not null)
            {
                // ControllerName is already stripped of the "Controller" suffix by the framework.
                operation.OperationId = $"{descriptor.ControllerName}_{descriptor.ActionName}";
            }
        }

        return Task.CompletedTask;
    }
}
