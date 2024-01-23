using Edi.Core;
using System;
using webapi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
var Edi = EdiBuilder.Create("EdiConfig.json");
await Edi.Init();
AppStatic.Edi = Edi;


app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
