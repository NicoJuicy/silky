﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Lms.Core.Exceptions;
using Lms.Core.Extensions;
using Lms.Rpc.Runtime.Server.ServiceEntry;
using Lms.Rpc.Runtime.Server.ServiceEntry.Parameter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.OpenApi.Models;

namespace Lms.Swagger.SwaggerGen.SwaggerGenerator
{
    public class SwaggerGenerator : ISwaggerProvider
    {
        private readonly IServiceEntryManager _serviceEntryManager;
        private readonly ISchemaGenerator _schemaGenerator;
        private readonly SwaggerGeneratorOptions _options;

        public SwaggerGenerator(
            SwaggerGeneratorOptions options,
            ISchemaGenerator schemaGenerator,
            IServiceEntryManager serviceEntryManager)
        {
            _options = options ?? new SwaggerGeneratorOptions();
            _schemaGenerator = schemaGenerator;
            _serviceEntryManager = serviceEntryManager;
        }


        public OpenApiDocument GetSwagger(string documentName, string host = null, string basePath = null)
        {
            if (!_options.SwaggerDocs.TryGetValue(documentName, out OpenApiInfo info))
                throw new UnknownSwaggerDocument(documentName, _options.SwaggerDocs.Select(d => d.Key));

            var schemaRepository = new SchemaRepository(documentName);
            var entries = _serviceEntryManager.GetAllEntries();
            var swaggerDoc = new OpenApiDocument
            {
                Info = info,
                Servers = GenerateServers(host, basePath),
                Paths = GeneratePaths(entries, schemaRepository),
                Components = new OpenApiComponents
                {
                    Schemas = schemaRepository.Schemas,
                    SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>(_options.SecuritySchemes)
                },
                SecurityRequirements = new List<OpenApiSecurityRequirement>(_options.SecurityRequirements)
            };
            return swaggerDoc;
        }

        private OpenApiPaths GeneratePaths(IReadOnlyList<ServiceEntry> entries, SchemaRepository schemaRepository)
        {
            var entriesByPath = entries.OrderBy(_options.SortKeySelector)
                .GroupBy(p => p.Router.RoutePath);

            var paths = new OpenApiPaths();
            foreach (var group in entriesByPath)
            {
                paths.Add($"/{group.Key}",
                    new OpenApiPathItem
                    {
                        Operations = GenerateOperations(group, schemaRepository)
                    });
            }

            return paths;
        }

        private IDictionary<OperationType, OpenApiOperation> GenerateOperations(
            IGrouping<string, ServiceEntry> apiDescriptions, SchemaRepository schemaRepository)
        {
            var apiDescriptionsByMethod = apiDescriptions
                .OrderBy(_options.SortKeySelector)
                .GroupBy(apiDesc => apiDesc.Router.HttpMethod);
            var operations = new Dictionary<OperationType, OpenApiOperation>();
            foreach (var group in apiDescriptionsByMethod)
            {
                var httpMethod = group.Key;

                if (httpMethod == null)
                    throw new SwaggerGeneratorException(string.Format(
                        "Ambiguous HTTP method for action - {0}. " +
                        "Actions require an explicit HttpMethod binding for Swagger/OpenAPI 3.0",
                        group.First().ServiceDescriptor.Id));

                if (group.Count() > 1 && _options.ConflictingActionsResolver == null)
                    throw new SwaggerGeneratorException(string.Format(
                        "Conflicting method/path combination \"{0} {1}\" for actions - {2}. " +
                        "Actions require a unique method/path combination for Swagger/OpenAPI 3.0. Use ConflictingActionsResolver as a workaround",
                        httpMethod,
                        group.First().Router.RoutePath,
                        string.Join(",", group.Select(apiDesc => apiDesc.ServiceDescriptor.Id))));

                var apiDescription = (group.Count() > 1) ? _options.ConflictingActionsResolver(group) : group.Single();

                operations.Add(OperationTypeMap[httpMethod.ToString().ToUpper()],
                    GenerateOperation(apiDescription, schemaRepository));
            }

            ;

            return operations;
        }

        private OpenApiOperation GenerateOperation(ServiceEntry apiDescription, SchemaRepository schemaRepository)
        {
            try
            {
                var operation = new OpenApiOperation
                {
                    Tags = GenerateOperationTags(apiDescription),
                    OperationId = _options.OperationIdSelector(apiDescription),
                    Parameters = GenerateParameters(apiDescription, schemaRepository),
                    RequestBody = GenerateRequestBody(apiDescription, schemaRepository),
                    Responses = GenerateResponses(apiDescription, schemaRepository),
                    //Deprecated = apiDescription.CustomAttributes().OfType<ObsoleteAttribute>().Any()
                };

                // apiDescription.TryGetMethodInfo(out MethodInfo methodInfo);
                var methodInfo = apiDescription.MethodInfo;
                var filterContext =
                    new OperationFilterContext(apiDescription, _schemaGenerator, schemaRepository, methodInfo);
                foreach (var filter in _options.OperationFilters)
                {
                    filter.Apply(operation, filterContext);
                }

                return operation;
            }
            catch (Exception ex)
            {
                throw new SwaggerGeneratorException(
                    message:
                    $"Failed to generate Operation for action - {apiDescription.ServiceDescriptor.Id}. See inner exception",
                    innerException: ex);
            }
        }

        private OpenApiResponses GenerateResponses(ServiceEntry apiDescription, SchemaRepository schemaRepository)
        {
            var responses = new OpenApiResponses();
            var statusCodeSourcess = StatusCodeHelper.GetResponseStatusCodes();
            foreach (var statusCodeSource in statusCodeSourcess)
            {
                responses.Add(((int)statusCodeSource.Key).ToString(), GenerateResponse(apiDescription, schemaRepository, statusCodeSource));
            }
            return responses;
        }

        private OpenApiResponse GenerateResponse(ServiceEntry apiDescription, SchemaRepository schemaRepository, KeyValuePair<StatusCode, string> statusCodeSource)
        {
            var description = statusCodeSource.Value;
            var responseContentTypes = apiDescription.SupportedResponseMediaTypes;
            if (statusCodeSource.Key == StatusCode.Success)
            {
                return new OpenApiResponse
                {
                    Description = description,
                    Content = responseContentTypes.ToDictionary(
                        contentType => contentType,
                        contentType => CreateResponseMediaType(apiDescription.ReturnType, schemaRepository)
                    )
                };
            }
            else
            {
                return new OpenApiResponse
                {
                    Description = description,
                    Content = responseContentTypes.ToDictionary(
                        contentType => contentType,
                        contentType => CreateResponseMediaType(typeof(string), schemaRepository)
                    )
                };
            }
        }


        private OpenApiMediaType CreateResponseMediaType(Type returnType, SchemaRepository schemaRepository)
        {
            return new OpenApiMediaType
            {
                Schema = GenerateSchema(returnType, schemaRepository)
            };
        }

        private IEnumerable<string> InferResponseContentTypes(ServiceEntry apiDescription, ApiResponseType apiResponseType)
        {
            return apiDescription.SupportedResponseMediaTypes;
        }

        private OpenApiRequestBody GenerateRequestBody(ServiceEntry apiDescription, SchemaRepository schemaRepository)
        {
            OpenApiRequestBody requestBody = null;
            RequestBodyFilterContext filterContext = null;
            var bodyParameter = apiDescription.ParameterDescriptors
                .FirstOrDefault(paramDesc => paramDesc.From == ParameterFrom.Body);

            var formParameters = apiDescription.ParameterDescriptors
                .Where(paramDesc => paramDesc.From == ParameterFrom.Form);
            if (bodyParameter != null)
            {
                requestBody = GenerateRequestBodyFromBodyParameter(apiDescription, schemaRepository, bodyParameter);

                filterContext = new RequestBodyFilterContext(
                    bodyParameterDescription: bodyParameter,
                    formParameterDescriptions: null,
                    schemaGenerator: _schemaGenerator,
                    schemaRepository: schemaRepository);
            }
            else if (formParameters.Any())
            {
                requestBody = GenerateRequestBodyFromFormParameters(apiDescription, schemaRepository, formParameters);

                filterContext = new RequestBodyFilterContext(
                    bodyParameterDescription: null,
                    formParameterDescriptions: formParameters,
                    schemaGenerator: _schemaGenerator,
                    schemaRepository: schemaRepository);
            }
            if (requestBody != null)
            {
                foreach (var filter in _options.RequestBodyFilters)
                {
                    filter.Apply(requestBody, filterContext);
                }
            }
            return requestBody;
        }

        private OpenApiRequestBody GenerateRequestBodyFromFormParameters(ServiceEntry apiDescription, SchemaRepository schemaRepository, IEnumerable<ParameterDescriptor> formParameters)
        {
           var contentTypes = InferRequestContentTypes(apiDescription);
                       contentTypes = contentTypes.Any() ? contentTypes : new[] { "multipart/form-data" };
           var schema = GenerateSchemaFromFormParameters(formParameters, schemaRepository);
           return new OpenApiRequestBody
           {
               Content = contentTypes
                   .ToDictionary(
                       contentType => contentType,
                       contentType => new OpenApiMediaType
                       {
                           Schema = schema,
                           Encoding = schema.Properties.ToDictionary(
                               entry => entry.Key,
                               entry => new OpenApiEncoding { Style = ParameterStyle.Form }
                           )
                       }
                   )
           };
        }

        private OpenApiSchema GenerateSchemaFromFormParameters(IEnumerable<ParameterDescriptor> formParameters, SchemaRepository schemaRepository)
        {
            var properties = new Dictionary<string, OpenApiSchema>();
            var requiredPropertyNames = new List<string>();
            foreach (var formParameter in formParameters)
            {
                if (formParameter.IsSample)
                {
                    var name = _options.DescribeAllParametersInCamelCase
                        ? formParameter.Name.ToCamelCase()
                        : formParameter.Name;
                    var schema = GenerateSchema(
                        formParameter.Type,
                        schemaRepository,
                        null,
                        formParameter.ParameterInfo);
                    properties.Add(name, schema);
                    if (formParameter.Type.GetCustomAttributes().Any(attr => RequiredAttributeTypes.Contains(attr.GetType())))
                    {
                        requiredPropertyNames.Add(name);
                    }
                }
                else
                {
                    foreach (var propertyInfo in formParameter.Type.GetProperties())
                    {
                        var name = _options.DescribeAllParametersInCamelCase
                            ? propertyInfo.Name.ToCamelCase()
                            : propertyInfo.Name;
                        var schema = GenerateSchema(
                            formParameter.Type,
                            schemaRepository,
                            propertyInfo,
                            null);
                        properties.Add(name, schema);
                        if (propertyInfo.GetCustomAttributes().Any(attr => RequiredAttributeTypes.Contains(attr.GetType())))
                        {
                            requiredPropertyNames.Add(name);
                        }
                    }
                }
            }
            return new OpenApiSchema
            {
                Type = "object",
                Properties = properties,
                Required = new SortedSet<string>(requiredPropertyNames)
            };
        }

        private OpenApiRequestBody GenerateRequestBodyFromBodyParameter(ServiceEntry apiDescription, SchemaRepository schemaRepository, ParameterDescriptor bodyParameter)
        {
            var contentTypes = InferRequestContentTypes(apiDescription);
            var isRequired = apiDescription.CustomAttributes.Any(attr => RequiredAttributeTypes.Contains(attr.GetType()));
            var schema = GenerateSchema(
                bodyParameter.Type,
                schemaRepository,
                null,
                bodyParameter.ParameterInfo);
            return new OpenApiRequestBody
            {
                Content = contentTypes
                    .ToDictionary(
                        contentType => contentType,
                        contentType => new OpenApiMediaType
                        {
                            Schema = schema
                        }
                    ),
                Required = isRequired
            };
        }

        private IEnumerable<string> InferRequestContentTypes(ServiceEntry apiDescription)
        {
            var explicitContentTypes = apiDescription.CustomAttributes.OfType<ConsumesAttribute>()
                .SelectMany(attr => attr.ContentTypes)
                .Distinct();
            if (explicitContentTypes.Any()) return explicitContentTypes;
            var apiExplorerContentTypes = apiDescription.SupportedRequestMediaTypes
                .Distinct();
            if (apiExplorerContentTypes.Any()) return apiExplorerContentTypes;

            return Enumerable.Empty<string>();
        }

        private IList<OpenApiParameter> GenerateParameters(ServiceEntry apiDescription,
            SchemaRepository schemaRespository)
        {
            var applicableApiParameters = apiDescription.ParameterDescriptors
                .Where(apiParam =>
                    apiParam.From == ParameterFrom.Path ||
                    apiParam.From == ParameterFrom.Query ||
                    apiParam.From == ParameterFrom.Header
                );
            return applicableApiParameters
                .SelectMany(apiParam => GenerateParameter(apiParam, schemaRespository))
                .ToList();
        }

        private IEnumerable<OpenApiParameter> GenerateParameter(ParameterDescriptor apiParameter,
            SchemaRepository schemaRespository)
        {
            var parameters = new List<OpenApiParameter>();
            if (apiParameter.IsSample)
            {
                parameters.Add(GenerateSampleParameter(apiParameter, schemaRespository));
            }
            else
            {
                parameters.AddRange(GenerateComplexParameter(apiParameter, schemaRespository));
            }

            return parameters;
        }


        private OpenApiParameter GenerateSampleParameter(ParameterDescriptor apiParameter,
            SchemaRepository schemaRespository)
        {
            var name = _options.DescribeAllParametersInCamelCase
                ? apiParameter.Name.ToCamelCase()
                : apiParameter.Name;

            var location = ParameterLocationMap[apiParameter.From];
            var isRequired = (apiParameter.From == ParameterFrom.Path)
                             || apiParameter.Type.GetCustomAttributes()
                                 .Any(attr => RequiredAttributeTypes.Contains(attr.GetType()));

            var schema = GenerateSchema(apiParameter.Type, schemaRespository, null,
                apiParameter.ParameterInfo);
            var parameter = new OpenApiParameter
            {
                Name = name,
                In = location,
                Required = isRequired,
                Schema = schema
            };

            var filterContext = new ParameterFilterContext(
                apiParameter,
                _schemaGenerator,
                schemaRespository,
                null,
                apiParameter.ParameterInfo);

            foreach (var filter in _options.ParameterFilters)
            {
                filter.Apply(parameter, filterContext);
            }

            return parameter;
        }

        private IEnumerable<OpenApiParameter> GenerateComplexParameter(ParameterDescriptor apiParameter,
            SchemaRepository schemaRespository)
        {
            var parameters = new List<OpenApiParameter>();
            var propertyInfos = apiParameter.Type.GetProperties();
            foreach (var propertyInfo in propertyInfos)
            {
                if (!propertyInfo.PropertyType.IsSample())
                {
                    throw new LmsException("指定QString 参数不允许指定复杂类型参数");
                }

                var name = _options.DescribeAllParametersInCamelCase
                    ? propertyInfo.Name.ToCamelCase()
                    : propertyInfo.Name;
                var location = ParameterLocationMap[apiParameter.From];
                var isRequired = (apiParameter.From == ParameterFrom.Path)
                                 || apiParameter.Type.GetCustomAttributes().Any(attr =>
                                     RequiredAttributeTypes.Contains(attr.GetType()));

                var schema = GenerateSchema(apiParameter.Type, schemaRespository, propertyInfo, null);
                var parameter = new OpenApiParameter
                {
                    Name = name,
                    In = location,
                    Required = isRequired,
                    Schema = schema
                };

                var filterContext = new ParameterFilterContext(
                    apiParameter,
                    _schemaGenerator,
                    schemaRespository,
                    null,
                    apiParameter.ParameterInfo);

                foreach (var filter in _options.ParameterFilters)
                {
                    filter.Apply(parameter, filterContext);
                }

                parameters.Add(parameter);
            }

            return parameters;
        }


        private OpenApiSchema GenerateSchema(
            Type type,
            SchemaRepository schemaRepository,
            PropertyInfo propertyInfo = null,
            ParameterInfo parameterInfo = null)
        {
            try
            {
                return _schemaGenerator.GenerateSchema(type, schemaRepository, propertyInfo, parameterInfo);
            }
            catch (Exception ex)
            {
                throw new SwaggerGeneratorException(
                    message: $"Failed to generate schema for type - {type}. See inner exception",
                    innerException: ex);
            }
        }

        private IList<OpenApiTag> GenerateOperationTags(ServiceEntry apiDescription)
        {
            return _options.TagsSelector(apiDescription)
                .Select(tagName => new OpenApiTag {Name = tagName})
                .ToList();
        }


        private IList<OpenApiServer> GenerateServers(string host, string basePath)
        {
            if (_options.Servers.Any())
            {
                return new List<OpenApiServer>(_options.Servers);
            }

            return (host == null && basePath == null)
                ? new List<OpenApiServer>()
                : new List<OpenApiServer> {new OpenApiServer {Url = $"{host}{basePath}"}};
        }

        private static readonly Dictionary<string, OperationType> OperationTypeMap =
            new Dictionary<string, OperationType>
            {
                {"GET", OperationType.Get},
                {"PUT", OperationType.Put},
                {"POST", OperationType.Post},
                {"DELETE", OperationType.Delete},
                {"OPTIONS", OperationType.Options},
                {"HEAD", OperationType.Head},
                {"PATCH", OperationType.Patch},
                {"TRACE", OperationType.Trace}
            };

        private static readonly Dictionary<ParameterFrom, ParameterLocation> ParameterLocationMap =
            new Dictionary<ParameterFrom, ParameterLocation>
            {
                {ParameterFrom.Query, ParameterLocation.Query},
                {ParameterFrom.Header, ParameterLocation.Header},
                {ParameterFrom.Path, ParameterLocation.Path}
            };

        private static readonly IEnumerable<Type> RequiredAttributeTypes = new[]
        {
            typeof(BindRequiredAttribute),
            typeof(RequiredAttribute)
        };
    }
}