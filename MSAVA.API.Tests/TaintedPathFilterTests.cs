using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using MSAVA_API.Attributes;
using MSAVA_API.Filters;

namespace MSAVA_API.Tests;

public class TaintedPathFilterTests
{
    [Test]
    public void OnActionExecuting_AllowsSafeFileName()
    {
        var context = CreateContext(nameof(SafeFileAction), "fileNameWithExtension", "safe-file.pdf");
        var filter = new TaintedPathFilter();

        filter.OnActionExecuting(context);

        context.Result.Should().BeNull();
    }

    [TestCase("../secret.pdf")]
    [TestCase("folder/secret.pdf")]
    [TestCase("folder\\secret.pdf")]
    [TestCase("")]
    [TestCase(" ")]
    public void OnActionExecuting_RejectsUnsafeFileName(string fileNameWithExtension)
    {
        var context = CreateContext(nameof(SafeFileAction), "fileNameWithExtension", fileNameWithExtension);
        var filter = new TaintedPathFilter();

        filter.OnActionExecuting(context);

        var result = context.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        result.Value.Should().Be("Invalid parameter.");
    }

    [Test]
    public void OnActionExecuting_RejectsMissingTaintedPathArgument()
    {
        var context = CreateContext(nameof(SafeFileAction), "otherArgument", "safe-file.pdf");
        var filter = new TaintedPathFilter();

        filter.OnActionExecuting(context);

        context.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void OnActionExecuting_RejectsNullTaintedPathArgument()
    {
        var context = CreateContext(nameof(SafeFileAction), "fileNameWithExtension", null);
        var filter = new TaintedPathFilter();

        filter.OnActionExecuting(context);

        context.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void OnActionExecuting_ThrowsWhenAttributeIsAppliedToNonStringParameter()
    {
        var context = CreateContext(nameof(InvalidAction), "fileId", 1);
        var filter = new TaintedPathFilter();

        var act = () => filter.OnActionExecuting(context);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*fileId*Int32*");
    }

    private static ActionExecutingContext CreateContext(string actionName, string argumentName, object? argumentValue)
    {
        var method = typeof(TaintedPathFilterTests).GetMethod(actionName)
            ?? throw new InvalidOperationException($"Test action {actionName} was not found.");

        var parameter = method.GetParameters().Single();
        var actionDescriptor = new ControllerActionDescriptor
        {
            MethodInfo = method,
            Parameters =
            [
                new ControllerParameterDescriptor
                {
                    Name = parameter.Name!,
                    ParameterInfo = parameter,
                    ParameterType = parameter.ParameterType
                }
            ]
        };

        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), actionDescriptor);
        var actionArguments = new Dictionary<string, object?>
        {
            [argumentName] = argumentValue
        };

        return new ActionExecutingContext(actionContext, [], actionArguments, controller: null!);
    }

    public static void SafeFileAction([TaintedPathCheck] string fileNameWithExtension)
    {
    }

    public static void InvalidAction([TaintedPathCheck] int fileId)
    {
    }
}
