using Edi.Core;
using Edi.Forms;
using Microsoft.Extensions.DependencyInjection.Extensions;

//var builder = WebApplication.CreateBuilder(args);
//// Add services to the container.

//builder.Services.AddControllers();

//// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen(c =>
//{
//    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Edi Rest", Version = "v1" });
//});


//builder.Services.AddEdi();

//var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();



//}
//var threadWeb = app.RunAsync();
Thread thread = new Thread(() =>
{
    var appForm = new App();
    appForm.Run();
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();

app.Run();
