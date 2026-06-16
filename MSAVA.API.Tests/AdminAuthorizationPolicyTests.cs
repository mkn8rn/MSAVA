using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using MSAVA_API.Authorization;
using MSAVA_API.Controllers;
using MSAVA_API.Handlers;

namespace MSAVA_API.Tests;

public class AdminAuthorizationPolicyTests
{
    [Test]
    public void ApiAuthorization_UsesAuthenticatedCurrentUserAccessFallbackPolicy()
    {
        var options = new AuthorizationOptions();

        ApiAuthorizationOptions.Configure(options);

        options.DefaultPolicy.Should().NotBeNull();
        options.FallbackPolicy.Should().NotBeNull();
        AssertRequiresAuthenticatedCurrentUserAccess(options.DefaultPolicy);
        AssertRequiresAuthenticatedCurrentUserAccess(options.FallbackPolicy!);
    }

    [Test]
    public void ApiAuthorization_CurrentAdminPolicyUsesDatabaseBackedRequirements()
    {
        var options = new AuthorizationOptions();

        ApiAuthorizationOptions.Configure(options);

        var policy = options.GetPolicy(AuthorizationPolicies.CurrentAdmin);
        policy.Should().NotBeNull();
        policy!.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Should().ContainSingle();
        policy.Requirements.OfType<CurrentUserAccessRequirement>().Should().ContainSingle();
        policy.Requirements.OfType<CurrentAdminRequirement>().Should().ContainSingle();
    }

    [Test]
    public void ApiAuthorization_CurrentUserPolicyMatchesDefaultAuthenticatedAccess()
    {
        var options = new AuthorizationOptions();

        ApiAuthorizationOptions.Configure(options);

        var policy = options.GetPolicy(AuthorizationPolicies.CurrentUser);
        policy.Should().NotBeNull();
        AssertRequiresAuthenticatedCurrentUserAccess(policy!);
    }

    [Test]
    public void AuthenticationController_AllowsAnonymousOnlyOnLoginAndRegisterActions()
    {
        typeof(AuthenticationController)
            .GetCustomAttributes<AllowAnonymousAttribute>()
            .Should()
            .BeEmpty();

        AssertAllowsAnonymous(typeof(AuthenticationController), nameof(AuthenticationController.Login));
        AssertAllowsAnonymous(typeof(AuthenticationController), nameof(AuthenticationController.Register));
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
    public void ApiControllers_DeclareAuthorizationIntent()
    {
        var controllersWithoutAuthorizationIntent = GetControllerTypes()
            .Where(type =>
                !ControllerDeclaresAuthorizationIntent(type))
            .Select(type => type.Name)
            .ToList();

        controllersWithoutAuthorizationIntent.Should().BeEmpty(
            "every API controller should explicitly require current-user access or allow anonymous access");
    }

    [Test]
    public void ApiControllers_UseNamedAuthorizationPolicies()
    {
        var authorizeAttributesWithoutNamedPolicy = GetControllerTypes()
            .SelectMany(GetControllerAndActionAuthorizeAttributes)
            .Where(attribute =>
                attribute.Policy != AuthorizationPolicies.CurrentUser &&
                attribute.Policy != AuthorizationPolicies.CurrentAdmin)
            .ToList();

        authorizeAttributesWithoutNamedPolicy.Should().BeEmpty(
            "controller authorization should use the database-backed current user or current admin policies");
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

    [Test]
    public void ProtectedControllers_UseCurrentUserPolicy()
    {
        AssertUsesCurrentUserPolicy(typeof(AccessGroupsController));
        AssertUsesCurrentUserPolicy(typeof(FilesCheckController));
        AssertUsesCurrentUserPolicy(typeof(FilesImportController));
        AssertUsesCurrentUserPolicy(typeof(FilesRetrieveController));
        AssertUsesCurrentUserPolicy(typeof(FilesStoreController));
        AssertUsesCurrentUserPolicy(typeof(InviteCodeController));
        AssertUsesCurrentUserPolicy(typeof(UsersController));
    }

    private static void AssertUsesCurrentUserPolicy(Type controllerType)
    {
        controllerType.GetCustomAttributes<AuthorizeAttribute>()
            .Should()
            .ContainSingle(attribute => attribute.Policy == AuthorizationPolicies.CurrentUser);
    }

    private static void AssertUsesCurrentAdminPolicy(Type controllerType, string methodName)
    {
        var method = controllerType.GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {controllerType.Name}.{methodName} was not found.");

        method.GetCustomAttributes<AuthorizeAttribute>()
            .Should()
            .ContainSingle(attribute => attribute.Policy == AuthorizationPolicies.CurrentAdmin);
    }

    private static void AssertAllowsAnonymous(Type controllerType, string methodName)
    {
        var method = controllerType.GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {controllerType.Name}.{methodName} was not found.");

        method.GetCustomAttributes<AllowAnonymousAttribute>()
            .Should()
            .ContainSingle();
    }

    private static bool ControllerDeclaresAuthorizationIntent(Type controllerType)
    {
        if (controllerType.GetCustomAttributes<AuthorizeAttribute>().Any() ||
            controllerType.GetCustomAttributes<AllowAnonymousAttribute>().Any())
        {
            return true;
        }

        var actionMethods = controllerType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();

        return actionMethods.Count > 0 &&
            actionMethods.All(method =>
                method.GetCustomAttributes<AuthorizeAttribute>().Any() ||
                method.GetCustomAttributes<AllowAnonymousAttribute>().Any());
    }

    private static IReadOnlyList<Type> GetControllerTypes()
    {
        return typeof(UsersController).Assembly
            .GetTypes()
            .Where(type =>
                typeof(ControllerBase).IsAssignableFrom(type) &&
                !type.IsAbstract &&
                type.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToList();
    }

    private static IEnumerable<AuthorizeAttribute> GetControllerAndActionAuthorizeAttributes(Type controllerType)
    {
        foreach (var attribute in controllerType.GetCustomAttributes<AuthorizeAttribute>())
            yield return attribute;

        foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            foreach (var attribute in method.GetCustomAttributes<AuthorizeAttribute>())
                yield return attribute;
        }
    }

    private static void AssertRequiresAuthenticatedCurrentUserAccess(AuthorizationPolicy policy)
    {
        policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Should().ContainSingle();
        policy.Requirements.OfType<CurrentUserAccessRequirement>().Should().ContainSingle();
    }
}
