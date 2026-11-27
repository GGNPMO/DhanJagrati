namespace DhanJagrati.Api.Infrastructure;

/// <summary>
/// Stores uploaded images (member photo / signature) outside the web root. The database keeps
/// only the relative path; files are served through authorised endpoints.
/// </summary>
public sealed class FileStore
{
    public const long MaxImageBytes = 1024 * 1024;
    private readonly string _root;

    public FileStore(IConfiguration config, IWebHostEnvironment env)
    {
        _root = Path.GetFullPath(config["App:UploadPath"] ?? Path.Combine(env.ContentRootPath, "App_Data", "uploads"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>Validates (JPEG/PNG by signature, size) and saves; returns the relative path.</summary>
    public async Task<string> SaveImageAsync(IFormFile file, string folder, string prefix, CancellationToken ct)
    {
        if (file.Length == 0) throw new Data.DomainException("The file is empty.");
        if (file.Length > MaxImageBytes) throw new Data.DomainException("Image must be 1 MB or smaller.");

        var header = new byte[8];
        await using (var probe = file.OpenReadStream())
            _ = await probe.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);

        var extension = header switch
        {
            [0xFF, 0xD8, 0xFF, ..] => ".jpg",
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A] => ".png",
            _ => throw new Data.DomainException("Only JPEG or PNG images are allowed.")
        };

        var relative = Path.Combine(folder, $"{prefix}-{Guid.NewGuid():N}{extension}");
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var target = File.Create(full);
        await file.CopyToAsync(target, ct);
        return relative.Replace('\\', '/');
    }

    public (string FullPath, string ContentType)? Open(string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return null;
        var full = Resolve(relative);
        if (!File.Exists(full)) return null;
        return (full, full.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg");
    }

    public void Delete(string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return;
        var full = Resolve(relative);
        if (File.Exists(full)) File.Delete(full);
    }

    /// <summary>Maps a stored relative path to disk, refusing anything outside the upload root.</summary>
    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid file path.");
        return full;
    }
}
