using System.Collections.Generic;
using Polly;
using Silky.Rpc.Runtime.Server;

namespace Silky.Rpc.Runtime.Client
{
    internal sealed class DefaultInvokePolicyBuilder : IInvokePolicyBuilder
    {
        private readonly ICollection<IPolicyWithResultProvider> _policyWithResultProviders;

        private readonly ICollection<IPolicyProvider> _policyProviders;

        private readonly ICollection<ICircuitBreakerPolicyProvider> _circuitBreakerPolicyProviders;

        public DefaultInvokePolicyBuilder(
            ICollection<IPolicyProvider> policyProviders,
            ICollection<IPolicyWithResultProvider> policyWithResultProviders,
            ICollection<ICircuitBreakerPolicyProvider> circuitBreakerPolicyProviders)
        {
            _policyProviders = policyProviders;
            _policyWithResultProviders = policyWithResultProviders;
            _circuitBreakerPolicyProviders = circuitBreakerPolicyProviders;
        }


        public IAsyncPolicy<object> Build(ServiceEntry serviceEntry, object[] parameters)
        {
            IAsyncPolicy<object> policy = Policy.NoOpAsync<object>();

            foreach (var policyProvider in _policyWithResultProviders)
            {
                var policyItem = policyProvider.Create(serviceEntry, parameters);
                if (policyItem != null)
                {
                    policy = policy.WrapAsync(policyItem);
                }
            }

            foreach (var policyProvider in _policyProviders)
            {
                var policyItem = policyProvider.Create(serviceEntry, parameters);
                if (policyItem != null)
                {
                    policy = policy.WrapAsync(policyItem);
                }
            }

            foreach (var circuitBreakerPolicyProvider in _circuitBreakerPolicyProviders)
            {
                var policyItem = circuitBreakerPolicyProvider.Create(serviceEntry, parameters);
                policy = policy.WrapAsync(policyItem);
            }

            return policy;
        }
    }
}