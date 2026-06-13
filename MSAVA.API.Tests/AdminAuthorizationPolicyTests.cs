using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSAVA_API.Authorization;
using MSAVA_API.Controllers;

namespace MSAVA_API.Tests;

public class AdminAuthorizationPolicyTests
{
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
}
