using System.Net;
using System.Threading.Tasks;
using Consul;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Silky.Core.Exceptions;
using Silky.Core.Extensions;
using Silky.Core.Serialization;
using Silky.RegistryCenter.Consul;
using Silky.RegistryCenter.Consul.Configuration;
using Silky.Swagger.Abstraction;
using Silky.Swagger.Gen.Extensions;

namespace Silky.Swagger.Gen.Register.Consul;

public class ConsulSwaggerInfoRegister : SwaggerInfoRegisterBase
{
    private readonly IConsulClientFactory _consulClientFactory;
    private readonly ConsulRegistryCenterOptions _consulRegistryCenterOptions;

    public ILogger<ConsulSwaggerInfoRegister> Logger { get; set; }

    public ConsulSwaggerInfoRegister(ISwaggerProvider swaggerProvider,
        IConsulClientFactory consulClientFactory,
        IOptions<ConsulRegistryCenterOptions> consulRegistryCenterOptions)
        : base(swaggerProvider)
    {
        _consulClientFactory = consulClientFactory;
        _consulRegistryCenterOptions = consulRegistryCenterOptions.Value;
        Logger = NullLogger<ConsulSwaggerInfoRegister>.Instance;
    }

    protected override async Task Register(string documentName, OpenApiDocument openApiDocument)
    {
        using var consulClient = _consulClientFactory.CreateClient();
        var openApiDocumentJsonString = openApiDocument.ToJson();
        var servicesPutResult = await consulClient.KV.Put(
            new KVPair(CreateServicePath(_consulRegistryCenterOptions.SwaggerDocPath, documentName))
            {
                Value = openApiDocumentJsonString.GetBytes()
            });
        if (servicesPutResult.StatusCode != HttpStatusCode.OK)
        {
            throw new SilkyException($"Register {documentName} SwaggerDoc Info To ServiceCenter Consul Error");
        }

        Logger.LogDebug($"Register {documentName} SwaggerDoc Info To ServiceCenter Consul Success");
    }

    private string CreateServicePath(string basePath, string child)
    {
        var servicePath = basePath;
        if (!servicePath.EndsWith("/"))
        {
            servicePath += "/";
        }

        servicePath += child;
        return servicePath;
    }
}