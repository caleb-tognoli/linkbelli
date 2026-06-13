using Linkbelli.Api.Auth;
using Linkbelli.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Linkbelli.Api.OpenApi;

internal static class SecuritySchemeNames
{
    public const string Bearer = "Bearer";
    public const string ApiKey = "ApiKey";
}

/// <summary>Declares the two auth schemes so Scalar can render an Authorize panel.</summary>
public sealed class SecuritySchemeDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[SecuritySchemeNames.Bearer] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JSON Web Token",
            Description = "Paste the accessToken returned by POST /auth/login.",
        };
        document.Components.SecuritySchemes[SecuritySchemeNames.ApiKey] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyToken.HeaderName,
            Description = "An API key (the full lbk_... token) created via POST /me/apikeys.",
        };

        return Task.CompletedTask;
    }
}

/// <summary>
/// Marks each secured operation with the scheme(s) it accepts. Schemes are emitted
/// as separate requirements (OR semantics), matching the per-endpoint policies.
/// </summary>
public sealed class SecurityRequirementOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return Task.CompletedTask;
        }

        var authData = metadata.OfType<IAuthorizeData>().ToList();
        if (authData.Count == 0)
        {
            return Task.CompletedTask;
        }

        var schemes = new HashSet<string>();
        foreach (var ad in authData)
        {
            if (string.IsNullOrWhiteSpace(ad.AuthenticationSchemes))
            {
                schemes.Add(SecuritySchemeNames.Bearer);
                schemes.Add(SecuritySchemeNames.ApiKey);
                continue;
            }

            foreach (var s in ad.AuthenticationSchemes.Split(
                         ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (s == IdentityConstants.BearerScheme)
                {
                    schemes.Add(SecuritySchemeNames.Bearer);
                }
                else if (s == ApiKeyAuthenticationDefaults.Scheme)
                {
                    schemes.Add(SecuritySchemeNames.ApiKey);
                }
            }
        }

        if (schemes.Count == 0)
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        foreach (var scheme in schemes)
        {
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(scheme, context.Document)] = [],
            });
        }

        return Task.CompletedTask;
    }
}
