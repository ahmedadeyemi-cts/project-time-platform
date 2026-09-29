using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SECURITY_TEST_DB"));
if(cs.Host!="127.0.0.1" || cs.Database!="security_ci") throw new Exception("Isolated fixture database required");
cs.Pooling=false;
var password=cs.Password!;
var newKey=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
var tool=Path.GetFullPath(args.Single());
var checks=0;
await Sql("CREATE TABLE microsoft_integration_client_secrets(tenant_key text PRIMARY KEY,ciphertext bytea,nonce bytea,authentication_tag bytea,fingerprint_sha256 text,encryption_key_source text); CREATE TABLE microsoft_integration_sso_client_secrets(environment_mode text PRIMARY KEY,tenant_key text,ciphertext bytea,nonce bytea,authentication_tag bytea,fingerprint_sha256 text,encryption_key_source text)");
try
{
    foreach(var sso in new[]{false,true})
    {
        var plaintext=Encoding.UTF8.GetBytes("SECURITY_TEST_SENTINEL");
        var key=SHA256.HashData(Encoding.UTF8.GetBytes((sso ? "ProjectPulse-Microsoft-SSO:" : "ProjectPulse-Microsoft-Integration:")+password));
        var cipher=new byte[plaintext.Length];var nonce=RandomNumberGenerator.GetBytes(12);var tag=new byte[16];
        using(var aes=new AesGcm(key,16)) aes.Encrypt(nonce,plaintext,cipher,tag,Encoding.UTF8.GetBytes(sso ? "ProjectPulse:065:SSO:test:fixture" : "ProjectPulse:065:fixture"));
        await using var c=new NpgsqlConnection(cs.ConnectionString);await c.OpenAsync();
        var table=sso ? "microsoft_integration_sso_client_secrets" : "microsoft_integration_client_secrets";
        await using var q=new NpgsqlCommand($"INSERT INTO {table} VALUES ({(sso ? "'test'," : "")}'fixture',@cipher,@nonce,@tag,@fingerprint,'database_credential_derived_key')",c);
        q.Parameters.AddWithValue("cipher",cipher);q.Parameters.AddWithValue("nonce",nonce);q.Parameters.AddWithValue("tag",tag);
        q.Parameters.AddWithValue("fingerprint",Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant());await q.ExecuteNonQueryAsync();
    }
    await Run("verify",true);
    await Run("rotate",false,maintenance:false);
    await Run("rotate",false,key:"invalid-key");
    await Sql("UPDATE microsoft_integration_sso_client_secrets SET fingerprint_sha256='tampered'");
    await Run("rotate",false);
    await Run("verify",false);
    // First-store updates must roll back when the second store fails.
    await using(var c=new NpgsqlConnection(cs.ConnectionString))
    {
        await c.OpenAsync();await using var q=new NpgsqlCommand("SELECT encryption_key_source FROM microsoft_integration_client_secrets",c);
        if((string?)await q.ExecuteScalarAsync()!="database_credential_derived_key") throw new Exception("Partial rotation committed");checks++;
    }
    var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("SECURITY_TEST_SENTINEL"))).ToLowerInvariant();
    await Sql($"UPDATE microsoft_integration_sso_client_secrets SET fingerprint_sha256='{fingerprint}'");
    await Run("rotate",true);
    await Run("verify",false);
    await Run("verify",true,legacy:newKey);
    await Run("restore-legacy",false,maintenance:false);
    await Run("restore-legacy",true);
    await Run("verify",true);
    await Run("verify",false,legacy:newKey);
    await Run("rotate",true);
    await Run("verify",true,legacy:newKey);
    Console.WriteLine($"SECURITY_CREDENTIAL_TRANSITION=PASS assertions={checks}");
}
finally { await Sql("DROP TABLE microsoft_integration_sso_client_secrets,microsoft_integration_client_secrets"); }

async Task Sql(string sql)
{
    await using var c=new NpgsqlConnection(cs.ConnectionString);await c.OpenAsync();await using var q=new NpgsqlCommand(sql,c);await q.ExecuteNonQueryAsync();
}
async Task Run(string mode,bool pass,bool maintenance=true,string? key=null,string? legacy=null)
{
    var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { RedirectStandardOutput=true,RedirectStandardError=true };
    start.ArgumentList.Add(tool);start.ArgumentList.Add(mode);
    foreach(var entry in new Dictionary<string,string>{{"PGHOST",cs.Host!},{"PGPORT",cs.Port.ToString()},{"PGDATABASE",cs.Database!},{"PGUSER",cs.Username!},{"PGPASSWORD",password},{"NEW_MICROSOFT_KEY",key??newKey},{"LEGACY_MICROSOFT_KEY",legacy??""},{"SECURITY_TRANSITION_MAINTENANCE_CONFIRMED",maintenance ? "test-integrations-paused" : ""}}) start.Environment[entry.Key]=entry.Value;
    using var p=Process.Start(start)!;var output=await p.StandardOutput.ReadToEndAsync();var error=await p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();
    if((p.ExitCode==0)!=pass || (output+error).Contains("SECURITY_TEST_SENTINEL") || (output+error).Contains(password) || (output+error).Contains(newKey)) throw new Exception("Credential transition boundary failed: "+mode);
    checks++;
}
