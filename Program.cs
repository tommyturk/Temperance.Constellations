using Hangfire;
using Hangfire.SqlServer;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;
using Temperance.Data.Data.Repositories.BalanceSheet.Implementation;
using Temperance.Data.Data.Repositories.BalanceSheet.Interface;
using Temperance.Data.Data.Repositories.Securities.Implementations;
using Temperance.Data.Data.Repositories.Securities.Interfaces;
using Temperance.Data.Data.Repositories.Trade.Implementations;
using Temperance.Data.Data.Repositories.Trade.Interfaces;
using Temperance.Data.Repositories.Securities.Implementations;
using Temperance.Services.BackTesting.Implementations;
using Temperance.Services.BackTesting.Interfaces;
using Temperance.Services.Factories.Implementations;
using Temperance.Services.Factories.Interfaces;
using Temperance.Services.Services.Implementations;
using Temperance.Services.Services.Interfaces;
using Temperance.Settings.Settings;
using Temperance.Utilities.Helpers;
using TradingApp.src.Core.Services.Implementations;
using TradingApp.src.Core.Services.Interfaces;
using TradingApp.src.Data.Repositories.HistoricalPrices.Implementations;
using TradingApp.src.Data.Repositories.HistoricalPrices.Interfaces;

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

builder.Services.Configure<ConductorSettings>(builder.Configuration.GetSection("ConductorSettings"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ConductorSettings>>().Value);

builder.Services.AddHttpClient<IAlphaVantageService, AlphaVantageService>();

builder.Services.AddSingleton(new DefaultConnectionString(connectionString));
builder.Services.AddSingleton(new HistoricalPriceConnectionString(historicalConnectionString));

builder.Services.AddTransient<IHangfireTestService, HangfireTestService>();

builder.Services.AddTransient<IEarningsService, EarningsService>();
builder.Services.AddTransient<IStrategyFactory, StrategyFactory>();
builder.Services.AddTransient<IPerformanceCalculator, PerformanceCalculator>();
builder.Services.AddScoped<IBacktestRunner, BacktestRunner>();
builder.Services.AddTransient<ITransactionCostService, TransactionCostService>();
builder.Services.AddTransient<ILiquidityService, LiquidityService>();
builder.Services.AddTransient<IGpuIndicatorService, GpuIndicatorService>();
builder.Services.AddScoped<IPortfolioManager, PortfolioManager>();
builder.Services.AddTransient<ISecuritiesOverviewService, SecuritiesOverviewService>();
builder.Services.AddTransient<IBalanceSheetService, BalanceSheetService>();
builder.Services.AddTransient<IPriceService, PriceService>();
builder.Services.AddTransient<IHistoricalPriceService, HistoricalPriceService>();
builder.Services.AddTransient<IConductorService, ConductorService>();
builder.Services.AddTransient<ITradeService, TradesService>();
builder.Services.AddTransient<ISecuritiesOverviewRepository>(provider =>
{
    var cs = provider.GetRequiredService<DefaultConnectionString>().Value;
    var logger = provider.GetRequiredService<ILogger<SecuritiesOverviewRepository>>();
    return new SecuritiesOverviewRepository(cs, logger);
});

builder.Services.AddTransient<IHistoricalPriceRepository>(provider =>
{
    var historicalConnectionString = provider.GetRequiredService<HistoricalPriceConnectionString>().Value;
    var sqlHelper = provider.GetRequiredService<ISqlHelper>();
    var securitiesOverviewRepository = provider.GetRequiredService<ISecuritiesOverviewRepository>();
    return new HistoricalPriceRepository(historicalConnectionString, securitiesOverviewRepository, sqlHelper);
});

builder.Services.AddTransient<IEarningsRepository>(provider =>
{
    var connectionString = provider.GetRequiredService<DefaultConnectionString>().Value;

    return new EarningsRepository(connectionString);
});

builder.Services.AddTransient<IBalanceSheetRepository>(provider =>
{
    var connectionString = provider.GetRequiredService<DefaultConnectionString>().Value;

    return new BalanceSheetRepository(connectionString);
});

builder.Services.AddTransient<IBacktestRepository>(provider =>
{
    var cs = provider.GetRequiredService<DefaultConnectionString>().Value;
    var logger = provider.GetRequiredService<ILogger<BacktestRepository>>();
    return new BacktestRepository(cs, logger);
});

builder.Services.AddTransient<ITradeRepository>(provider =>
{
    var connectionString = provider.GetRequiredService<DefaultConnectionString>().Value;
    var logger = provider.GetRequiredService<ILogger<TradeRepository>>();
    return new TradeRepository(connectionString, logger);
});

builder.Services.AddSingleton<ISqlHelper>(provider =>
{
    var cs = provider.GetRequiredService<HistoricalPriceConnectionString>().Value;
    return new SqlHelper(cs);
});

builder.Services.AddSingleton<Accelerator>(sp =>
{
    //var logger = sp.GetRequiredService<ILogger<Program>>(); 
    //logger.LogInformation("Initializing ILGPU Context and Accelerator...");

    //try
    //{
    //    var context = Context.Create(builder => builder.Default().EnableAlgorithms());

    //    var device = context.GetPreferredDevice(preferCPU: false);

    //    if (device == null)
    //    {
    //        logger.LogWarning("No suitable ILGPU device found. GPU acceleration will be unavailable.");
    //        throw new InvalidOperationException("ILGPU Accelerator could not be created as no suitable device was found.");
    //    }

    //    logger.LogInformation("Found ILGPU Device: {DeviceName}", device.Name);

    //    var accelerator = device.CreateAccelerator(context);
    //    logger.LogInformation("ILGPU Accelerator created successfully for {AcceleratorName}", accelerator.Name);

    //    return accelerator;
    //}
    //catch (Exception ex)
    //{
    //    logger.LogCritical(ex, "A critical error occurred while initializing the ILGPU Accelerator. The application cannot start.");
    //    throw;
    //}
    var logger = sp.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Initializing ILGPU Context and Accelerator...");
    var context = Context.Create(builder => builder.Default().EnableAlgorithms());
    var device = context.GetPreferredDevice(preferCPU: false);
    if (device == null)
    {
        logger.LogCritical("No suitable ILGPU device found.");
        throw new InvalidOperationException("ILGPU Accelerator could not be created.");
    }
    logger.LogInformation("Found ILGPU Device: {DeviceName}", device.Name);
    var accelerator = device.CreateAccelerator(context);
    logger.LogInformation("ILGPU Accelerator created successfully for {AcceleratorName}", accelerator.Name);
    return accelerator;
});

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(10),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(10),
        QueuePollInterval = TimeSpan.FromSeconds(15),
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true,
        SchemaName = "HangFire"
    }));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount;
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