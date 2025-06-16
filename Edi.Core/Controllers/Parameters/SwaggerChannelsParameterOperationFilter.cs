using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Edi.Core.Controllers.Parameters
{
    public class SwaggerChannelsParameterOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var controllerActionDescriptor = context.ApiDescription.ActionDescriptor as ControllerActionDescriptor;
            if (controllerActionDescriptor == null)
                return;

            // Methods that use channels
            var actionsWithChannels = new[] { "Play", "Stop", "Pause", "Resume", "Intensity" };
            if (!actionsWithChannels.Contains(controllerActionDescriptor.ActionName))
                return;

            // Add query parameter
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "channels",
                In = ParameterLocation.Query,
                Description = "Optional. Comma-separated list of channels",
                Required = false,
                Schema = new OpenApiSchema { Type = "string" }
            });
        }
    }
}
