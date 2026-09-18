using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Inventria.Tests;

/// <summary>
/// Enough of IWebHostEnvironment for UserProfileController's tests - WebRootPath
/// is the one property AvatarStorage actually reads - without standing up a real
/// ASP.NET Core host. Each instance gets its own throwaway directory under the
/// temp folder so avatar-upload tests can write and read back real files without
/// colliding with each other or leaving anything behind that matters.
/// </summary>
public sealed class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string WebRootPath { get; set; } =
        Path.Combine(Path.GetTempPath(), "inventria-tests", Guid.NewGuid().ToString("N"));

    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ApplicationName { get; set; } = "Inventria.Tests";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public string EnvironmentName { get; set; } = "Test";
}
