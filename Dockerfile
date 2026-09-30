FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY hfparquet.csproj ./
RUN dotnet restore hfparquet.csproj
COPY . ./
RUN dotnet publish hfparquet.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "hfparquet.dll"]
