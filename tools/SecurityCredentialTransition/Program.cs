using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

// Private-network, operator-controlled transition. No plaintext, row identity,
// connection string or exception message is emitted. Default is read-only.
try
{
    var mode = args.SingleOrDefault() ?? "verify";
    if (mode is not ("verify" or "rotate" or "restore-legacy")) throw new InvalidOperationException();
    var host = Required("PGHOST");
    var database = Required("PGDATABASE");
    var isolatedFixture = host == "127.0.0.1" && database == "security_ci";
    if (!isolatedFixture && (host != "pg-phd-test-w3-7825cc.postgres.database.azure.com" || database != "project_health_dashboard"))
        throw new InvalidOperationException();
    if (mode != "verify" && Required("SECURITY_TRANSITION_MAINTENANCE_CONFIRMED") != "test-integrations-paused")
        throw new InvalidOperationException();
    var password = Required("PGPASSWORD");
    var configured = Environment.GetEnvironmentVariable("LEGACY_MICROSOFT_KEY");
    var seed = string.IsNullOrWhiteSpace(configured) ? password : configured;
    var oldMicrosoft = LegacyKey(seed, "ProjectPulse-Microsoft-Integration:");
    var oldSso = LegacyKey(seed, "ProjectPulse-Microsoft-SSO:");
    var newKey = mode != "verify" ? Convert.FromBase64String(Required("NEW_MICROSOFT_KEY")) : null;
    if (newKey is not null && (newKey.Length != 32 || newKey.SequenceEqual(oldMicrosoft) || newKey.SequenceEqual(oldSso)))
        throw new CryptographicException();
    try
    {
        var cs = new NpgsqlConnectionStringBuilder { Host=host, Database=database,
            Username=Required("PGUSER"), Password=password, Port=int.Parse(Environment.GetEnvironmentVariable("PGPORT") ?? "5432"),
            SslMode=isolatedFixture ? SslMode.Disable : SslMode.VerifyFull,
            GssEncryptionMode=GssEncryptionMode.Disable,
            Timeout=15, CommandTimeout=30, IncludeErrorDetail=false, Pooling=false };
        await using var connection = new NpgsqlConnection(cs.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var setup = new NpgsqlCommand(mode == "verify"
            ? "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='30s';"
            : "SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'; LOCK TABLE microsoft_integration_client_secrets,microsoft_integration_sso_client_secrets IN ACCESS EXCLUSIVE MODE;", connection, transaction))
            await setup.ExecuteNonQueryAsync();
        var totals = new Dictionary<string,int>();
        foreach (var sso in new[]{false,true})
        {
            var table = sso ? "microsoft_integration_sso_client_secrets" : "microsoft_integration_client_secrets";
            var columns = sso ? "environment_mode,tenant_key" : "tenant_key,tenant_key";
            var entries = new List<(string Id,string Tenant,byte[] Cipher,byte[] Nonce,byte[] Tag,string Fingerprint,string Source)>();
            await using (var read = new NpgsqlCommand($"SELECT {columns},ciphertext,nonce,authentication_tag,fingerprint_sha256,encryption_key_source FROM {table}", connection, transaction))
            await using (var reader = await read.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    entries.Add((reader.GetString(0),reader.GetString(1),(byte[])reader[2],(byte[])reader[3],(byte[])reader[4],reader.GetString(5),reader.GetString(6)));
            if (entries.Count > 100) throw new InvalidOperationException();
            foreach (var entry in entries)
            {
                if (entry.Source is not ("database_credential_derived_key" or "dedicated_environment_key"))
                    throw new InvalidOperationException();
                var aad = Encoding.UTF8.GetBytes(sso ? $"ProjectPulse:065:SSO:{entry.Id}:{entry.Tenant}" : $"ProjectPulse:065:{entry.Tenant}");
                var plaintext = new byte[entry.Cipher.Length];
                try
                {
                    var legacyKey=sso ? oldSso : oldMicrosoft;
                    var readKey=mode=="restore-legacy" ? newKey! : legacyKey;
                    if(mode=="restore-legacy" && entry.Source!="dedicated_environment_key")throw new InvalidOperationException();
                    using (var aes = new AesGcm(readKey,16))
                        aes.Decrypt(entry.Nonce,entry.Cipher,entry.Tag,plaintext,aad);
                    var fingerprint = Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant();
                    if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(fingerprint),Encoding.UTF8.GetBytes(entry.Fingerprint)))
                        throw new CryptographicException();
                    if (newKey is null) continue;
                    var writeKey=mode=="restore-legacy" ? legacyKey : newKey;
                    var nonce=RandomNumberGenerator.GetBytes(12); var cipher=new byte[plaintext.Length]; var tag=new byte[16];
                    using (var aes = new AesGcm(writeKey,16)) aes.Encrypt(nonce,plaintext,cipher,tag,aad);
                    var check=new byte[plaintext.Length];
                    try
                    {
                        using (var aes = new AesGcm(writeKey,16)) aes.Decrypt(nonce,cipher,tag,check,aad);
                        if (!CryptographicOperations.FixedTimeEquals(check,plaintext)) throw new CryptographicException();
                    }
                    finally { CryptographicOperations.ZeroMemory(check); }
                    var idColumn=sso ? "environment_mode" : "tenant_key";
                    await using var update=new NpgsqlCommand($"UPDATE {table} SET ciphertext=@cipher,nonce=@nonce,authentication_tag=@tag,encryption_key_source=@source WHERE {idColumn}=@id AND ciphertext=@old",connection,transaction);
                    update.Parameters.AddWithValue("source",mode=="restore-legacy" && string.IsNullOrWhiteSpace(configured) ? "database_credential_derived_key" : "dedicated_environment_key");
                    update.Parameters.AddWithValue("cipher",cipher); update.Parameters.AddWithValue("nonce",nonce); update.Parameters.AddWithValue("tag",tag);
                    update.Parameters.AddWithValue("id",entry.Id); update.Parameters.AddWithValue("old",entry.Cipher);
                    if (await update.ExecuteNonQueryAsync()!=1) throw new InvalidOperationException();
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }
            totals[sso ? "sso" : "microsoft"] = entries.Count;
        }
        await transaction.CommitAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { status="passed", mode, counts=totals, credentialValuesEmitted=false }));
    }
    finally
    {
        CryptographicOperations.ZeroMemory(oldMicrosoft); CryptographicOperations.ZeroMemory(oldSso);
        if(newKey is not null) CryptographicOperations.ZeroMemory(newKey);
    }
}
catch(Exception ex)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { status="failed", errorType=ex.GetType().Name,
        innerType=ex.InnerException?.GetType().Name, sqlState=(ex as PostgresException)?.SqlState, credentialValuesEmitted=false }));
    Environment.ExitCode=1;
}

static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: >0 } value ? value : throw new InvalidOperationException();
static byte[] LegacyKey(string seed,string prefix)
{
    try { var decoded=Convert.FromBase64String(seed); if(decoded.Length==32)return decoded; CryptographicOperations.ZeroMemory(decoded); }
    catch(FormatException) { }
    return SHA256.HashData(Encoding.UTF8.GetBytes(prefix+seed));
}
