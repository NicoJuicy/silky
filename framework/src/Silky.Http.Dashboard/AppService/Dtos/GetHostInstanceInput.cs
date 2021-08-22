using Silky.Rpc.Runtime.Server;

namespace Silky.Http.Dashboard.AppService.Dtos
{
    public class GetHostInstanceInput : PagedRequestDto
    {
        
        public ServiceProtocol ServiceProtocol { get; set; } = ServiceProtocol.Tcp;
    }
}