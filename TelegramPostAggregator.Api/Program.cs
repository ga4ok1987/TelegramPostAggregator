using Hangfire;
using Hangfire.Dashboard;
using TelegramPostAggregator.Application;
using TelegramPostAggregator.Api.Models;
using TelegramPostAggregator.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var hangfireDashboardSettings = builder.Configuration
    .GetSection(HangfireDashboardSettings.SectionName)
    .Get<HangfireDashboardSettings>() ?? new HangfireDashboardSettings();

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
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsDevelopment() || hangfireDashboardSettings.Enabled)
{
    var dashboardOptions = new DashboardOptions();

    if (!app.Environment.IsDevelopment())
    {
        dashboardOptions.Authorization =
        [
            new HangfireDashboardAuthorizationFilter(
                hangfireDashboardSettings.AllowLocalRequests,
                hangfireDashboardSettings.Username,
                hangfireDashboardSettings.Password)
        ];
    }

    app.MapHangfireDashboard("/jobs", dashboardOptions);
}

app.Run();
