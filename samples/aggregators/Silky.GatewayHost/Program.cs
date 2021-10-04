﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Silky.Core;
using Silky.Core.Extensions;

namespace Silky.GatewayHost
{
    class Program
    {
        public async static Task Main(string[] args)
        {
            await CreateHostBuilder(args).Build().RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var hostBuilder = Host.CreateDefaultBuilder(args)
                .ConfigureSilkyWebHostDefaults()
                .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); })
                .UseSerilogDefault();
            if (EngineContext.Current.IsEnvironment("Apollo"))
            {
               // hostBuilder.AddApollo();
            }

            return hostBuilder;
        }
    }
}