using Microsoft.AspNetCore.Authorization;
using MSAVA_API.Handlers;

namespace MSAVA_API.Authorization;

public static class ApiAuthorizationOptions
{
    public static void Configure(AuthorizationOptions options)
    {
        var authenticatedUserPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new NotBannedRequirement())
            .Build();

        options.DefaultPolicy = authenticatedUserPolicy;
        options.FallbackPolicy = authenticatedUserPolicy;

        options.AddPolicy(AuthorizationPolicies.CurrentAdmin, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new NotBannedRequirement(), new CurrentAdminRequirement());
        });
    }
}
