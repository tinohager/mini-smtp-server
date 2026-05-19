FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore layer (cached)
COPY src/MiniSmtpServer/MiniSmtpServer.csproj src/MiniSmtpServer/
RUN dotnet restore src/MiniSmtpServer/MiniSmtpServer.csproj

# full source
COPY . .

# native build dependencies
RUN apt-get update && apt-get install -y \
    clang \
    zlib1g-dev \
    libkrb5-dev \
    && rm -rf /var/lib/apt/lists/*

# publish NativeAOT
RUN dotnet publish src/MiniSmtpServer/MiniSmtpServer.csproj \
    -c Release \
    -r linux-x64 \
    -p:PublishAot=true \
    -p:OptimizationPreference=Speed \
    -p:StripSymbols=true \
    -o /app/publish

# runtime image (minimal)
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0

WORKDIR /app
COPY --from=build /app/publish .

# IMPORTANT: native executable name = project name
ENTRYPOINT ["./MiniSmtpServer"]