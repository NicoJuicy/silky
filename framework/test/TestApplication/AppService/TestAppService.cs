using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ITestApplication.Test;
using ITestApplication.Test.Dtos;
using Mapster;
using NormHostDemo.Tests;
using Silky.Caching;
using Silky.Core;
using Silky.Core.DbContext.UnitOfWork;
using Silky.Core.Exceptions;
using Silky.Core.Extensions;
using Silky.Core.Extensions.Collections.Generic;
using Silky.Core.Runtime.Rpc;
using Silky.Core.Runtime.Session;
using Silky.Core.Serialization;
using Silky.EntityFrameworkCore.Extensions;
using Silky.EntityFrameworkCore.Repositories;
using Silky.Rpc.Runtime.Client;
using Silky.Rpc.Runtime.Server;
using TestApplication.AppService.DomainService;

namespace TestApplication.AppService
{
    [ServiceKey("v1", 3)]
    public class TestAppService : ITestAppService
    {
        private readonly IDistributedCache<TestOut> _distributedCache;
        private readonly IRepository<Test> _testRepository;
        private readonly ISerializer _serializer;
        private readonly ISession _session;
        private readonly IRpcContextAccessor _rpcContextAccessor;
        private readonly IInvokeTemplate _invokeTemplate;
        private static object locker = new object();

        public TestAppService(
            IDistributedCache<TestOut> distributedCache,
            IRepository<Test> testRepository,
            ISerializer serializer,
            IRpcContextAccessor rpcContextAccessor, IInvokeTemplate invokeTemplate)
        {
            _distributedCache = distributedCache;
            _testRepository = testRepository;
            _serializer = serializer;
            _rpcContextAccessor = rpcContextAccessor;
            _invokeTemplate = invokeTemplate;
            _session = NullSession.Instance;
        }

        // [UnitOfWork]
        [UnitOfWork]
        public async Task<TestOut> CreateOrUpdateAsync(TestInput input)
        {
            if (input.Id.HasValue)
            {
                return await Update(input);
            }

            var test = input.Adapt<Test>();
            var result = await _testRepository.InsertNowAsync(test);
            return result.Entity.Adapt<TestOut>();
        }

        [UnitOfWork]
        public Task CreateOrUpdate2Async(TestInput input)
        {
            if (input.Id.HasValue)
            {
                return Update(input);
            }

            var test = input.Adapt<Test>();
            return _testRepository.InsertAsync(test);
        }

        public async Task<TestOut> Get(long id)
        {
            var test = await _testRepository.FindOrDefaultAsync(id);
            if (test == null)
            {
                throw new UserFriendlyException($"不存在Id为{id}的数据");
            }

            return test.Adapt<TestOut>();
        }

        [UnitOfWork]
        public async Task<TestOut> Update(TestInput input)
        {
            var entity = await _testRepository.FindOrDefaultAsync(input.Id);
            if (entity == null)
            {
                throw new UserFriendlyException($"不存在Id为{input.Id}的数据");
            }

            entity = input.Adapt(entity);
            var result = await _testRepository.UpdateAsync(entity);

            return result.Entity.Adapt<TestOut>();
        }

        [UnitOfWork]
        public async Task<string> Update2(long id)
        {
            var entity = await _testRepository.FindOrDefaultAsync(id);
            if (entity == null)
            {
                throw new UserFriendlyException($"不存在Id为{id}的数据");
            }

            if (entity.Address.IsNullOrEmpty())
            {
                entity.Address = "1";
            }
            else
            {
                var addressList = entity.Address.Split(",").ToList();
                var newValue = (addressList.Count + 1).ToString();
                addressList.Add(newValue);
                entity.Address = string.Join(",", addressList);
            }

            await _testRepository.UpdateNowAsync(entity);
            return entity.Address;
        }


        public async Task<string> DeleteAsync(long id)
        {
            // await _anotherAppService.DeleteOne(input.Name);
            // await _anotherAppService.DeleteTwo(input.Address);
            // // throw new BusinessException("test exception");
            // 
            var entity = await _testRepository.FindOrDefaultAsync(id);
            if (entity == null)
            {
                throw new UserFriendlyException($"不存在Id为{id}的数据");
            }

            await _testRepository.DeleteAsync(entity);
            await _distributedCache.RemoveAsync($"name:{entity.Name}");
            return "删除数据成功";
        }

        public async Task<IDictionary<string, string>> TestDict1()
        {
            return new Dictionary<string, string>()
            {
                { "Id", "1" },
                { "Name", "Name" }
            };
        }

        public async Task<PagedList<TestOut>> Search1(string name, string address, IList<long> ids, int pageIndex = 1,
            int pageSize = 10)
        {
            return await _testRepository.AsQueryable(false)
                .Where(!name.IsNullOrEmpty(), p => p.Name.Contains(name))
                .Where(!address.IsNullOrEmpty(), p => p.Address.Contains(name))
                .Where(!ids.IsNullOrEmpty(), p => ids.Contains(p.Id))
                .ProjectToType<TestOut>()
                .ToPagedListAsync(pageIndex, pageSize);
        }

        public async Task<PagedList<TestOut>> Search2(SearchInput query)
        {
            return await _testRepository.AsQueryable(false)
                .Where(!query.Name.IsNullOrEmpty(), p => p.Name.Contains(query.Name))
                .Where(!query.Address.IsNullOrEmpty(), p => p.Address.Contains(query.Address))
                .Where(!query.Ids.IsNullOrEmpty(), p => query.Ids.Contains(p.Id))
                .ProjectToType<TestOut>()
                .SortBy("Name-Asc")
                .ToPagedListAsync(query.PageIndex, query.PageSize);
        }

        public Task<PagedList<TestOut>> Search3(long[] Ids, Sort[] sorts)
        {
            throw new System.NotImplementedException();
        }

        public Task<TestOut> Form(TestInput input)
        {
            return CreateOrUpdateAsync(input);
        }

        public async Task<TestOut> Get1(string name)
        {
            var entity = await _testRepository.FirstOrDefaultAsync(p => p.Name == name);
            if (entity == null)
            {
                throw new UserFriendlyException($"不存在名称为{name}的记录");
            }

            return entity.Adapt<TestOut>();
        }

        public Task<TestOut> GetById(long? id)
        {
            if (!id.HasValue)
            {
                throw new UserFriendlyException("Id的值不允许为空");
            }

            return Get(id.Value);
        }

        public async Task<TestOut> UpdatePart(TestUpdatePart input)
        {
            var entity = await _testRepository.FindOrDefaultAsync(input.Id);
            if (entity == null)
            {
                throw new UserFriendlyException($"不存在Id为{input.Id}的数据");
            }

            entity = input.Adapt(entity);
            var result = await _testRepository.UpdateExcludeAsync(entity, new[] { "Name" });
            return result.Entity.Adapt<TestOut>();
        }

        public Task<TestOut> TestFromHeader(string id)
        {
            throw new System.NotImplementedException();
        }

        public async Task<TestOut> TestCache(TestInput input)
        {
            var test = await _testRepository.FirstOrDefaultAsync(p => p.Name == input.Name);
            return test.Adapt<TestOut>();
        }


        public async Task<IList<object>> GetObjectList()
        {
            var objects = new List<object>();
            dynamic obj = new
            {
                Value = "08车10F号",
                Position = new List<int>()
                {
                    730,
                    246,
                    967,
                    249,
                    966,
                    302,
                    729,
                    299
                },
                Key = "seat_number",
                Description = "座位号"
            };
            for (int i = 0; i < 1000; i++)
            {
                objects.Add(obj);
            }

            return objects;
        }

        public async Task<string> InvokeQuery1()
        {
            var result = await _invokeTemplate.GetForObjectAsync<string>("/api/another/query1/{id:long}", 1);
            return result;
        }

        public async Task<object> GetObject()
        {
            var obj = new
            {
                Value = "08车10F号",
                Position = new List<int>()
                {
                    730,
                    246,
                    967,
                    249,
                    966,
                    302,
                    729,
                    299
                },
                Key = "seat_number",
                Description = "座位号"
            };
            return obj;
        }

        public async Task<OcrOutput> GetOcr()
        {
            var obj = new
            {
                Value = "08车10F号",
                Position = new List<int>()
                {
                    730,
                    246,
                    967,
                    249,
                    966,
                    302,
                    729,
                    299
                },
                Key = "seat_number",
                Description = "座位号"
            };
            var ocrOutput = new OcrOutput()
            {
                Result = obj
            };
            return ocrOutput;
        }

        public async Task<string> TestNamedService(string serviceName)
        {
            var service = EngineContext.Current.ResolveNamed<ITestDomainService>(serviceName);
            if (service == null)
            {
                throw new BusinessException($"不存在{serviceName}的服务");
            }

            return await service.Test();
        }

        public async Task<string> TestIgnoreBodyData(TestInput input)
        {
            var requestParameters = RpcContext.Context.GetRequestParameters();
            return input.Name;
        }

        public async Task<bool> TestForLongTime()
        {
            try
            {
                await Task.Run(() =>
                {
                    var x = "";
                    for (int i = 0; i < 999999999999999999; i++)
                    {
                        x += i;
                    }
                });
                
               
                //  await Task.Delay(20 * 1000);

            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
            }
            return true;
        }
    }
}