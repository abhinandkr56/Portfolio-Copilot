using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PortfolioCopilot.Api;

/// <summary>
/// Tightens the generated OpenAPI so the frontend's generated TypeScript types are precise (doc 07):
/// shared enum schemas never contain null (nullability belongs on the property), and finding evidence values
/// are typed as string | number | boolean instead of an empty object.
/// </summary>
public static class OpenApiFixes
{
    public static OpenApiOptions AddPortfolioCopilotFixes(this OpenApiOptions options)
    {
        options.AddSchemaTransformer((schema, context, _) =>
        {
            var type = context.JsonTypeInfo.Type;
            var enumType = Nullable.GetUnderlyingType(type) ?? type; // the shared schema can come from Severity? as well as Severity

            if (enumType.IsEnum && schema.Enum is { Count: > 0 } values)
            {
                foreach (var nullValue in values.Where(v => v is null || v.GetValueKind() == System.Text.Json.JsonValueKind.Null).ToList())
                    values.Remove(nullValue);
                schema.Type = JsonSchemaType.String;
            }

            if (type == typeof(IReadOnlyDictionary<string, object>))
            {
                schema.Type = JsonSchemaType.Object;
                schema.AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Number | JsonSchemaType.Boolean };
            }
            return Task.CompletedTask;
        });
        return options;
    }
}
