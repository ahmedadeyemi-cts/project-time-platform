namespace ProjectTime.Api.Modules;

internal static class SafeDocumentMedia
{
    internal static bool IsAllowed(string name) => Path.GetExtension(name).ToLowerInvariant()
        is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".csv";

    // Downloads must never turn stored or client-declared media into active content.
    internal const string DownloadContentType = "application/octet-stream";
}
