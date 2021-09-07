﻿using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Silky.Core.Modularity;
using Silky.Http.Core;

namespace Silky.Http.CorsAccessor
{
    [DependsOn(typeof(SilkyHttpCoreModule))]
    public class CorsModule : WebSilkyModule
    {
        public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddCorsAccessor(configuration);
        }

        public override void Configure(IApplicationBuilder application)
        {
            application.UseCorsAccessor();
        }
    }
}