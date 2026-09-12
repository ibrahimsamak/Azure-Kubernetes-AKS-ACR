using OrderFlow.Order.Infrastructure.Messaging;
using OrderFlow.Order.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddOrderPersistence();
builder.AddOrderMessaging();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
