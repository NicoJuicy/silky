using System.Linq;
using System.Threading.Tasks;
using Silky.Core;
using Silky.Core.Exceptions;
using Silky.Core.Extensions;
using Silky.Core.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Silky.Core.Modularity;
using Silky.Http.Core.Configuration;
using Silky.Rpc.MiniProfiler;
using Silky.Rpc.Routing;
using Silky.Rpc.Runtime.Server;
using Silky.Rpc.Utils;

namespace Silky.Http.Core.Middlewares
{
    public static class ApplicationBuilderExtensions
    {
        public static void UseSilkyExceptionHandler(this IApplicationBuilder application)
        {
            GatewayOptions gatewayOptions = default;
            gatewayOptions = EngineContext.Current.GetOptionsMonitor<GatewayOptions>((options, name) =>
            {
                gatewayOptions = options;
            });

            var serializer = EngineContext.Current.Resolve<ISerializer>();

            application.UseExceptionHandler(handler =>
            {
                if (EngineContext.Current.Resolve<IModuleContainer>().Modules.Any(p => p.Name == "MiniProfiler"))
                {
                    handler.UseMiniProfiler();
                }

                handler.Run(context =>
                {
                    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                    if (exception == null)
                        return Task.CompletedTask;
                    context.Response.ContentType = context.GetResponseContentType(gatewayOptions);
                    MiniProfilerPrinter.Print("Error", "Exception", exception.Message, true);

                    if (gatewayOptions.WrapResult)
                    {
                        var responseResultDto = new ResponseResultDto()
                        {
                            Status = exception.GetExceptionStatusCode(),
                            ErrorMessage = exception.Message
                        };
                        if (exception is IHasValidationErrors)
                        {
                            responseResultDto.ValidErrors = ((IHasValidationErrors)exception).GetValidateErrors();
                        }

                        var responseResultData = serializer.Serialize(responseResultDto);
                        context.Response.ContentLength = responseResultData.GetBytes().Length;
                        context.Response.StatusCode = ResponseStatusCode.Success;
                        context.Response.SetResultCode(exception.GetExceptionStatusCode());
                        return context.Response.WriteAsync(responseResultData);
                    }
                    else
                    {
                        context.Response.ContentType = "text/plain";
                        context.Response.SetResultCode(exception.GetExceptionStatusCode());
                        context.Response.StatusCode = exception.IsBusinessException()
                            ? ResponseStatusCode.BadCode
                            : exception.IsUnauthorized()
                                ? ResponseStatusCode.Unauthorized
                                : ResponseStatusCode.InternalServerError;

                        if (exception is IHasValidationErrors)
                        {
                            var validateErrors = exception.GetValidateErrors();
                            var responseResultData = serializer.Serialize(validateErrors);
                            context.Response.ContentLength = responseResultData.GetBytes().Length;
                            return context.Response.WriteAsync(responseResultData);
                        }

                        context.Response.ContentLength = exception.Message.GetBytes().Length;
                        return context.Response.WriteAsync(exception.Message);
                    }
                });
            });
        }

        public static async void RegisterHttpRoutes(this IApplicationBuilder application)
        {
            var serviceRouteRegisterProvider =
                application.ApplicationServices.GetRequiredService<IServiceRouteRegisterProvider>();
            await serviceRouteRegisterProvider.RegisterHttpRoutes();
        }
    }
}