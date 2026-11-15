FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY OblakoHub.Server.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish --no-restore -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
RUN mkdir /data && chown app:app /data
ENV OBLAKO_DATA=/data
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "OblakoHub.Server.dll"]
