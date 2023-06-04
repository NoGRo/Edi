using Edi.Core;
using Edi.Forms;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Edi Rest", Version = "v1" });
});


builder.Services.AddEdi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();



using (var serviceScope = app.Services.CreateScope())
{
    var edi = serviceScope.ServiceProvider.GetRequiredService<IEdi>();
    await edi.Init();

}
app.Run();
Main();
[STAThread] // Esta línea asegura que el hilo principal se ejecute en el modelo de subprocesamiento STA
static void Main()
{
    App app = new App();
    MainWindow mainWindow = new MainWindow();
    app.Run(mainWindow);
}