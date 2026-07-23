using System.Reflection;

namespace ProjectTime.Api.Modules;

internal static class InvoiceBrandingAssets
{
    internal const int LogoPixelWidth = 1019;
    internal const int LogoPixelHeight = 899;

    private const string PngResourceName =
        "ProjectTime.Api.Assets.Branding.USSNavyStacked.png";
    private const string JpegResourceName =
        "ProjectTime.Api.Assets.Branding.USSNavyStacked.jpg";

    internal static byte[] LoadPng() => Load(PngResourceName);

    internal static byte[] LoadJpeg() => Load(JpegResourceName);

    private static byte[] Load(string resourceName)
    {
        var assembly = typeof(InvoiceBrandingAssets).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded invoice branding resource was not found: {resourceName}");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
