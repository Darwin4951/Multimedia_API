using Api_Multimedia.Data;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Límite de subida: video de 30 MB + margen para el resto del formulario multipart.
// Así un archivo algo mayor de 30 MB llega al controlador y este responde 413 con su mensaje.
const long limiteSolicitud = 35L * 1024 * 1024;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = limiteSolicitud;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = limiteSolicitud;
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Crear automáticamente la base de datos SQLite para este ejercicio
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();
