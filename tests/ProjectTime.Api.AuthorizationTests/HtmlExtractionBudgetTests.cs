using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using ProjectTime.Api.Ai;

internal static class HtmlExtractionBudgetTests
{
    internal static async Task RunAsync()
    {
        var root=Path.Combine(Path.GetTempPath(),"html-security-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);var checks=0;
        try
        {
            var options=PulseAiDocumentPipelineOptions.FromEnvironment() with {
                UploadRoot=root,MaximumCharacters=2_200_000,MaximumFileBytes=3_000_000,ExtractionPreviewEnabled=true };
            foreach(var legacy in new[]{false,true})
            foreach(var payload in new[]{"<html><body>Ordinary readable document</body></html>",
                "<html>"+new string('<',2_000_000),
                string.Concat(Enumerable.Repeat("<script>",250_000)),
                string.Concat(Enumerable.Repeat("<style>",280_000))})
            {
                var path=Path.Combine(root,legacy?"fixture.doc":"fixture.html");
                await File.WriteAllTextAsync(path,payload);
                var source=new PulseAiAuthorizedDocumentSource(Guid.NewGuid(),null,"","","","sow","sow",
                    Path.GetFileName(path),Path.GetFileName(path),path,"text/html",payload.Length,true,true,"pending",false,null,
                    DateTimeOffset.UtcNow,"test","test","internal",Array.Empty<string>());
                var safety=new PulseAiDocumentSafetyAssessment("allowed",legacy?".doc":".html",legacy?"legacy_doc_html":"html",
                    true,true,true,true,true,false,false,false,true,"test",payload.Length,"",Array.Empty<string>(),Array.Empty<string>());
                var clock=Stopwatch.StartNew();
                try
                {
                    PulseAiDocumentExtractionResult result;
                    if(legacy)result=await PulseAiLegacyBinaryWordExtraction.ExtractAsync(source,options,safety,default);
                    else
                    {
                        var method=typeof(PulseAiPrivateDocumentExtractionService).GetMethod("ExtractHtmlAsync",BindingFlags.Static|BindingFlags.NonPublic)!;
                        result=await (Task<PulseAiDocumentExtractionResult>)method.Invoke(null,new object[]{source,options,safety,CancellationToken.None})!;
                    }
                    if(payload.Length<100 && result.Sections.Count==0)throw new Exception("Ordinary HTML must remain extractable");
                }
                catch(RegexMatchTimeoutException) when(payload.Length>100) { }
                if(clock.Elapsed>TimeSpan.FromSeconds(5))throw new Exception("HTML parser exceeded bounded adversarial execution budget");
                checks++;
            }
        }
        finally {Directory.Delete(root,true);}
        Console.WriteLine($"SECURITY_HTML_BUDGET=PASS assertions={checks}");
    }
}
