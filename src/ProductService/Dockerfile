FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/ProductService/ProductService.csproj", "src/ProductService/"]
RUN dotnet restore "src/ProductService/ProductService.csproj"
COPY . .
WORKDIR "/src/src/ProductService"
RUN dotnet publish "ProductService.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5002
EXPOSE 5002
ENTRYPOINT ["dotnet", "ProductService.dll"]
