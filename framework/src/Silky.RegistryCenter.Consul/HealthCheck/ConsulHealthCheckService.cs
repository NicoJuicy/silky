﻿using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Consul;
using Medallion.Threading;
using Microsoft.Extensions.Options;
using Silky.Core;
using Silky.Rpc.Configuration;

namespace Silky.RegistryCenter.Consul.HealthCheck;

public class ConsulHealthCheckService : IHealthCheckService
{
    private ConcurrentDictionary<string, int> _serverInstanceUnHealthCache = new();


    private GovernanceOptions GovernanceOptions => _governanceOptionsMonitor.CurrentValue;

    private IOptionsMonitor<GovernanceOptions> _governanceOptionsMonitor;

    private readonly IDistributedLockProvider _distributedLockProvider;

    public ConsulHealthCheckService(IDistributedLockProvider distributedLockProvider,
        IOptionsMonitor<GovernanceOptions> governanceOptionsMonitor)
    {
        _distributedLockProvider = distributedLockProvider;
        _governanceOptionsMonitor = governanceOptionsMonitor;
    }


    public async Task<string[]> Check(IConsulClient consulClient, string service)
    {
        var unHealthServiceIds = new List<string>();
        await using var handle = await _distributedLockProvider.TryAcquireLockAsync($"ConsulHealthCheck:{service}");

        if (handle == null) return unHealthServiceIds.ToArray();
        var result = await consulClient.Health.Checks(service);
        foreach (var healthCheck in result.Response)
        {
            var unHealthCount = _serverInstanceUnHealthCache.GetValueOrDefault(healthCheck.ServiceID, 0);
            if (healthCheck.Status.Equals(HealthStatus.Passing))
            {
                unHealthCount = 0;
            }
            else if (healthCheck.Status.Equals(HealthStatus.Critical))
            {
                unHealthServiceIds.Add(healthCheck.ServiceID);
                unHealthCount += 1;
            }

            _serverInstanceUnHealthCache.AddOrUpdate(healthCheck.ServiceID, unHealthCount, (k, v) => unHealthCount);
            if (unHealthCount >= GovernanceOptions.UnHealthAddressTimesAllowedBeforeRemoving)
            {
                await consulClient.Agent.ServiceDeregister(healthCheck.ServiceID);
                _serverInstanceUnHealthCache.TryRemove(healthCheck.ServiceID, out _);
            }
        }

        return unHealthServiceIds.ToArray();
    }
}