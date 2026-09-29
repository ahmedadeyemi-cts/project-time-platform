using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using ProjectTime.Api.Modules;

internal static class CanonicalMiddlewareTests
{
    internal static async Task RunAsync()
    {
        var checks=0;
        foreach(var mode in new[]{"import","closeout","sso"})
        {
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions { Args=Array.Empty<string>(),EnvironmentName="Test" });
            await using var app=builder.Build();
            app.UseCanonicalApiPaths();
            if(mode=="import") app.UseMicrosoftIntegrationSecurityCompatibility();
            if(mode=="closeout") app.UseProjectNotificationCloseoutCompatibility();
            if(mode=="sso") app.UseMicrosoftSsoRuntimeCompatibility();
            var reached=false;string? sanitized=null;
            ((IApplicationBuilder)app).Run(async context=>
            {
                reached=true;
                using var document=await JsonDocument.ParseAsync(context.Request.Body);
                sanitized=document.RootElement.GetProperty("authorityUrl").GetString();
            });
            var pipeline=((IApplicationBuilder)app).Build();
            var path=mode switch { "import"=>"/api/microsoft-integration/directory-users/import-selected", "closeout"=>"/api/project-closeout/email/send", _=>"/api/microsoft-integration/sso-test" };
            foreach(var spelling in new[]{path,path+"/",path.ToUpperInvariant()+"/"})
            {
                reached=false;sanitized=null;
                var context=new DefaultHttpContext { RequestServices=app.Services };
                context.Request.Method="POST";context.Request.Path=spelling;context.Request.ContentType="application/json";
                var payload=mode=="sso"
                    ? "{\"tenantId\":\"10000000-0000-0000-0000-000000000001\",\"clientId\":\"20000000-0000-0000-0000-000000000001\",\"redirectUri\":\"https://phd-west-test.onenecklab.com/api/auth/sso/callback\",\"authorityUrl\":\"http://169.254.169.254/latest/meta-data/\"}"
                    : "{}";
                var body=Encoding.UTF8.GetBytes(payload);context.Request.Body=new MemoryStream(body);context.Request.ContentLength=body.Length;context.Response.Body=new MemoryStream();
                await pipeline(context);
                var passed=mode=="sso" ? reached && sanitized?.StartsWith("https://login.microsoftonline.com/10000000-0000-0000-0000-000000000001",StringComparison.Ordinal)==true
                    : !reached && context.Response.StatusCode==401;
                if(!passed) throw new Exception($"Canonical {mode} middleware failed for {spelling}: {context.Response.StatusCode}");
                checks++;
            }
        }
        Console.WriteLine($"SECURITY_CANONICAL_MIDDLEWARE=PASS assertions={checks}");
    }
}
