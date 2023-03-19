using DependencyStore.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddConfiguration();
builder.Services.AddSqlConnection(builder);
builder.Services.AddRepositories();
builder.Services.AddServices();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();