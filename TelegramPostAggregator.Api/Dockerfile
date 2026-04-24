FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["TelegramPostAggregator.Api/TelegramPostAggregator.Api.csproj", "TelegramPostAggregator.Api/"]
COPY ["TelegramPostAggregator.Application/TelegramPostAggregator.Application.csproj", "TelegramPostAggregator.Application/"]
COPY ["TelegramPostAggregator.Domain/TelegramPostAggregator.Domain.csproj", "TelegramPostAggregator.Domain/"]
COPY ["TelegramPostAggregator.Infrastructure/TelegramPostAggregator.Infrastructure.csproj", "TelegramPostAggregator.Infrastructure/"]
RUN dotnet restore "TelegramPostAggregator.Api/TelegramPostAggregator.Api.csproj"

COPY . .
WORKDIR "/src/TelegramPostAggregator.Api"
RUN dotnet publish "TelegramPostAggregator.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .

RUN mkdir -p /var/lib/telegram-post-aggregator/tdlib-data /var/lib/telegram-post-aggregator/tdlib-files

ENTRYPOINT ["dotnet", "TelegramPostAggregator.Api.dll"]
