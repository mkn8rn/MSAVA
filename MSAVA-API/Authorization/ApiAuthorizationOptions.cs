using Microsoft.AspNetCore.Authorization;
using MSAVA_API.Handlers;

namespace MSAVA_API.Authorization;

public static class ApiAuthorizationOptions
{
    public static void Configure(AuthorizationOptions options)
    {
        var authenticatedUserPolicy = BuildCurrentUserPolicy();

        options.DefaultPolicy = authenticatedUserPolicy;
        options.FallbackPolicy = authenticatedUserPolicy;
        options.AddPolicy(AuthorizationPolicies.CurrentUser, authenticatedUserPolicy);

        options.AddPolicy(AuthorizationPolicies.CurrentAdmin, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new CurrentUserAccessRequirement(), new CurrentAdminRequirement());
        });
    }

    private static AuthorizationPolicy BuildCurrentUserPolicy()
    {
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new CurrentUserAccessRequirement())
            .Build();
    }
}
