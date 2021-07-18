using Silky.Lms.Core.DependencyInjection;
using Silky.Lms.Rpc.Transport;

namespace Silky.Lms.Rpc.Runtime.Server
{
    public class CurrentServiceKey : ICurrentServiceKey, IScopedDependency
    {
        public string ServiceKey => RpcContext.GetContext().GetAttachment("serviceKey")?.ToString();

        public void Change(string seviceKey)
        {
            RpcContext.GetContext().SetAttachment(AttachmentKeys.ServiceKey, seviceKey);
        }
    }
}