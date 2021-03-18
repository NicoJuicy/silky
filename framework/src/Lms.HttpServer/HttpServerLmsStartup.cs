using System;
using Lms.Core;
using Lms.HttpServer.Configuration;
using Lms.HttpServer.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.HttpServer
{
    public class HttpServerLmsStartup : ILmsStartup
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<GatewayOptions>()
                .Bind(configuration.GetSection(GatewayOptions.Gateway));
        }

        public void Configure(IApplicationBuilder application)
        {
            application.UseLmsExceptionHandler();
            application.UseLms();
        }

        public int Order { get; } = Int32.MinValue;
    }
}