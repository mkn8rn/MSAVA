using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi.Models;
using MSAVA_API.Controllers;
using MSAVA_API.Filters;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MSAVA_API.Tests;

public class AuthorizeCheckOperationFilterTests
{
    [Test]
    public void Apply_LeavesAnonymousActionsWithoutSecurityRequirement()
    {
        var operation = new OpenApiOperation();

        ApplyFilter(operation, typeof(AuthenticationController).GetMethod(nameof(AuthenticationController.Login)));

        operation.Security.Should().BeNullOrEmpty();
    }

    [Test]
    public void Apply_AddsBearerSecurityRequirementToProtectedActions()
    {
        var operation = new OpenApiOperation();

        ApplyFilter(operation, typeof(UsersController).GetMethod(nameof(UsersController.GetCurrentSession)));

        var requirement = operation.Security.Should().ContainSingle().Subject;
        requirement.Should().ContainSingle(pair =>
            pair.Key.Reference.Type == ReferenceType.SecurityScheme &&
            pair.Key.Reference.Id == "Bearer" &&
            pair.Value.Count == 0);
    }

    private static void ApplyFilter(OpenApiOperation operation, MethodInfo? method)
    {
        method.Should().NotBeNull();

        var context = new OperationFilterContext(
            new ApiDescription(),
            null!,
            new SchemaRepository(),
            method!);

        new AuthorizeCheckOperationFilter().Apply(operation, context);
    }
}
