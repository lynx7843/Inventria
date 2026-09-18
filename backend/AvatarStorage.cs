using Microsoft.AspNetCore.Hosting;

namespace Inventria;

/// <summary>
/// Where profile photos live on disk, and the one place that decides whether an
/// upload is actually an image worth keeping.
///
/// Two things the client sends are never trusted. Its filename is never used to
/// name anything on disk - the name written here is always a freshly minted
/// GUID, because building a path out of whatever a caller typed is a path
/// traversal waiting to happen. And its declared Content-Type is never taken at
/// its word either - that header is just a string the caller chose - so the
/// extension a file is saved under is decided by sniffing the bytes it actually
/// starts with.
///
/// Files live under wwwroot/uploads/avatars, served back out by the static file
/// middleware Program.cs wires up over the same folder. Only the relative path
/// under wwwroot - never an absolute filesystem path - is ever stored on a User
/// row or handed back to a caller.
/// </summary>
public static class AvatarStorage
{
    /// <summary>Comfortably more than a profile photo needs, and far less than an upload that would tie up the disk or the request.</summary>
    public const long MaxBytes = 2 * 1024 * 1024;

    private const string RelativeDirectory = "uploads/avatars";

    public static string DirectoryOn(IWebHostEnvironment environment) =>
        Path.Combine(WebRootOf(environment), "uploads", "avatars");

    public static string WebRootOf(IWebHostEnvironment environment) =>
        // WebRootPath is a path string derived from convention, set whether or
        // not the wwwroot folder has been created yet - unlike WebRootFileProvider,
        // which is only wired up if the folder already existed when the host
        // started. Falling back here matters for the same reason Program.cs does
        // not lean on that provider either.
        string.IsNullOrEmpty(environment.WebRootPath)
            ? Path.Combine(environment.ContentRootPath, "wwwroot")
            : environment.WebRootPath;

    /// <summary>
    /// Validates and writes an uploaded file, returning the relative path to
    /// store on the user's row, or an error message safe to show the caller.
    /// </summary>
    public static async Task<(string? RelativePath, string? Error)> SaveAsync(
        IFormFile file, IWebHostEnvironment environment, CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
        {
            return (null, "Choose an image to upload.");
        }

        if (file.Length > MaxBytes)
        {
            return (null, "Image must be 2 MB or smaller.");
        }

        await using var stream = file.OpenReadStream();
        var extension = await SniffImageExtensionAsync(stream, cancellationToken);

        if (extension is null)
        {
            return (null, "File must be a JPEG, PNG, or WebP image.");
        }

        var directory = DirectoryOn(environment);
        Directory.CreateDirectory(directory);

        var filename = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(directory, filename);

        stream.Position = 0;
        await using (var output = File.Create(fullPath))
        {
            await stream.CopyToAsync(output, cancellationToken);
        }

        return ($"{RelativeDirectory}/{filename}", null);
    }

    /// <summary>
    /// Deletes a previously saved file. Best-effort: a file that is already gone,
    /// or one the process cannot remove, is not worth failing the request over -
    /// the row that pointed at it is already being overwritten or cleared.
    /// </summary>
    public static void Delete(string? relativePath, IWebHostEnvironment environment)
    {
        if (string.IsNullOrEmpty(relativePath)) return;

        var fullPath = Path.Combine(WebRootOf(environment), relativePath.Replace('/', Path.DirectorySeparatorChar));

        try
        {
            File.Delete(fullPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The root-relative URL a browser can load the photo from, or null when there isn't one.</summary>
    public static string? UrlFor(string? relativePath) =>
        relativePath is null ? null : $"/{relativePath}";

    // Recognises a file by the bytes it actually starts with rather than by
    // anything the caller claimed. JPEG, PNG and WebP each begin with a fixed
    // signature; anything else - including a renamed .exe or an SVG, which can
    // carry script - is refused rather than guessed at.
    private static async Task<string?> SniffImageExtensionAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[12];
        var read = await ReadFullyAsync(stream, header, cancellationToken);

        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ".jpg";
        }

        if (read >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return ".png";
        }

        if (read >= 12
            && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
            && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
        {
            return ".webp";
        }

        return null;
    }

    // Stream.ReadAsync is free to return fewer bytes than asked for even when
    // more are available - only the end of the stream guarantees a short read.
    // A signature check that stopped at the first partial read could misjudge a
    // genuine image as unrecognised.
    private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0) break;
            total += read;
        }

        return total;
    }
}
