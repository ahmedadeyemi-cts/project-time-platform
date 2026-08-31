namespace ProjectTime.Api.Modules;

// Keeps Module 025 DOCX serialization explicitly bound to System.Xml.Linq
// even though ClosedXML also exposes a SaveOptions type in this namespace.
internal static class SaveOptions
{
    internal const System.Xml.Linq.SaveOptions DisableFormatting = System.Xml.Linq.SaveOptions.DisableFormatting;
}
