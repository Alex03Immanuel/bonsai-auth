using Serilog;
using Serilog.Formatting.Elasticsearch;
using Serilog.Sinks.Http;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// Enable Serilog self-logging to diagnose sink issues
Serilog.Debugging.SelfLog.Enable(Console.Error);

// GET THE ENVIRONMENT VARIABLES FOR ELASTICSEARCH/OPENSEARCH LOGGING
var esUri = Environment.GetEnvironmentVariable("BONSAI_URL");

// CONFIGURING LOGGER
var loggerCfg = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .Enrich.WithProperty("Application", "bonsai-auth")
    .WriteTo.Console();

// If BONSAI_URL is set, configure HTTP sink for OpenSearch
if (!string.IsNullOrEmpty(esUri))
{
    try
    {
        var uri = new Uri(esUri);
        var bulkEndpoint = $"{uri.Scheme}://{uri.Host}:{(uri.Port > 0 ? uri.Port : 443)}/_bulk";
        
        // Extract credentials from URI
        var credentials = uri.UserInfo;
        
        Console.WriteLine($"[Startup] Configuring OpenSearch HTTP sink");
        Console.WriteLine($"[Startup] Host: {uri.Host}");
        Console.WriteLine($"[Startup] Index pattern: auth-logs-yyyy.MM.dd");
        
        loggerCfg.WriteTo.Http(
            requestUri: bulkEndpoint,
            queueLimitBytes: null,
            textFormatter: new ElasticsearchJsonFormatter(renderMessageTemplate: false, inlineFields: true),
            batchFormatter: new OpenSearchBatchFormatter(),
            httpClient: new OpenSearchHttpClient(credentials)
        );
        
        Console.WriteLine("[Startup] OpenSearch HTTP sink configured successfully");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Startup] Failed to configure OpenSearch sink: {ex.Message}");
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

// CORS Configuration - Allow frontend to call API
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "https://bonsai-auth-frontend.onrender.com",
                "http://localhost:5500",  // For local development
                "http://127.0.0.1:5500"   // For local development
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// DEPENDENCY INJECTION
builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();
builder.Services.AddSingleton<IOtpStore, RedisOtpStore>();
builder.Services.AddSingleton<IEmailService, EmailService>();

// CONTROLLERS & SWAGGER
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Use CORS - MUST be before other middleware
app.UseCors("AllowFrontend");

// Request logging middleware
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
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

// Custom HTTP client for OpenSearch with Basic Auth
public class OpenSearchHttpClient : IHttpClient
{
    private readonly HttpClient _httpClient;

    public OpenSearchHttpClient(string credentials)
    {
        _httpClient = new HttpClient();
        
        if (!string.IsNullOrEmpty(credentials))
        {
            var authBytes = Encoding.ASCII.GetBytes(credentials);
            var authHeader = Convert.ToBase64String(authBytes);
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Basic", authHeader);
        }
        
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public void Configure(IConfiguration configuration) { }

    public async Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream, CancellationToken cancellationToken)
    {
        using var content = new StreamContent(contentStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        
        var response = await _httpClient.PostAsync(requestUri, content, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.Error.WriteLine($"[OpenSearch] Error: {response.StatusCode} - {body}");
        }
        
        return response;
    }

    public void Dispose() => _httpClient?.Dispose();
}

// Custom batch formatter for OpenSearch bulk API
public class OpenSearchBatchFormatter : IBatchFormatter
{
    public void Format(IEnumerable<string> logEvents, TextWriter output)
    {
        foreach (var logEvent in logEvents)
        {
            if (string.IsNullOrWhiteSpace(logEvent))
                continue;

            // Get current date-based index name
            var indexName = $"auth-logs-{DateTime.UtcNow:yyyy.MM.dd}";
            
            // Write the bulk API action line
            output.Write($"{{\"index\":{{\"_index\":\"{indexName}\"}}}}");
            output.Write('\n');
            
            // Write the document
            output.Write(logEvent);
            output.Write('\n');
        }
    }
}