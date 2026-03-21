FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /api

COPY src/api/*.csproj ./src/

COPY src/api ./src/
RUN dotnet restore ./src/API.csproj
RUN dotnet publish ./src/API.csproj -c Release -o /out/publish --no-restore -f net8.0 -r linux-x64

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /api

RUN apt-get update && apt-get install -y --no-install-recommends \
    libicu-dev \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /out/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

ENTRYPOINT ["./GotoRef.API"]
