FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/MiniSmtpServer/MiniSmtpServer.csproj src/MiniSmtpServer/

RUN dotnet restore src/MiniSmtpServer/MiniSmtpServer.csproj

COPY . .

RUN dotnet publish src/MiniSmtpServer/MiniSmtpServer.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MiniSmtpServer.dll"]
