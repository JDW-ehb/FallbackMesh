using Microsoft.Maui.Storage;
using ZCL.Security;

namespace ZCM.Security;

public sealed class SharedSecretStore : ISharedSecretProvider
{
    private const string KeyName = "zc_tls_secret_v1";

    public string? GetSecret()
        => SecureStorage.Default.GetAsync(KeyName).GetAwaiter().GetResult();

    public void SetSecret(string secret)
        => SecureStorage.Default.SetAsync(KeyName, secret).GetAwaiter().GetResult();
}