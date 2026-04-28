using Azure.Identity;
using Microsoft.Graph;

namespace MailTool;

public static class Auth
{
    // Microsoft Graph PowerShell app ID — publicly known, multi-tenant, covers all delegated scopes.
    // Same client ID used by the PowerShell Microsoft.Graph module.
    private const string ClientId = "14d82eec-204b-4c2f-b7e8-296a70dab67e";
    private const string TenantId = "organizations";

    private static readonly string[] Scopes =
    [
        "Mail.ReadWrite",
        "Mail.Send",
        "MailboxSettings.ReadWrite",
        "Calendars.ReadWrite",
        "Contacts.ReadWrite",
        "Files.ReadWrite.All",
        "User.Read"
    ];

    private static readonly string AuthRecordPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "mailtool",
        "auth-record.json"
    );

    public static async Task<GraphServiceClient> GetClientAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AuthRecordPath)!);

        var cacheOptions = new TokenCachePersistenceOptions
        {
            Name = "mailtool",
            UnsafeAllowUnencryptedStorage = true
        };

        var options = new DeviceCodeCredentialOptions
        {
            ClientId = ClientId,
            TenantId = TenantId,
            TokenCachePersistenceOptions = cacheOptions,
            DeviceCodeCallback = (code, _) =>
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(code.Message);
                Console.Error.WriteLine();
                return Task.CompletedTask;
            }
        };

        if (File.Exists(AuthRecordPath))
        {
            try
            {
                await using var fs = File.OpenRead(AuthRecordPath);
                options.AuthenticationRecord = await AuthenticationRecord.DeserializeAsync(fs, ct);
            }
            catch
            {
                // Corrupt record — fall through to fresh auth.
            }
        }

        var credential = new DeviceCodeCredential(options);

        if (options.AuthenticationRecord is null)
        {
            var record = await credential.AuthenticateAsync(new Azure.Core.TokenRequestContext(Scopes), ct);
            await using (var fs = File.Create(AuthRecordPath))
            {
                await record.SerializeAsync(fs, ct);
            }
            // Rebuild credential so subsequent GetToken calls can locate the cached token via the record.
            options.AuthenticationRecord = record;
            credential = new DeviceCodeCredential(options);
        }

        return new GraphServiceClient(credential, Scopes);
    }

    public static void SignOut()
    {
        if (File.Exists(AuthRecordPath))
        {
            File.Delete(AuthRecordPath);
            Console.Error.WriteLine("Signed out. Run any command to re-authenticate.");
        }
        else
        {
            Console.Error.WriteLine("No active session.");
        }
    }
}
