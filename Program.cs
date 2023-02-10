using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReSchedule;

var host = new HostBuilder()
    .ConfigureServices(services =>
    {
        var logger = services.FirstOrDefault(s => s.ServiceType == typeof(ILogger<>));
        if (logger != null)
            services.Remove(logger);

        services.Add(new ServiceDescriptor(typeof(ILogger<>), typeof(FunctionsLogger<>), ServiceLifetime.Transient));
        
        services.AddMemoryCache();
        services.AddHttpClient();
        services.AddSingleton<ChatStorageClient>();
        services.AddSingleton<ApiClient>();
        services.AddSingleton<Bot>();

    })
    .ConfigureFunctionsWorkerDefaults()
    .Build();

host.Run();
