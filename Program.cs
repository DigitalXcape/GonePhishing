using GonePhishing.Models;
using GonePhishing.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ScanDbContext>(options =>
    options.UseSqlite("Data Source=scans.db"));

builder.Services.AddControllersWithViews();
builder.Services.AddHostedService<ScannerBackgroundService>();

builder.Services.AddDbContext<ScanDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ScanDbContext>();
    db.Database.EnsureCreated();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Scan}/{action=Index}/{id?}");

app.Run();