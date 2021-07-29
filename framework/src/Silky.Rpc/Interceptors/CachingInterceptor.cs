﻿using System.Linq;
using System.Threading.Tasks;
using Silky.Caching;
using Silky.Core;
using Silky.Core.DependencyInjection;
using Silky.Core.DynamicProxy;
using Silky.Core.Extensions;
using Silky.Rpc.MiniProfiler;
using Silky.Rpc.Runtime.Server;

namespace Silky.Rpc.Interceptors
{
    public class CachingInterceptor : SilkyInterceptor, ITransientDependency
    {
        private readonly IDistributedInterceptCache _distributedCache;

        public CachingInterceptor(IDistributedInterceptCache distributedCache)
        {
            _distributedCache = distributedCache;
        }

        public override async Task InterceptAsync(ISilkyMethodInvocation invocation)
        {
            var serviceEntry = invocation.ArgumentsDictionary["serviceEntry"] as ServiceEntry;
            var serviceKey = invocation.ArgumentsDictionary["serviceKey"] as string;
            var parameters = invocation.ArgumentsDictionary["parameters"] as object[];

            async Task<object> GetResultFirstFromCache(string cacheName, string cacheKey, ServiceEntry entry)
            {
                _distributedCache.UpdateCacheName(cacheName);
                return await _distributedCache.GetOrAddAsync(cacheKey,
                    serviceEntry.MethodInfo.GetReturnType(),
                    async () => await entry.Executor(serviceKey, parameters));
            }

            if (serviceEntry.GovernanceOptions.CacheEnabled)
            {
                var removeCachingInterceptProviders = serviceEntry.RemoveCachingInterceptProviders;
                if (removeCachingInterceptProviders.Any())
                {
                    var index = 1;
                    foreach (var removeCachingInterceptProvider in removeCachingInterceptProviders)
                    {
                        var removeCacheKey =
                            serviceEntry.GetCachingInterceptKey(parameters, removeCachingInterceptProvider);
                        await _distributedCache.RemoveAsync(removeCacheKey, removeCachingInterceptProvider.CacheName,
                            true);
                        MiniProfilerPrinter.Print(MiniProfileConstant.Caching.Name,
                            MiniProfileConstant.Caching.State.RemoveCaching + index,
                            $"Remove the cache with key {removeCacheKey}");
                        index++;
                    }
                }

                if (serviceEntry.GetCachingInterceptProvider != null)
                {
                    if (serviceEntry.IsTransactionServiceEntry())
                    {
                        MiniProfilerPrinter.Print(MiniProfileConstant.Caching.Name,
                            MiniProfileConstant.Caching.State.GetCaching,
                            $"Cache interception is invalid in distributed transaction processing");
                        await invocation.ProceedAsync();
                    }
                    else
                    {
                        var getCacheKey = serviceEntry.GetCachingInterceptKey(parameters,
                            serviceEntry.GetCachingInterceptProvider);
                        MiniProfilerPrinter.Print(MiniProfileConstant.Caching.Name,
                            MiniProfileConstant.Caching.State.GetCaching,
                            $"Ready to get data from the cache service:[cacheName=>{serviceEntry.GetCacheName()};cacheKey=>{getCacheKey}]");
                        invocation.ReturnValue = await GetResultFirstFromCache(
                            serviceEntry.GetCacheName(),
                            getCacheKey,
                            serviceEntry);
                    }
                }
                else if (serviceEntry.UpdateCachingInterceptProvider != null)
                {
                    if (serviceEntry.IsTransactionServiceEntry())
                    {
                        MiniProfilerPrinter.Print(MiniProfileConstant.Caching.Name,
                            MiniProfileConstant.Caching.State.UpdateCaching,
                            $"Cache interception is invalid in distributed transaction processing");
                        await invocation.ProceedAsync();
                    }
                    else
                    {
                        var updateCacheKey = serviceEntry.GetCachingInterceptKey(parameters,
                            serviceEntry.UpdateCachingInterceptProvider);
                        MiniProfilerPrinter.Print(MiniProfileConstant.Caching.Name,
                            MiniProfileConstant.Caching.State.UpdateCaching,
                            $"The cacheKey for updating the cache data is[cacheName=>{serviceEntry.GetCacheName()};cacheKey=>{updateCacheKey}]");
                        await _distributedCache.RemoveAsync(updateCacheKey, serviceEntry.GetCacheName(),
                            hideErrors: true);
                        invocation.ReturnValue = await GetResultFirstFromCache(
                            serviceEntry.GetCacheName(),
                            updateCacheKey,
                            serviceEntry);
                    }
                }
                else
                {
                    await invocation.ProceedAsync();
                }
            }
            else
            {
                await invocation.ProceedAsync();
            }
        }
    }
}