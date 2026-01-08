using Library.Data.PostgreSql;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderWorkerService.Consumers;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((hostContext, services) =>
{
    services.AddMassTransit(x =>
    {
        var connectionString = hostContext.Configuration.GetConnectionString("DbConnectionString");
        services.AddDbContext<BookContext>(options =>
            options.UseNpgsql(connectionString));
        
        x.AddConsumer<SubmitOrderConsumer>();

        x.UsingRabbitMq((context, cfg) =>
        {
            var rabbitMqConfig = hostContext.Configuration.GetSection("MassTransit:RabbitMq");

            cfg.Host(rabbitMqConfig["Host"], "/", h =>
            {
                h.Username(rabbitMqConfig["Username"]!);
                h.Password(rabbitMqConfig["Password"]!);
            });
            
            cfg.UseMessageRetry(r => r.Incremental(5, 
                TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200)));

            cfg.ConfigureEndpoints(context);
        });
    })
        .AddOpenTelemetry()
        .WithTracing(b => b
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("OrderWorkerService"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("MassTransit")
            .AddJaegerExporter(options =>
            {
                options.Endpoint = new Uri("http://jaeger-tracing.svc.cluster.local:14268/api/traces");
                options.Protocol = JaegerExportProtocol.HttpBinaryThrift;
            })
            .AddZipkinExporter(o =>
            {
                o.Endpoint = new Uri("http://zipkin:9411/api/v2/spans");
            })
        )
        .WithMetrics(m => m
            .AddHttpClientInstrumentation()
            .AddPrometheusExporter());
});

var host = builder.Build();

await host.RunAsync();