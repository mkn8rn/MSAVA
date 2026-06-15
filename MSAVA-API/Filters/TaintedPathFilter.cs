using MSAVA_API.Attributes;
using MSAVA_INF.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Reflection;

namespace MSAVA_API.Filters
{
    public class TaintedPathFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            var parameters = context.ActionDescriptor.Parameters;
            foreach (var param in parameters)
            {
                var paramInfo = param as ControllerParameterDescriptor;
                if (paramInfo?.ParameterInfo.GetCustomAttribute<TaintedPathCheckAttribute>() is null)
                    continue;

                if (paramInfo.ParameterInfo.ParameterType != typeof(string))
                {
                    throw new InvalidOperationException(
                        $"[TaintedPathCheck] can only be applied to string parameters. Parameter '{param.Name}' is of type '{paramInfo.ParameterInfo.ParameterType.Name}'.");
                }

                if (!context.ActionArguments.TryGetValue(param.Name, out var value) ||
                    value is not string fileNameWithExtension ||
                    !FileContentUtils.TryGetSafeFullPath(fileNameWithExtension, out _))
                {
                    context.Result = new BadRequestObjectResult("Invalid parameter.");
                    return;
                }
            }
        }

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}
