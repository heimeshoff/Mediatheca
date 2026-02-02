# Stage 1: Build server
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS server-build
WORKDIR /app

COPY global.json ./
COPY Mediatheca.sln ./
COPY src/Shared/Shared.fsproj src/Shared/
COPY src/Server/Server.fsproj src/Server/
COPY src/Client/Client.fsproj src/Client/
COPY tests/Server.Tests/Server.Tests.fsproj tests/Server.Tests/

RUN dotnet restore

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

COPY --from=server-build /app/publish ./
COPY --from=client-build /app/deploy/public ./deploy/public

# Image storage volume for posters, backdrops, cast photos, and friend images
VOLUME /app/images

# Set TMDB_API_KEY environment variable to enable TMDB integration
# Example: docker run -e TMDB_API_KEY=your_key_here ...
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "Server.dll"]
