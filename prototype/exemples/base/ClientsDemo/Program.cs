using ClientsDemo.Data;
using ClientsDemo.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<ClientsDemoContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("ClientsDemo")));
builder.Services.AddScoped<ClientService>();
builder.Services.AddSingleton<Journal>();

var app = builder.Build();
app.MapDefaultControllerRoute();
app.Run();
