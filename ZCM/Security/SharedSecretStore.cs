using ZCL.Security;

public sealed class SharedSecretStore : ISharedSecretProvider
{
    private const string KeyName = "zc_tls_secret_v1";
    private string? _cachedSecret;
    private bool _loaded;

    public string? GetSecret()
    {
        if (_loaded)
            return _cachedSecret;

        try
        {
            _cachedSecret = SecureStorage.Default
                .GetAsync(KeyName)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[SECURE STORAGE] Failed to load secret:");
            Console.WriteLine(ex);
            _cachedSecret = null;
        }

        _loaded = true;
        return _cachedSecret;
    }

    public void SetSecret(string secret)
    {
        _cachedSecret = secret;
        _loaded = true;
        _ = SecureStorage.Default.SetAsync(KeyName, secret);
    }
}