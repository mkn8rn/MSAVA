using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using MSAVA_API.Authorization;
using MSAVA_API.Controllers;
using MSAVA_API.Handlers;

namespace MSAVA_API.Tests;

public class AdminAuthorizationPolicyTests
{
    [Test]
    public void ApiAuthorization_UsesAuthenticatedNotBannedFallbackPolicy()
    {
        var options = new AuthorizationOptions();

        ApiAuthorizationOptions.Configure(options);

        options.DefaultPolicy.Should().NotBeNull();
        options.FallbackPolicy.Should().NotBeNull();
        AssertRequiresAuthenticatedNotBannedUser(options.DefaultPolicy);
        AssertRequiresAuthenticatedNotBannedUser(options.FallbackPolicy!);
    }

    [Test]
    public void ApiAuthorization_CurrentAdminPolicyUsesDatabaseBackedRequirements()
    {
        var options = new AuthorizationOptions();

        ApiAuthorizationOptions.Configure(options);

        var policy = options.GetPolicy(AuthorizationPolicies.CurrentAdmin);
        policy.Should().NotBeNull();
        policy!.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Should().ContainSingle();
        policy.Requirements.OfType<NotBannedRequirement>().Should().ContainSingle();
        policy.Requirements.OfType<CurrentAdminRequirement>().Should().ContainSingle();
    }

    [Test]
    public void AuthenticationController_ExplicitlyAllowsAnonymousAccess()
    {
        typeof(AuthenticationController)
            .GetCustomAttributes<AllowAnonymousAttribute>()
            .Should()
            .ContainSingle();
    }

    [Test]
    public void ApiControllers_DoNotUseTokenRoleBasedAdminAuthorization()
    {
        var roleBasedAdminAttributes = typeof(UsersController).Assembly
            .GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>())
            .Where(attribute => attribute.Roles == "Admin")
            .ToList();

        roleBasedAdminAttributes.Should().BeEmpty();
    }

    [Test]
    public void AdminEndpoints_UseCurrentAdminPolicy()
    {
        AssertUsesCurrentAdminPolicy(typeof(UsersController), nameof(UsersController.GetAll));
        AssertUsesCurrentAdminPolicy(typeof(InviteCodeController), nameof(InviteCodeController.GetRemainingUses));
        AssertUsesCurrentAdminPolicy(typeof(InviteCodeController), nameof(InviteCodeController.CreateInviteCode));
        AssertUsesCurrentAdminPolicy(typeof(InviteCodeController), nameof(InviteCodeController.GetAllInviteCodes));
        AssertUsesCurrentAdminPolicy(typeof(InviteCodeController), nameof(InviteCodeController.GetInviteCodeById));
    }

    private static void AssertUsesCurrentAdminPolicy(Type controllerType, string methodName)
    {
        var method = controllerType.GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {controllerType.Name}.{methodName} was not found.");

        method.GetCustomAttributes<AuthorizeAttribute>()
            .Should()
            .ContainSingle(attribute => attribute.Policy == AuthorizationPolicies.CurrentAdmin);
    }

    private static void AssertRequiresAuthenticatedNotBannedUser(AuthorizationPolicy policy)
    {
        policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Should().ContainSingle();
        policy.Requirements.OfType<NotBannedRequirement>().Should().ContainSingle();
    }
}
