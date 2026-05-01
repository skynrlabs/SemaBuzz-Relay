FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish SemaBuzz.Relay.csproj -c Release -r linux-x64 --self-contained false -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
# Railway sets PORT at runtime; the relay reads it via Environment.GetEnvironmentVariable("PORT")
# and passes it to builder.WebHost.UseUrls — so no ASPNETCORE_URLS override needed.
ENTRYPOINT ["dotnet", "SemaBuzz.Relay.dll"]
