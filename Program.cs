using Serilog;
using Serilog.Sinks.Elasticsearch;
using System;

var builder = WebApplication.CreateBuilder(args);

// GET THE ENVIRONMENT VARIABLES FOR ELASTICSEARCH LOGGING
var esUri = Environment.GetEnvironmentVariable("BONSAI_URL"); // e.g. https://user:pass@xxxx.bonsai.io

// CONFIGURING LOGGER MACHINE 
var loggerCfg = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .WriteTo.Console();

// If BONSAI_URL is set, configure Elasticsearch sink 

if (!string.IsNullOrEmpty(esUri))
{
    try
    {
        loggerCfg.WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(esUri))
        {
            AutoRegisterTemplate = true,
            IndexFormat = "auth-logs-{0:yyyy.MM.dd}",
            FailureCallback = (logEvent, ex) => Console.Error.WriteLine("Elasticsearch sink failure: " + ex?.Message),
            EmitEventFailure = EmitEventFailureHandling.WriteToSelfLog |
                                EmitEventFailureHandling.WriteToFailureSink
        });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("Failed to configure Elasticsearch sink: " + ex.Message);
    }
}

// CREATE THE LOGGER

Log.Logger = loggerCfg.CreateLogger();
builder.Host.UseSerilog();

// DEPENDENCY INJECTION
builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();
builder.Services.AddSingleton<IOtpStore, RedisOtpStore>();
builder.Services.AddSingleton<IEmailService, EmailService>();

// CONTROLLERS & SWAGGER
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSerilogRequestLogging();

// SWAGGER IN DEV ONLY

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Lifetime.ApplicationStopped.Register(() => Log.CloseAndFlush());

app.Run();
