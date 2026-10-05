# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Restoring from the csproj files alone puts the packages in a layer a source change keeps.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY Debarr/Debarr.csproj Debarr/
RUN dotnet restore Debarr/Debarr.csproj

COPY . .
# Writes Wolverine's handlers into Debarr/Internal/Generated, which a Release build loads instead of compiling them at startup.
RUN DEBARR__APP__DATADIR=/src/codegen-data dotnet run --project Debarr/Debarr.csproj -- codegen write
# Publish restores again so the static web asset manifest covers wwwroot and the Razor components.
RUN dotnet publish Debarr/Debarr.csproj -c Release -o /app

# The data directory, copied into the final image with its owner and mode set by COPY.
RUN mkdir -p /rootfs/data

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
COPY --from=mwader/static-ffmpeg:8.1.2@sha256:33f770f812cbfc3de96c547157fc9faf8bd95a36481753439ffa761045167585 /ffmpeg /ffprobe /usr/local/bin/

# /data belongs to group 0 with write permission, so a run under any uid with GID 0 can write it.
COPY --from=build --chown=65532:0 --chmod=775 /rootfs/ /
ENV DEBARR__APP__DATADIR=/data
# Scratch files go under the data directory, so the image runs with --read-only.
ENV TMPDIR=/data/tmp

WORKDIR /app
COPY --from=build /app .
USER 65532:65532
EXPOSE 8080
ENTRYPOINT ["dotnet", "Debarr.dll"]
