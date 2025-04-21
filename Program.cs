using TradingApp.src.Core.Services.Implementations; // For TradesService
using TradingBot.Data.Data.Repositories.Trade.Implementations; // For TradeRepository
using TradingBot.Data.Data.Repositories.Trade.Interfaces;
using TradingApp.src.Data.Repositories.HistoricalPrices.Interfaces; // For IHistoricalPriceRepository
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;
using TradingBot.Services.Services.Interfaces;
using TradingBot.Services.Factories.Interfaces;
using TradingBot.Services.Factories.Implementations;
using TradingBot.Services.BackTesting.Interfaces;
using TradingBot.Services.BackTesting.Implementations;
using TradingApp.src.Core.Services.Interfaces;
using TradingBot.Services.Services.Implementations;
using TradingApp.src.Data.Repositories.HistoricalPrices.Implementations;
using TradingBot.Settings.Settings;
using TradingBot.Data.Data.Repositories.Securities.Implementations;
using TradingBot.Data.Data.Repositories.Securities.Interfaces;
using TradingBot.Utilities.Helpers;
using TradingBot.Data.Data.Repositories.BalanceSheet.Implementation;
using TradingBot.Data.Data.Repositories.BalanceSheet.Interface;
using TradingBot.Data.Repositories.Securities.Implementations;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var historicalConnectionString = builder.Configuration.GetConnectionString("HistoricalPricesConnection");

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.AddConfiguration(builder.Configuration.GetSection("Logging"));
    loggingBuilder.AddConsole();
    loggingBuilder.AddDebug();
});

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// External Data Services (Replace Placeholders with Real Implementations)
builder.Services.AddScoped<IHistoricalPriceService, HistoricalPriceService>();
builder.Services.AddScoped<ISecuritiesOverviewService, SecuritiesOverviewService>();
builder.Services.AddScoped<IHistoricalPriceRepository, HistoricalPriceRepository>(); // Needed by TradeRepository
builder.Services.AddHttpClient<IAlphaVantageService, AlphaVantageService>();

builder.Services.AddSingleton(new DefaultConnectionString(connectionString));
builder.Services.AddSingleton(new HistoricalPriceConnectionString(historicalConnectionString));
// Backtesting Core Services
builder.Services.AddSingleton<IStrategyFactory, StrategyFactory>(); // Factory can be singleton
builder.Services.AddScoped<IPerformanceCalculator, PerformanceCalculator>();
builder.Services.AddScoped<IBacktestRunner, BacktestRunner>(); // Scoped is fine for background jobs too
builder.Services.AddSingleton<ISecuritiesOverviewRepository, SecuritiesOverviewRepository>();
builder.Services.AddScoped<IHistoricalPriceService, HistoricalPriceService>();
builder.Services.AddScoped<ISecuritiesOverviewService, SecuritiesOverviewService>();
builder.Services.AddScoped<IAlphaVantageService, AlphaVantageService>();
builder.Services.AddScoped<IEarningsService, EarningsService>();
builder.Services.AddScoped<IBalanceSheetService, BalanceSheetService>();
builder.Services.AddScoped<IPriceService, PriceService>();
builder.Services.AddScoped<IHistoricalPriceService, HistoricalPriceService>();
builder.Services.AddScoped<IEarningsRepository, EarningsRepository>();
builder.Services.AddScoped<IBalanceSheetRepository, BalanceSheetRepository>();

builder.Services.AddScoped<ITradeRepository>(provider =>
    new TradeRepository(
        historicalConnectionString,
        provider.GetRequiredService<ILogger<TradeRepository>>()
    ));

builder.Services.AddScoped<ITradeService, TradesService>();

builder.Services.AddSingleton(sp => "YourConnectionStringHere");

builder.Services.AddSingleton<ISecuritiesOverviewRepository>(provider =>
{
    var cs = provider.GetRequiredService<DefaultConnectionString>().Value;
    return new SecuritiesOverviewRepository(cs);
});

builder.Services.AddSingleton<IHistoricalPriceRepository>(provider =>
{
    var historicalConnectionString = provider.GetRequiredService<HistoricalPriceConnectionString>().Value;
    var sqlHelper = provider.GetRequiredService<ISqlHelper>();
    var securitiesOverviewRepository = provider.GetRequiredService<ISecuritiesOverviewRepository>();
    return new HistoricalPriceRepository(historicalConnectionString, securitiesOverviewRepository, sqlHelper);
});

builder.Services.AddSingleton<IBacktestRepository>(provider =>
{
    var cs = provider.GetRequiredService<DefaultConnectionString>().Value;
    var logger = provider.GetRequiredService<ILogger<BacktestRepository>>();
    return new BacktestRepository(cs, logger);
});

builder.Services.AddSingleton<ITradeRepository>(provider =>
{
    var cs = provider.GetRequiredService<DefaultConnectionString>().Value;
    var logger = provider.GetRequiredService<ILogger<TradeRepository>>();
    return new TradeRepository(cs, logger);
});

builder.Services.AddSingleton<ISqlHelper>(provider =>
{
    var cs = provider.GetRequiredService<HistoricalPriceConnectionString>().Value;
    return new SqlHelper(cs);
});

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(10),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(10),
        QueuePollInterval = TimeSpan.FromSeconds(15),
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true
    }));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount;
    options.Queues = new[] { "default" };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Constellations API", Version = "v1" });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin",
        policy =>
        {
            policy.WithOrigins(builder.Configuration.GetValue<string>("AllowedCorsOrigin") ?? "http://localhost:3000") 
              .AllowAnyHeader()
              .AllowAnyMethod();
        });
    }
);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Constellations API v1"));
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("AllowSpecificOrigin");

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new Hangfire.Dashboard.AllowAllConnectionsFilter() }
});
app.MapControllers();

app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();

namespace Hangfire.Dashboard
{
    public class AllowAllConnectionsFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context) => true;
    }
}