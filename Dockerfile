# Stage 1: Build server
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS server-build
WORKDIR /app

COPY global.json ./
COPY src/Shared/Shared.fsproj src/Shared/
COPY src/Server/Server.fsproj src/Server/
COPY src/Client/Client.fsproj src/Client/

RUN dotnet restore src/Server/Server.fsproj

COPY src/Shared/ src/Shared/
COPY src/Server/ src/Server/

RUN dotnet publish src/Server/Server.fsproj -c Release -o /app/publish

# Stage 2: Build client
FROM server-build AS client-build
WORKDIR /app

COPY src/Client/ src/Client/
COPY package.json ./
COPY vite.config.mts ./

RUN curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get install -y nodejs \
    && npm install \
    && npx vite build

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=server-build /app/publish ./
COPY --from=client-build /app/deploy/public ./deploy/public

# Data volume for SQLite database, images, and other persistent state
VOLUME /app/data

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "Server.dll"]
