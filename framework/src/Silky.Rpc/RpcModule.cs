﻿using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Medallion.Threading;
using Silky.Core;
using Silky.Core.Exceptions;
using Silky.Core.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Silky.Rpc.Configuration;
using Silky.Rpc.Routing;
using Silky.Rpc.Runtime;
using Silky.Rpc.Runtime.Client;
using Silky.Rpc.Runtime.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Silky.Core.Runtime.Rpc;
using Silky.Rpc.Endpoint.Selector;
using Silky.Rpc.Extensions;
using Silky.Rpc.RegistryCenters.HeartBeat;
using Silky.Rpc.Transport.Messages;
using ServiceKeyAttribute = Silky.Rpc.Runtime.Server.ServiceKeyAttribute;

namespace Silky.Rpc
{
    public class RpcModule : SilkyModule
    {
        public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<RpcOptions>()
                .Bind(configuration.GetSection(RpcOptions.Rpc));
            services.AddOptions<GovernanceOptions>()
                .Bind(configuration.GetSection(GovernanceOptions.Governance));
            services.AddOptions<WebSocketOptions>()
                .Bind(configuration.GetSection(WebSocketOptions.WebSocket));

            services.AddDefaultMessageCodec();
            services.AddAuditing(configuration);
            services.TryAddSingleton<IHeartBeatService, DefaultHeartBeatService>();
            services.TryAddSingleton<IServerProvider, DefaultServerProvider>();
        }

        protected override void RegisterServices(ContainerBuilder builder)
        {
            var localServiceTypes = ServiceHelper.FindLocalServiceTypes(EngineContext.Current.TypeFinder)
                .ToArray();
            builder.RegisterTypes(localServiceTypes)
                .PropertiesAutowired()
                .AsSelf()
                .InstancePerDependency()
                .AsImplementedInterfaces();

            var serviceKeyTypes =
                localServiceTypes.Where(p => p.GetCustomAttributes().OfType<ServiceKeyAttribute>().Any());
            foreach (var serviceKeyType in serviceKeyTypes)
            {
                var serviceKeyAttribute = serviceKeyType.GetCustomAttributes().OfType<ServiceKeyAttribute>().First();
                builder.RegisterType(serviceKeyType)
                    .Named(serviceKeyAttribute.Name,
                        serviceKeyType.GetInterfaces().First(p =>
                            p.GetCustomAttributes().OfType<IRouteTemplateProvider>().Any()))
                    .InstancePerDependency()
                    .AsImplementedInterfaces()
                    ;
            }

            RegisterServicesForAddressSelector(builder);
            RegisterServicesExecutor(builder);
            RegisterServicesForParameterResolver(builder);
        }

        public override Task PreInitialize(ApplicationInitializationContext context)
        {
            if (!context.IsAddRegistryCenterService(out var registryCenterType))
            {
                throw new SilkyException(
                    $"You did not specify the dependent {registryCenterType} service registry module");
            }

            return !context.ServiceProvider.GetAutofacRoot().IsRegistered(typeof(IDistributedLockProvider))
                ? throw new SilkyException(
                    "You must specify the implementation of IDistributedLockProvider in the Silky.RegistryCenter project of the distributed transaction")
                : Task.CompletedTask;
        }

        public override async Task PostInitialize(ApplicationInitializationContext context)
        {
            var rpcOptions = context.ServiceProvider.GetService<IOptions<RpcOptions>>()?.Value;
            if (rpcOptions != null && rpcOptions.MinThreadPoolSize <= rpcOptions.MaxThreadPoolSize)
            {
                ThreadPool.SetMaxThreads(rpcOptions.MaxThreadPoolSize, rpcOptions.MaxThreadPoolSize);
                ThreadPool.SetMinThreads(rpcOptions.MinThreadPoolSize, rpcOptions.MinThreadPoolSize);
            }
            else
            {
                var minThreadPoolSize = Environment.ProcessorCount * 4;
                var maxThreadPoolSize = Environment.ProcessorCount * 10;
                ThreadPool.SetMaxThreads(maxThreadPoolSize, maxThreadPoolSize);
                ThreadPool.SetMinThreads(minThreadPoolSize, minThreadPoolSize);
            }
            var messageListeners = context.ServiceProvider.GetServices<IServerMessageListener>();
            var logger = context.ServiceProvider.GetService<ILogger<RpcModule>>();
            if (messageListeners.Any())
            {
                foreach (var messageListener in messageListeners)
                {
                    messageListener.Received += async (sender, message) =>
                    {
                        using var serviceScope = EngineContext.Current.ServiceProvider.CreateScope();
                        message.SetRpcMessageId();
                        var remoteInvokeMessage = message.GetContent<RemoteInvokeMessage>();
                        remoteInvokeMessage.SetRpcAttachments();
                        var rpcContextAccessor = EngineContext.Current.Resolve<IRpcContextAccessor>();
                        try
                        {
                            RpcContext.Context.RpcServices = serviceScope.ServiceProvider;
                            rpcContextAccessor.RpcContext = RpcContext.Context;
                            var serverDiagnosticListener =
                                EngineContext.Current.Resolve<IServerDiagnosticListener>();
                            var tracingTimestamp =
                                serverDiagnosticListener.TracingBefore(remoteInvokeMessage, message.Id);
                            var handlePolicyBuilder = EngineContext.Current.Resolve<IHandlePolicyBuilder>();
                            var policy = handlePolicyBuilder.Build(remoteInvokeMessage);
                            var pollyContext = new Context(PollyContextNames.ServerHandlerOperationKey)
                            {
                                [PollyContextNames.TracingTimestamp] = tracingTimestamp
                            };
                            var result = await policy.ExecuteAsync(async ct =>
                            {
                                var messageReceivedHandler =
                                    EngineContext.Current.Resolve<IServerMessageReceivedHandler>();
                                var remoteResultMessage =
                                    await messageReceivedHandler.Handle(remoteInvokeMessage, ct,
                                        CancellationToken.None);
                                return remoteResultMessage;
                            }, pollyContext);
                            var resultTransportMessage = new TransportMessage(result, message.Id);
                            await sender.SendMessageAsync(resultTransportMessage);
                            serverDiagnosticListener.TracingAfter(tracingTimestamp, message.Id,
                                remoteInvokeMessage.ServiceEntryId, result);
                        }
                        finally
                        {
                            RpcContext.Clear();
                            rpcContextAccessor.RpcContext = null;
                        }
                    };
                }
            }
        }

        private void RegisterServicesForAddressSelector(ContainerBuilder builder)
        {
            builder.RegisterType<PollingRpcEndpointSelector>()
                .SingleInstance()
                .AsSelf()
                .Named<IRpcEndpointSelector>(ShuntStrategy.Polling.ToString());
            builder.RegisterType<PollingRpcEndpointSelector>()
                .SingleInstance()
                .AsSelf()
                .Named<IRpcEndpointSelector>(ShuntStrategy.Random.ToString());
            builder.RegisterType<HashAlgorithmRpcEndpointSelector>()
                .SingleInstance()
                .AsSelf()
                .Named<IRpcEndpointSelector>(ShuntStrategy.HashAlgorithm.ToString());
        }

        private void RegisterServicesForParameterResolver(ContainerBuilder builder)
        {
            builder.RegisterType<TemplateParameterResolver>()
                .SingleInstance()
                .AsSelf()
                .Named<IParameterResolver>(ParameterType.Dict.ToString());

            builder.RegisterType<RpcParameterResolver>()
                .SingleInstance()
                .AsSelf()
                .Named<IParameterResolver>(ParameterType.Rpc.ToString());

            builder.RegisterType<HttpParameterResolver>()
                .SingleInstance()
                .AsSelf()
                .Named<IParameterResolver>(ParameterType.Http.ToString());
        }

        private void RegisterServicesExecutor(ContainerBuilder builder)
        {
            builder.RegisterType<DefaultLocalExecutor>()
                .As<ILocalExecutor>()
                .InstancePerDependency()
                ;

            builder.RegisterType<DefaultRemoteExecutor>()
                .As<IRemoteExecutor>()
                .InstancePerDependency()
                ;

            builder.RegisterType<DefaultExecutor>()
                .As<IExecutor>()
                .InstancePerDependency()
                ;
        }
    }
}
