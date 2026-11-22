using System.Text;
using Api_Multimedia.Data;
using Api_Multimedia.Models;
using Microsoft.AspNetCore.Mvc;

namespace Api_Multimedia.Controllers;

[ApiController]
[Route("api/productos")]
public class ProductosController : ControllerBase
{
    private const long MaxImagenBytes = 5L * 1024 * 1024;   // 5 MB
    private const long MaxVideoBytes = 30L * 1024 * 1024;   // 30 MB

    private const string CarpetaImagenes = "Uploads/imagenes";
    private const string CarpetaVideos = "Uploads/videos";

    // Extensiones permitidas para imagen y el Content-Type que les corresponde
    private static readonly Dictionary<string, string> ExtensionesImagen = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp"
    };

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public ProductosController(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    // ---------------------------------------------------------------
    // Productos (solo para crear y consultar registros de prueba)
    // ---------------------------------------------------------------

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearProductoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Nombre))
            return BadRequest(new { mensaje = "El nombre es obligatorio." });

        var producto = new Producto { Nombre = request.Nombre.Trim() };
        _db.Productos.Add(producto);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(ObtenerPorId), new { id = producto.Id }, producto);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> ObtenerPorId(int id)
    {
        var producto = await _db.Productos.FindAsync(id);
        if (producto is null)
            return NotFound(new { mensaje = "Producto no encontrado." });

        return Ok(producto);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id)
    {
        var producto = await _db.Productos.FindAsync(id);
        if (producto is null)
            return NotFound(new { mensaje = "Producto no encontrado." });

        BorrarArchivo(producto.ImagenRuta);
        BorrarArchivo(producto.VideoRuta);

        _db.Productos.Remove(producto);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // ---------------------------------------------------------------
    // Imagen en Base64
    // ---------------------------------------------------------------

    [HttpPut("{id:int}/imagen-base64")]
    public async Task<IActionResult> SubirImagenBase64(int id, [FromBody] ImagenBase64Request request)
    {
        var producto = await _db.Productos.FindAsync(id);
        if (producto is null)
            return NotFound(new { mensaje = "Producto no encontrado." });

        if (string.IsNullOrWhiteSpace(request.NombreArchivo) || string.IsNullOrWhiteSpace(request.Base64))
            return BadRequest(new { mensaje = "Los campos nombreArchivo y base64 son obligatorios." });

        // 1. Validar extensión
        var extension = Path.GetExtension(request.NombreArchivo);
        if (!ExtensionesImagen.TryGetValue(extension, out var contentTypePorExtension))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType,
                new { mensaje = "Formato no permitido. Solo se aceptan JPG, PNG y WebP." });

        // Si viene como data URI (data:image/png;base64,....) se toma solo la parte Base64
        var base64 = request.Base64.Trim();
        var coma = base64.IndexOf(',');
        if (base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && coma >= 0)
            base64 = base64[(coma + 1)..];

        // 2. Decodificar
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return BadRequest(new { mensaje = "El contenido Base64 no es válido." });
        }

        if (bytes.Length == 0)
            return BadRequest(new { mensaje = "El archivo está vacío." });

        // 3. Validar tamaño
        if (bytes.Length > MaxImagenBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { mensaje = "La imagen supera el tamaño máximo de 5 MB." });

        // 4. Validar contenido real (firma / magic bytes) y que coincida con la extensión
        var contentTypeReal = DetectarTipoImagen(bytes);
        if (contentTypeReal is null || contentTypeReal != contentTypePorExtension)
            return StatusCode(StatusCodes.Status415UnsupportedMediaType,
                new { mensaje = "El contenido del archivo no corresponde a una imagen JPG, PNG o WebP válida, o no coincide con su extensión." });

        // 5. Guardar en disco con nombre Guid
        var extensionFinal = contentTypeReal switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => ".webp"
        };
        var rutaRelativa = $"{CarpetaImagenes}/{Guid.NewGuid()}{extensionFinal}";
        var rutaFisica = RutaFisica(rutaRelativa);
        Directory.CreateDirectory(Path.GetDirectoryName(rutaFisica)!);
        await System.IO.File.WriteAllBytesAsync(rutaFisica, bytes);

        // 6. Actualizar referencia y eliminar la imagen anterior
        var rutaAnterior = producto.ImagenRuta;
        producto.ImagenRuta = rutaRelativa;
        producto.ImagenContentType = contentTypeReal;
        await _db.SaveChangesAsync();
        BorrarArchivo(rutaAnterior);

        return Ok(producto);
    }

    [HttpGet("{id:int}/imagen")]
    public async Task<IActionResult> ObtenerImagen(int id)
    {
        var producto = await _db.Productos.FindAsync(id);
        if (producto is null)
            return NotFound(new { mensaje = "Producto no encontrado." });

        if (producto.ImagenRuta is null || !System.IO.File.Exists(RutaFisica(producto.ImagenRuta)))
            return NotFound(new { mensaje = "El producto no tiene imagen." });

        return PhysicalFile(RutaFisica(producto.ImagenRuta), producto.ImagenContentType ?? "application/octet-stream");
    }

    [HttpDelete("{id:int}/imagen")]
    public async Task<IActionResult> EliminarImagen(int id)
    {
        var producto = await _db.Productos.FindAsync(id);
        if (producto is null)
            return NotFound(new { mensaje = "Producto no encontrado." });

        if (producto.ImagenRuta is null)
            return NotFound(new { mensaje = "El producto no tiene imagen." });

        BorrarArchivo(producto.ImagenRuta);
        producto.ImagenRuta = null;
        producto.ImagenContentType = null;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // ---------------------------------------------------------------
    // Video MP4 (multipart/form-data)
    // ---------------------------------------------------------------

    [HttpPost("{id:int}/video")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SubirVideo(int id, IFormFile? archivo)
    {
        var producto = await _db.Productos.FindAsync(id);
        if (producto is null)
            return NotFound(new { mensaje = "Producto no encontrado." });

        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "Debe enviar un archivo en el campo 'archivo'." });

        // 1. Validar tamaño
        if (archivo.Length > MaxVideoBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { mensaje = "El video supera el tamaño máximo de 30 MB." });

        // 2. Validar extensión
        if (!string.Equals(Path.GetExtension(archivo.FileName), ".mp4", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType,
                new { mensaje = "Formato no permitido. Solo se acepta MP4." });

        // 3. Validar contenido real: un MP4 tiene la caja 'ftyp' en los bytes 4 a 7
        var cabecera = new byte[12];
        await using (var lectura = archivo.OpenReadStream())
        {
            var leidos = await lectura.ReadAtLeastAsync(cabecera, cabecera.Length, throwOnEndOfStream: false);
            if (leidos < 12 || Encoding.ASCII.GetString(cabecera, 4, 4) != "ftyp")
                return StatusCode(StatusCodes.Status415UnsupportedMediaType,
                    new { mensaje = "El contenido del archivo no corresponde a un video MP4 válido." });
        }

        // 4. Guardar en disco con nombre Guid
        var rutaRelativa = $"{CarpetaVideos}/{Guid.NewGuid()}.mp4";
        var rutaFisica = RutaFisica(rutaRelativa);
        Directory.CreateDirectory(Path.GetDirectoryName(rutaFisica)!);
        await using (var destino = System.IO.File.Create(rutaFisica))
        {
            await archivo.CopyToAsync(destino);
        }

        // 5. Actualizar referencia y eliminar el video anterior
        var rutaAnterior = producto.VideoRuta;
        producto.VideoRuta = rutaRelativa;
        await _db.SaveChangesAsync();
        BorrarArchivo(rutaAnterior);

        return Ok(producto);
    }

    [HttpGet("{id:int}/video")]
    public async Task<IActionResult> ObtenerVideo(int id)
    {
        var producto = await _db.Productos.FindAsync(id);
        if (producto is null)
            return NotFound(new { mensaje = "Producto no encontrado." });

        if (producto.VideoRuta is null || !System.IO.File.Exists(RutaFisica(producto.VideoRuta)))
            return NotFound(new { mensaje = "El producto no tiene video." });

        return PhysicalFile(RutaFisica(producto.VideoRuta), "video/mp4", enableRangeProcessing: true);
    }

    [HttpDelete("{id:int}/video")]
    public async Task<IActionResult> EliminarVideo(int id)
    {
        var producto = await _db.Productos.FindAsync(id);
        if (producto is null)
            return NotFound(new { mensaje = "Producto no encontrado." });

        if (producto.VideoRuta is null)
            return NotFound(new { mensaje = "El producto no tiene video." });

        BorrarArchivo(producto.VideoRuta);
        producto.VideoRuta = null;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // ---------------------------------------------------------------
    // Utilidades
    // ---------------------------------------------------------------

    private string RutaFisica(string rutaRelativa) =>
        Path.Combine(_env.ContentRootPath, rutaRelativa.Replace('/', Path.DirectorySeparatorChar));

    private void BorrarArchivo(string? rutaRelativa)
    {
        if (string.IsNullOrEmpty(rutaRelativa))
            return;

        var rutaFisica = RutaFisica(rutaRelativa);
        if (System.IO.File.Exists(rutaFisica))
            System.IO.File.Delete(rutaFisica);
    }

    // Detecta el tipo de imagen por sus primeros bytes (firma del archivo)
    private static string? DetectarTipoImagen(byte[] b)
    {
        // JPEG: FF D8 FF
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            return "image/jpeg";

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 &&
            b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
            return "image/png";

        // WebP: "RIFF" .... "WEBP"
        if (b.Length >= 12 &&
            Encoding.ASCII.GetString(b, 0, 4) == "RIFF" &&
            Encoding.ASCII.GetString(b, 8, 4) == "WEBP")
            return "image/webp";

        return null;
    }
}

public record CrearProductoRequest(string? Nombre);

public record ImagenBase64Request(string? NombreArchivo, string? Base64);
