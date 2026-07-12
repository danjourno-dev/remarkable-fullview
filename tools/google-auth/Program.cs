// Checkpoint 6.5.2 — one-time consent, run once on Dan's own machine (or a forker's).
// Opens the browser, he consents to read-only Calendar access, and this prints a refresh
// token that goes straight into SSM (checkpoint 6.5.1's parameter) — it is never written
// to a file or committed anywhere. One consent covers every calendar on the account, since
// Stage 6.5's puller reads them all with the same credential.
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Util.Store;

Console.Write("Google OAuth client id: ");
var clientId = Console.ReadLine()?.Trim();

Console.Write("Google OAuth client secret: ");
var clientSecret = Console.ReadLine()?.Trim();

if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
{
    Console.Error.WriteLine("Both client id and client secret are required.");
    return 1;
}

Console.WriteLine();
Console.WriteLine("Opening your browser to sign in and consent (read-only Calendar access)...");

var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
    new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
    new[] { CalendarService.Scope.CalendarReadonly },
    "dan",
    CancellationToken.None,
    new NullDataStore());

if (string.IsNullOrEmpty(credential.Token.RefreshToken))
{
    Console.Error.WriteLine(
        "No refresh token came back. If you've consented with this client before, revoke " +
        "access at https://myaccount.google.com/permissions and run this again — Google only " +
        "issues a refresh token on first consent (or when access is re-granted from scratch).");
    return 1;
}

Console.WriteLine();
Console.WriteLine("Refresh token (put this in the SSM parameter from checkpoint 6.5.1 —");
Console.WriteLine("do not commit it anywhere):");
Console.WriteLine();
Console.WriteLine(credential.Token.RefreshToken);

return 0;

// AuthorizeAsync wants somewhere to persist tokens between runs; this tool is a one-shot
// (the refresh token goes to SSM, not a local file), so nothing is actually stored.
sealed class NullDataStore : IDataStore
{
    public Task ClearAsync() => Task.CompletedTask;
    public Task DeleteAsync<T>(string key) => Task.CompletedTask;
    public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
    public Task StoreAsync<T>(string key, T value) => Task.CompletedTask;
}
