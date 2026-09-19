using System.Text;
using ProjectTime.Api.Modules;

var count = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
Check(ConnectWiseSellContract.TryCreateCredential("tenant_azure", "public-test", "private-test", out var secret), "three valid keys accepted");
using var request = new HttpRequestMessage(HttpMethod.Get, ConnectWiseSellContract.HealthUrl);
Check(ConnectWiseSellContract.TryAuthorize(request, secret), "request authenticated");
Check(request.Headers.Authorization?.Scheme == "Basic", "CPQ uses Basic");
Check(Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization!.Parameter!)) == "tenant_azure+public-test:private-test", "access key + public key username and private key password");
Check(request.Content?.Headers.ContentType?.ToString() == "application/json; version=1.0", "version header");
Check(!request.RequestUri!.Query.Contains("test") && !request.RequestUri.UserInfo.Any(), "no keys in URL");
foreach (var uri in new[] { "https://api.getbase.com/v2/contacts", "https://api-na.myconnectwise.net/v4_6_release/apis/3.0/company/companies", "https://evil.example/api/quotes", "http://sellapi.quosalsell.com/api/quotes", "https://sellapi.quosalsell.com.evil.example/api/quotes", "https://sellapi.quosalsell.com:8443/api/quotes", "https://user@sellapi.quosalsell.com/api/quotes", "https://sellapi.quosalsell.com/api/quotes-malicious" })
{
    using var wrong = new HttpRequestMessage(HttpMethod.Get, uri);
    Check(!ConnectWiseSellContract.TryAuthorize(wrong, secret) && wrong.Headers.Authorization is null, "reject credential destination " + uri);
}
foreach (var bad in new[] { "", "bearer-token", "{}", "{\"AccessKey\":\"a\"}" })
{
    using var invalid = new HttpRequestMessage(HttpMethod.Get, ConnectWiseSellContract.HealthUrl);
    Check(!ConnectWiseSellContract.TryAuthorize(invalid, bad), "reject malformed credential");
}
foreach (var bad in new[] { "", "a:b", "a+b", "a\r\nb", "a b" })
    Check(!ConnectWiseSellContract.TryCreateCredential(bad, "public", "private", out _), "reject malformed access key");
Check(!ConnectWiseSellContract.TryCreateCredential("tenant", "public", "", out _), "private key required");
Check(ConnectWiseSellContract.IsQuoteList("[]"), "empty quote list is authenticated success");
Check(ConnectWiseSellContract.IsQuoteList("[{\"id\":\"quote-123\",\"name\":\"Sample\"}]"), "CPQ quote list accepted");
foreach (var body in new[] { "<html>Login</html>", "{\"items\":[{\"data\":{\"id\":123}}]}", "null", "[{}]", "[1]", "[{\"id\":\"\"}]" })
    Check(!ConnectWiseSellContract.IsQuoteList(body), "reject login, Zendesk or malformed response");
Check(ConnectWiseSellContract.IsConfiguration("api_key", ConnectWiseSellContract.BaseUrl,
    ConnectWiseSellContract.HealthUrl, "Authorization", "Basic", ConnectWiseSellContract.LookupUrl, "", ""), "canonical configuration accepted");
foreach (var auth in new[] { "oauth2", "basic", "" })
    Check(!ConnectWiseSellContract.IsConfiguration(auth, ConnectWiseSellContract.BaseUrl,
        ConnectWiseSellContract.HealthUrl, "Authorization", "Basic", null, "", ""), "wrong auth rejected");
foreach (var health in new[] { "https://example.com/api/quotes", ConnectWiseSellContract.BaseUrl, ConnectWiseSellContract.BaseUrl + "/api/quotes/123" })
    Check(!ConnectWiseSellContract.IsConfiguration("api_key", ConnectWiseSellContract.BaseUrl,
        health, "Authorization", "Basic", null, "", ""), "noncanonical health rejected");
Check(!ConnectWiseSellContract.IsConfiguration("api_key", ConnectWiseSellContract.BaseUrl,
    ConnectWiseSellContract.HealthUrl, "Authorization", "Bearer", null, "", ""), "bearer prefix rejected");
Check(!ConnectWiseSellContract.IsConfiguration("api_key", ConnectWiseSellContract.BaseUrl,
    ConnectWiseSellContract.HealthUrl, "Authorization", "Basic", null, "https://example.com/oauth", ""), "OAuth endpoint rejected");
Check(!ConnectWiseSellContract.IsEndpoint(new Uri("/relative", UriKind.Relative)), "relative destination rejected without throwing");
Console.WriteLine($"ConnectWise SELL API: {count} assertions passed; no network calls.");
