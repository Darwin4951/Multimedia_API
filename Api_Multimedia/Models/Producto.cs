namespace Api_Multimedia.Models;

public class Producto
{
    public int Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    // Ruta relativa del archivo de imagen en disco (null si no tiene imagen)
    public string? ImagenRuta { get; set; }

    // Content-Type real detectado de la imagen (image/jpeg, image/png o image/webp)
    public string? ImagenContentType { get; set; }

    // Ruta relativa del archivo de video en disco (null si no tiene video)
    public string? VideoRuta { get; set; }
}
