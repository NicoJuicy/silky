using System;
using Lms.Core;
using Lms.Rpc.Address;
using Lms.Rpc.Configuration;
using Microsoft.Extensions.Options;

namespace Lms.Rpc.Runtime.Server.ServiceDiscovery
{
    [AttributeUsage(AttributeTargets.Interface)]
    public class ServiceRouteAttribute : Attribute, IRouteTemplateProvider
    {
        public ServiceRouteAttribute(string template = "api/{appservice}",
            bool multipleServiceKey = false)
        {
            Template = template;
            MultipleServiceKey = multipleServiceKey;
            var rpcOptions = EngineContext.Current.Resolve<IOptions<RpcOptions>>().Value;
            RpcPort = rpcOptions.RpcPort;
        }

        public string Template { get; }

        public virtual ServiceProtocol ServiceProtocol { get; } = ServiceProtocol.Tcp;

        public virtual int RpcPort { get; protected set; }

        public bool MultipleServiceKey { get; }
    }
}