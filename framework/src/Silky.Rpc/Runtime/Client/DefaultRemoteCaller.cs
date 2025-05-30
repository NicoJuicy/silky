using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Silky.Core;
using Silky.Core.Exceptions;
using Silky.Core.Logging;
using Silky.Core.Runtime.Rpc;
using Silky.Core.Serialization;
using Silky.Core.Utils;
using Silky.Rpc.Endpoint;
using Silky.Rpc.Endpoint.Selector;
using Silky.Rpc.Extensions;
using Silky.Rpc.Runtime.Server;
using Silky.Rpc.Transport;
using Silky.Rpc.Transport.Messages;

namespace Silky.Rpc.Runtime.Client
{
    internal class DefaultRemoteCaller : IRemoteCaller
    {
        private readonly IServerManager _serverManager;
        private readonly ISerializer _serializer;
        private readonly IClientRemoteInvokerFactory _clientRemoteInvokerFactory;
        private readonly ITransportClientFactory _transportClientFactory;
        private readonly IClientInvokeDiagnosticListener _clientInvokeDiagnosticListener;

        public ILogger<DefaultRemoteCaller> Logger { get; set; }

        public DefaultRemoteCaller(IServerManager serverManager,
            ISerializer serializer,
            IClientRemoteInvokerFactory clientRemoteInvokerFactory,
            ITransportClientFactory transportClientFactory,
            IClientInvokeDiagnosticListener clientInvokeDiagnosticListener)
        {
            _serverManager = serverManager;
            _serializer = serializer;
            _clientRemoteInvokerFactory = clientRemoteInvokerFactory;
            _transportClientFactory = transportClientFactory;
            _clientInvokeDiagnosticListener = clientInvokeDiagnosticListener;

            Logger = NullLogger<DefaultRemoteCaller>.Instance;
        }

        public async Task<object?> InvokeAsync(RemoteInvokeMessage remoteInvokeMessage,
            ShuntStrategy shuntStrategy, string? hashKey = null)
        {
            var sp = Stopwatch.StartNew();
            var messageId = GuidGenerator.CreateGuidStrWithNoUnderline();
            Logger.LogDebug(
                "The rpc request call start{0} serviceEntryId:[{1}]",
                args: new[] { Environment.NewLine, remoteInvokeMessage.ServiceEntryId });
            ClientInvokeInfo? clientInvokeInfo = null;
            ISilkyEndpoint? selectedRpcEndpoint = null;
            long? tracingTimestamp = null;
            IRemoteInvoker remoteInvoker;
            var invokeMonitor = EngineContext.Current.Resolve<IInvokeMonitor>();
            try
            {
                var rpcEndpoints = FindRpcEndpoint(remoteInvokeMessage);
                selectedRpcEndpoint =
                    SelectedRpcEndpoint(rpcEndpoints, shuntStrategy, remoteInvokeMessage.ServiceEntryId, hashKey,
                        out var confirmedShuntStrategy);
                RpcContext.Context.SetRcpInvokeAddressInfo(selectedRpcEndpoint.Descriptor);
                tracingTimestamp = _clientInvokeDiagnosticListener.TracingBefore(remoteInvokeMessage, messageId);
                clientInvokeInfo = invokeMonitor?.Monitor((remoteInvokeMessage.ServiceEntryId, selectedRpcEndpoint));
                _clientInvokeDiagnosticListener.TracingSelectInvokeAddress(tracingTimestamp,
                    remoteInvokeMessage.ServiceEntryId, confirmedShuntStrategy,
                    rpcEndpoints, selectedRpcEndpoint);

                var client = await _transportClientFactory.GetClient(selectedRpcEndpoint);
                remoteInvoker =
                    _clientRemoteInvokerFactory.CreateInvoker(new ClientInvokeContext(remoteInvokeMessage,
                        confirmedShuntStrategy,
                        hashKey), client, messageId);

                await remoteInvoker.InvokeAsync();
            }
            catch (Exception ex)
            {
                _clientInvokeDiagnosticListener.TracingError(tracingTimestamp, messageId,
                    remoteInvokeMessage.ServiceEntryId, ex.GetExceptionStatusCode(), ex);

                invokeMonitor?.ExecFail((remoteInvokeMessage.ServiceEntryId, selectedRpcEndpoint),
                    sp.Elapsed.TotalMilliseconds, clientInvokeInfo);
                if (ex.IsFriendlyException())
                {
                    Logger.LogWarning(ex.Message);
                }
                else
                {
                    Logger.LogException(ex);
                }

                throw;
            }
            finally
            {
                sp.Stop();
            }

            invokeMonitor?.ExecSuccess((remoteInvokeMessage.ServiceEntryId, selectedRpcEndpoint),
                sp.Elapsed.TotalMilliseconds, clientInvokeInfo);
            var invokeResult = remoteInvoker.RemoteResult;
            _clientInvokeDiagnosticListener.TracingAfter(tracingTimestamp, messageId,
                remoteInvokeMessage.ServiceEntryId, invokeResult);
            return invokeResult.Result;
        }

        private ISilkyEndpoint[] FindRpcEndpoint(RemoteInvokeMessage remoteInvokeMessage)
        {
            var rpcEndpoints = _serverManager.GetRpcEndpoints(remoteInvokeMessage.ServiceId, ServiceProtocol.Rpc);
            if (rpcEndpoints == null || !rpcEndpoints.Any())
            {
                throw new NotFindServiceRouteException(
                    $"The service routing could not be found via [{remoteInvokeMessage.ServiceId}]");
            }

            return rpcEndpoints;
        }

        private ISilkyEndpoint SelectedRpcEndpoint(ISilkyEndpoint[] rpcEndpoints, ShuntStrategy shuntStrategy,
            string serviceEntryId, string? hashKey, out ShuntStrategy confirmedShuntStrategy)
        {
            var remoteAddress = RpcContext.Context.GetInvokeAttachment(AttachmentKeys.SelectedServerEndpoint);
            ISilkyEndpoint? selectedSilkyEndpoint;
            if (remoteAddress != null)
            {
                selectedSilkyEndpoint =
                    rpcEndpoints.FirstOrDefault(p => p.GetAddress().Equals(remoteAddress) && p.Enabled);

                if (selectedSilkyEndpoint == null)
                {
                    throw new NotFindServiceRouteAddressException(
                        $"Server [{serviceEntryId}] does not have a healthy designated service rpcEndpoint [{remoteAddress}]");
                }

                confirmedShuntStrategy = ShuntStrategy.Appoint;
            }
            else
            {
                var addressSelector =
                    EngineContext.Current.ResolveNamed<IRpcEndpointSelector>(shuntStrategy.ToString());

                selectedSilkyEndpoint = addressSelector.Select(new RpcEndpointSelectContext(serviceEntryId,
                    rpcEndpoints,
                    hashKey));
                confirmedShuntStrategy = shuntStrategy;
            }

            Logger.LogDebug(
                "There are currently available service provider addresses:{0}{1}" +
                "The selected service provider rpcEndpoint is:[{2}]",
                args: new[]
                {
                    _serializer.Serialize(rpcEndpoints.Where(p => p.Enabled).Select(p => p.ToString())),
                    Environment.NewLine,
                    selectedSilkyEndpoint!.ToString()
                });
            RpcContext.Context.SetRcpInvokeAddressInfo(selectedSilkyEndpoint.Descriptor);
            return selectedSilkyEndpoint;
        }
    }
}