FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["TelegramPostAggregator.Monitoring.Web/TelegramPostAggregator.Monitoring.Web.csproj", "TelegramPostAggregator.Monitoring.Web/"]
COPY ["TelegramPostAggregator.Application/TelegramPostAggregator.Application.csproj", "TelegramPostAggregator.Application/"]
COPY ["TelegramPostAggregator.Domain/TelegramPostAggregator.Domain.csproj", "TelegramPostAggregator.Domain/"]
COPY ["TelegramPostAggregator.Infrastructure/TelegramPostAggregator.Infrastructure.csproj", "TelegramPostAggregator.Infrastructure/"]
RUN dotnet restore "TelegramPostAggregator.Monitoring.Web/TelegramPostAggregator.Monitoring.Web.csproj"

COPY . .
WORKDIR "/src/TelegramPostAggregator.Monitoring.Web"
RUN dotnet publish "TelegramPostAggregator.Monitoring.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 5180

ENV ASPNETCORE_URLS=http://+:5180
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "TelegramPostAggregator.Monitoring.Web.dll"]
