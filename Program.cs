using Serilog;
using Serilog.Sinks.Elasticsearch;
using System;

var builder = WebApplication.CreateBuilder(args);

// Enable Serilog self-logging to diagnose sink issues
Serilog.Debugging.SelfLog.Enable(Console.Error);

// GET THE ENVIRONMENT VARIABLES FOR ELASTICSEARCH LOGGING
var esUri = Environment.GetEnvironmentVariable("BONSAI_URL");

// CONFIGURING LOGGER MACHINE 
var loggerCfg = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .Enrich.WithProperty("Application", "bonsai-auth")
    .WriteTo.Console();

// If BONSAI_URL is set, configure Elasticsearch sink 
if (!string.IsNullOrEmpty(esUri))
{
    Console.WriteLine($"[Startup] Configuring Elasticsearch sink with URI: {new Uri(esUri).Host}");
    
    try
    {
        loggerCfg.WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(esUri))
        {
            // OpenSearch 2.x compatibility settings
            AutoRegisterTemplate = true,
            AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv7,
            
            // Index naming pattern - creates daily indices
            IndexFormat = "auth-logs-{0:yyyy.MM.dd}",
            
            // Bonsai hobby tier settings (single node)
            NumberOfReplicas = 0,
            NumberOfShards = 1,
            
            // Batch settings for better performance
            BatchPostingLimit = 50,
            Period = TimeSpan.FromSeconds(2),
            
            // Error handling - use SelfLog for failures
            EmitEventFailure = EmitEventFailureHandling.WriteToSelfLog |
                               EmitEventFailureHandling.RaiseCallback,
            
            // Connection settings
            ConnectionTimeout = TimeSpan.FromSeconds(30)
        });
        
        Console.WriteLine("[Startup] Elasticsearch sink configured successfully");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Startup] Failed to configure Elasticsearch sink: {ex.Message}");
        Console.Error.WriteLine($"[Startup] Stack trace: {ex.StackTrace}");
    }
}
else
{
    Console.WriteLine("[Startup] BONSAI_URL not set - logging to console only");
}

// CREATE THE LOGGER
Log.Logger = loggerCfg.CreateLogger();
builder.Host.UseSerilog();

// Log startup
Log.Information("Application starting up");
Log.Information("Environment: {Environment}", builder.Environment.EnvironmentName);

// DEPENDENCY INJECTION
builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();
builder.Services.AddSingleton<IOtpStore, RedisOtpStore>();
builder.Services.AddSingleton<IEmailService, EmailService>();

// CONTROLLERS & SWAGGER
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Request logging middleware
app.UseSerilogRequestLogging(options =>
{
    // Customize the message template
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    
    // Attach additional properties to the request completion event
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].ToString());
        diagnosticContext.Set("ClientIP", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    };
});

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

// Graceful shutdown
app.Lifetime.ApplicationStarted.Register(() => Log.Information("Application started"));
app.Lifetime.ApplicationStopping.Register(() => Log.Information("Application stopping"));
app.Lifetime.ApplicationStopped.Register(() => 
{
    Log.Information("Application stopped");
    Log.CloseAndFlush();
});

app.Run();