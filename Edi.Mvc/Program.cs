using Edi.Core;
using Edi.Core.Controllers;
using Edi.Core.Gallery;

var builder = WebApplication.CreateBuilder(args);

var configuredEdiPath = builder.Configuration["EdiMvc:ConfigPath"]
    ?? Environment.GetEnvironmentVariable("EDI_CONFIG_PATH")
    ?? "EdiConfig.json";
var ediConfigPath = Path.IsPathRooted(configuredEdiPath)
    ? configuredEdiPath
    : Path.GetFullPath(configuredEdiPath, builder.Environment.ContentRootPath);

builder.Services.AddEdi(ediConfigPath);
builder.Services
    .AddControllers()
    .AddApplicationPart(typeof(EdiController).Assembly);

var app = builder.Build();

var edi = app.Services.GetRequiredService<IEdi>();
await edi.Init(ediConfigPath);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    const string assetsPrefix = "/Edi/Assets/";
    var requestPath = context.Request.Path.Value;
    if (requestPath?.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase) == true)
    {
        var relativeAssetPath = requestPath[assetsPrefix.Length..].TrimStart('/');
        context.Request.Path = assetsPrefix + relativeAssetPath;
    }

    await next();
});

app.UseRouting();

app.UseAuthorization();

app.MapControllers();
app.UseFiles();


app.Run();
