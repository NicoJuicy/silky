FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app
ARG rpc_port=2200
ARG ws_port=3000
ENV TZ=Asia/Shanghai 
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone 
EXPOSE ${rpc_port} ${ws_port}

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /
COPY common.props .
COPY src /src
RUN dotnet restore /src/Silky.SilkyAppHost/Silky.SilkyAppHost.csproj --disable-parallel && \
    dotnet build --no-restore -c Release /src/Silky.SilkyAppHost/Silky.SilkyAppHost.csproj

FROM build AS publish
WORKDIR /src/Silky.SilkyAppHost
RUN dotnet publish --no-restore -c Release -o /app

FROM base AS final
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "Silky.SilkyAppHost.dll"]