using Hangfire;
using Hangfire.SqlServer;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using MathNet.Numerics;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;
using Temperance.Constellations.BackTesting.Interfaces;
using Temperance.Constellations.Repositories.Implementations;
using Temperance.Constellations.Repositories.Interfaces;
using Temperance.Constellations.Repositories.Interfaces.HistoricalData.Implementations;
using Temperance.Constellations.Repositories.Interfaces.Training;
using Temperance.Constellations.Repositories.Interfaces.WalkForward.Implementations;
using Temperance.Constellations.Services;
using Temperance.Constellations.Services.Implementations;
using Temperance.Constellations.Services.Interfaces;
using Temperance.Constellations.Settings;
using Temperance.Constellations.src.Core.Services.Implementations;
using Temperance.Constellations.src.Data.Repositories.HistoricalPrices.Implementations;
using Temperance.Ephemeris.Repositories.Constellations.Implementations;
using Temperance.Ephemeris.Repositories.Constellations.Interfaces;
using Temperance.Ephemeris.Repositories.Financials.Implementations;
using Temperance.Ephemeris.Repositories.Financials.Interfaces;
using Temperance.Ephemeris.Repositories.Ludus.Implementations;
using Temperance.Ephemeris.Repositories.Ludus.Interfaces;
using Temperance.Ephemeris.Services.Financials.Implementation;
using Temperance.Ephemeris.Services.Financials.Interfaces;
using Temperance.Ephemeris.Utilities.Helpers;
using Temperance.Hermes.Connection;
using Temperance.Hermes.Publishing;
using Temperance.Services.BackTesting.Implementations;
using Temperance.Services.Factories.Implementations;
using Temperance.Services.Factories.Interfaces;
using Temperance.Services.Services.Implementations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ConnectionStrings>(
    builder.Configuration.GetSection("ConnectionStrings"));

builder.Services.Configure<AlphaVantageSettings>(
    builder.Configuration.GetSection("AlphaVantageSettings"));

builder.Services.Configure<ConductorSettings>(
    builder.Configuration.GetSection("ConductorSettings"));

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.AddConfiguration(builder.Configuration.GetSection("Logging"));
    loggingBuilder.AddConsole();
    loggingBuilder.AddDebug();
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ConductorSettings>>().Value);

builder.Services.AddHttpClient<IConductorClient, ConductorClient>();

builder.Services.AddTransient<IHangfireTestService, HangfireTestService>();
builder.Services.AddTransient<IEarningsService, EarningsService>();
builder.Services.AddTransient<IStrategyFactory, StrategyFactory>();
builder.Services.AddTransient<IPerformanceCalculator, PerformanceCalculator>();
builder.Services.AddTransient<ITransactionCostService, TransactionCostService>();
builder.Services.AddTransient<ILiquidityService, LiquidityService>();
builder.Services.AddTransient<IGpuIndicatorService, GpuIndicatorService>();
builder.Services.AddScoped<IPortfolioManager, PortfolioManager>();
builder.Services.AddScoped<IShadowPortfolioManager, ShadowPortfolioManager>(); 
builder.Services.AddScoped<ISecurityMasterService, SecurityMasterService>();
builder.Services.AddScoped<ISecuritiesOverviewService, SecuritiesOverviewService>();
builder.Services.AddTransient<IBalanceSheetService, BalanceSheetService>();
builder.Services.AddTransient<IPriceService, PriceService>();
builder.Services.AddTransient<IHistoricalPriceService, HistoricalPriceService>();
builder.Services.AddTransient<IConductorService, ConductorService>();
builder.Services.AddTransient<ITradeService, TradesService>();
builder.Services.AddTransient<IQualityFilterService, QualityFilterService>();
builder.Services.AddTransient<IMarketHealthService, MarketHealthService>();
builder.Services.AddTransient<IEconomicDataService, EconomicDataService>();
builder.Services.AddTransient<ISecuritiesService, SecuritiesService>();
//builder.Services.AddTransient<IMasterWalkForwardOrchestrator, MasterWalkForwardOrchestrator>();
builder.Services.AddTransient<IInitialTrainingOrchestrator, InitialTrainingOrchestrator>();
//builder.Services.AddTransient<ISingleSecurityBacktester, ShadowBacktestOrchestrator>();
//builder.Services.AddTransient<IShadowBacktestOrchestrator, ShadowBacktestOrchestrator>();
//builder.Services.AddTransient<IPortfolioBacktestOrchestrator, PortfolioBacktestOrchestrator>();
//builder.Services.AddTransient<IPortfolioBacktestRunner, PortfolioBacktestRunner>();
// =========================================================================
// CONSTELLATIONS PHASE 3: THE AIRTIGHT REGISTRY
// =========================================================================

// 1. Map the Interfaces to the Implementations (Crucial for MasterBacktestRunner)
builder.Services.AddScoped<IWalkForwardSleeveRepository, WalkForwardSleeveRepository>();
builder.Services.AddScoped<IShadowPerformanceRepository, ShadowPerformanceRepository>();
builder.Services.AddScoped<IStrategyOptimizedParametersRepository, StrategyOptimizedParametersRepository>();
builder.Services.AddScoped<IPortfolioTelemetryRepository, PortfolioTelemetryRepository>();
// 2. Repositories requiring SQL Helper (Manual Factory)
builder.Services.AddScoped<IWalkForwardSessionRepository>(provider =>
{
    var options = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var sqlHelper = provider.GetRequiredService<ISqlHelper>();
    return new WalkForwardSessionRepository(options.DefaultConnectionString, sqlHelper);
});
builder.Services.AddScoped<ISecuritiesRepository>(provider =>
{
    var options = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    return new SecuritiesRepository(options.DefaultConnectionString);
});
builder.Services.AddScoped<ICycleTrackerRepository>(provider =>
{
    var options = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var sqlHelper = provider.GetRequiredService<ISqlHelper>();
    return new CycleTrackerRepository(options.DefaultConnectionString, sqlHelper);
});

// 3. Hermes/Nuncio Messaging (Outbound Only)
// Note: RabbitMqPublisher needs IRabbitMqConnectionFactory to be born.
builder.Services.AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>();
builder.Services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();

// 4. The Orchestrators (The "Big Brains")
builder.Services.AddScoped<IMasterBacktestRunner, MasterBacktestRunner>();
builder.Services.AddScoped<IPortfolioCommitteeService, PortfolioCommitteeService>();

// =========================================================================
builder.Services.AddTransient<IHistoricalDataRepository>(provider =>
{
    var connectionString = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var defaultConnnection = connectionString.DefaultConnectionString;
    var securitiesOverviewRepository = provider.GetRequiredService<ISecuritiesOverviewRepository>();
    return new HistoricalDataRepository(defaultConnnection, securitiesOverviewRepository);
});
builder.Services.AddTransient<IOptimizationRepository>(provider =>
{
    var connectionString = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var defaultConnnection = connectionString.DefaultConnectionString;
    return new OptimizationRepository(defaultConnnection);
});
builder.Services.AddScoped<ISecuritiesOverviewRepository>(provider =>
{
    var connectionStrings = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var cs = connectionStrings.DefaultConnectionString;
    var logger = provider.GetRequiredService<ILogger<SecuritiesOverviewRepository>>();
    return new SecuritiesOverviewRepository(cs, logger);
});

builder.Services.AddScoped<ISecurityMasterRepsotory>(provider =>
{
    var connectionStrings = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var cs = connectionStrings.DefaultConnectionString;
    var logger = provider.GetRequiredService<ILogger<SecurityMasterRepository>>();
    return new SecurityMasterRepository(cs);
});

builder.Services.AddTransient<IHistoricalPriceRepository>(provider =>
{
    var connectionStrings = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var historicalConnectionString = connectionStrings.HistoricalPricesConnection;
    var sqlHelper = provider.GetRequiredService<ISqlHelper>();
    var securitiesOverviewRepository = provider.GetRequiredService<ISecuritiesOverviewRepository>();
    return new HistoricalPriceRepository(historicalConnectionString, securitiesOverviewRepository, sqlHelper);
});

builder.Services.AddTransient<IEarningsRepository>(provider =>
{
    var connectionString = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var defaultConnnection = connectionString.DefaultConnectionString;
    return new EarningsRepository(defaultConnnection);
});

builder.Services.AddTransient<IBalanceSheetRepository>(provider =>
{
    var connectionString = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var defaultConnnection = connectionString.DefaultConnectionString;
    return new BalanceSheetRepository(defaultConnnection);
});

// Ensure the interface type matches exactly what TradesService wants
builder.Services.AddTransient<Temperance.Constellations.Repositories.Interfaces.Trade.Interfaces.IBacktestRepository>(provider =>
{
    var connectionString = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var logger = provider.GetRequiredService<ILogger<Temperance.Constellations.Repositories.Interfaces.Trade.Implementations.BacktestRepository>>();
    // Verify your BacktestRepository actually implements IBacktestRepository
    return new Temperance.Constellations.Repositories.Interfaces.Trade.Implementations.BacktestRepository(connectionString.DefaultConnectionString, logger);
});

builder.Services.AddTransient<ITradeRepository>(provider =>
{
    var connectionString = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var defaultConnnection = connectionString.DefaultConnectionString;
    var logger = provider.GetRequiredService<ILogger<TradeRepository>>();
    return new TradeRepository(defaultConnnection, logger);
});

builder.Services.AddSingleton<ISqlHelper>(provider =>
{
    var connectionString = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var historicalDbConnection = connectionString.HistoricalPricesConnection;
    return new SqlHelper(historicalDbConnection);
});

builder.Services.AddSingleton<IIndicatorRepository>(provider =>
{
    var connectionString = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var defaultConnnection = connectionString.DefaultConnectionString;
    var logger = provider.GetRequiredService<ILogger<IndicatorRepository>>();
    return new IndicatorRepository(defaultConnnection, logger);
});

builder.Services.AddSingleton<IWalkForwardRepository>(provider =>
{
    var connectionString = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var defaultConnnection = connectionString.DefaultConnectionString;
    var logger = provider.GetRequiredService<ILogger<WalkForwardRepository>>();
    return new WalkForwardRepository(defaultConnnection, logger);
});

builder.Services.AddSingleton<IPerformanceRepository>(provider =>
{
   var connectionString = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var defaultConnnection = connectionString.DefaultConnectionString;
    var logger = provider.GetRequiredService<ILogger<PerformanceRepository>>();
    return new PerformanceRepository(defaultConnnection, logger);
});

builder.Services.AddSingleton<ITrainingRepository>(provider =>
{
    var connectionString = provider.GetRequiredService<IOptions<ConnectionStrings>>().Value;
    var defaultConnnection = connectionString.DefaultConnectionString;

    var logger = provider.GetRequiredService<ILogger<TrainingRepository>>();
    return new TrainingRepository(logger, defaultConnnection);
});

builder.Services.AddSingleton<Accelerator>(sp =>
{ 
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
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection"), new SqlServerStorageOptions
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