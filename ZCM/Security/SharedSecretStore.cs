// ZCM/Security/SharedSecretStore.cs
using Microsoft.Maui.Storage;
using ZCL.Security;

namespace ZCM.Security;

public sealed class SharedSecretStore : ISharedSecretProvider
{
    private const string KeyName = "zc_tls_secret_v1";
    private string? _cachedSecret;
    private int _loadStarted; // 0/1

    public string? GetSecret()
    {
        if (Interlocked.Exchange(ref _loadStarted, 1) == 0)
        {
            _ = LoadAsync();
        }

        return _cachedSecret;
    }

    public void SetSecret(string secret)
    {
        _cachedSecret = secret;
        _ = SecureStorage.Default.SetAsync(KeyName, secret);
    }

    private async Task LoadAsync()
    {
        try
        {
            _cachedSecret = await SecureStorage.Default.GetAsync(KeyName);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[SECURE STORAGE] Failed to load secret:");
            Console.WriteLine(ex);
            _cachedSecret = null;
        }
    }
}