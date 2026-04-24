using Hangfire;
using TelegramPostAggregator.Application;
using TelegramPostAggregator.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await app.Services.InitializeInfrastructureAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler("/api/health/error");
if (!string.IsNullOrWhiteSpace(builder.Configuration["HTTPS_PORTS"]) ||
    !string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_HTTPS_PORTS"]) ||
    !string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_HTTPS_PORT"]))
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();
app.MapHangfireDashboard("/jobs");

app.Run();
