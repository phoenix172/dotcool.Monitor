using dotCool.Monitor;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services
    .AddHttpClient()
    .AddSingleton<DotcoolSubscriber>()
    .AddSingleton<AdvertisementHandler>();

if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<IBleClient, WindowsBleClient>();
}
else
{
    builder.Services.AddSingleton<IBleClient, LinuxBleClient>();
}

var configuredSensors = builder.Configuration.GetSection("sensors").Get<SensorBinding[]>() ?? throw new ArgumentNullException($"Sensors configuration section is missing or empty");
builder.Services.AddSingleton(configuredSensors);
builder.Services
    .AddHostedService<AdvertisementHandler>(svc => svc.GetRequiredService<AdvertisementHandler>())
    .AddHostedService<DotcoolSubscriber>(svc => svc.GetRequiredService<DotcoolSubscriber>());

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();