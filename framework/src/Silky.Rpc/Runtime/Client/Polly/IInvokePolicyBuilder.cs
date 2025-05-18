using Polly;
using Silky.Core.DependencyInjection;

namespace Silky.Rpc.Runtime.Client
{
    public interface IInvokePolicyBuilder : ISingletonDependency
    {
        IAsyncPolicy<object?> Build(string serviceEntryId);

        IAsyncPolicy<object?> Build(string serviceEntryId, object[] parameters);
    }
}