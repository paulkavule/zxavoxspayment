# syntax=docker/dockerfile:1
#
# Payment service API. Build from THIS repo's root: docker compose up --build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .

# GitHub Packages refuses anonymous restore; nuget.config reads %GH_PACKAGES_TOKEN%.
RUN --mount=type=secret,id=ghtoken \
    GH_PACKAGES_TOKEN="$(cat /run/secrets/ghtoken)" \
    dotnet restore zxavoxpaymentapi/zxavoxpaymentapi.csproj

RUN dotnet publish zxavoxpaymentapi/zxavoxpaymentapi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "zxavoxpaymentapi.dll"]
