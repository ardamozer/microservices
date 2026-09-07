FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/UserService/UserService.csproj", "src/UserService/"]
RUN dotnet restore "src/UserService/UserService.csproj"
COPY . .
WORKDIR "/src/src/UserService"
RUN dotnet publish "UserService.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5001
EXPOSE 5001
ENTRYPOINT ["dotnet", "UserService.dll"]
