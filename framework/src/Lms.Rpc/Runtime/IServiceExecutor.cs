using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Lms.Core.DependencyInjection;
using Lms.Rpc.Runtime.Server;
using Lms.Rpc.Runtime.Server.Parameter;

namespace Lms.Rpc.Runtime
{
    public interface IServiceExecutor : ITransientDependency
    {
        Task<object> Execute(ServiceEntry serviceEntry, IDictionary<ParameterFrom, object> requestParameters);

        Task<object> Execute(ServiceEntry serviceEntry, IList<object> parameters, [CanBeNull] string serviceKey = null);
    }
}