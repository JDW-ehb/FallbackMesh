using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ZCL.Security
{
    internal static class TlsCertificateProvider
    {

        public static X509Certificate2 LoadOrCreateIdentityCertificate(
    string baseDirectory,
    string sharedSecret,
    string? peerLabel = null,
    string? pfxPassword = null,
    string? pfxFileName = null)
        {
            Directory.CreateDirectory(baseDirectory);

            pfxPassword ??= TlsConstants.DefaultPfxPassword;

            var secretHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sharedSecret))
            ).Substring(0, 12);

            pfxFileName ??= $"zc_tls_identity_{secretHash}.pfx";

            var pfxPath = Path.Combine(baseDirectory, pfxFileName);

            if (File.Exists(pfxPath))
            {
                var loaded = new X509Certificate2(
                    pfxPath,
                    pfxPassword,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

                if (!loaded.HasPrivateKey)
                    throw new InvalidOperationException("Loaded TLS identity cert has no private key.");

                return loaded;
            }

            var created = CreateSelfSignedIdentityCertificate(sharedSecret, peerLabel);
            SavePfx(created, pfxPath, pfxPassword);
            return created;
        }


        public static X509Certificate2 CreateSelfSignedIdentityCertificate(string sharedSecret, string? peerLabel = null)
        {
            peerLabel ??= Environment.MachineName;

            using var rsa = RSA.Create(3072);

            var subject = new X500DistinguishedName($"CN={TlsConstants.SubjectCnPrefix} - {EscapeCn(peerLabel)}");

            var req = new CertificateRequest(
                subject,
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            req.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, true));

            req.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    true));

            var eku = new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1"), 
                new Oid("1.3.6.1.5.5.7.3.2"), 
            };
            req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, true));

            req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

            var tagHex = ComputeMembershipTagHex(sharedSecret, req.PublicKey);
            var payload = $"{TlsConstants.MembershipTagPrefix}{tagHex}";
            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            var membershipExt = new X509Extension(
                new Oid(TlsConstants.MembershipTagOid),
                payloadBytes,
                critical: false);

            req.CertificateExtensions.Add(membershipExt);

            var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            var notAfter = DateTimeOffset.UtcNow.AddYears(5);

            var cert = req.CreateSelfSigned(notBefore, notAfter);

            return new X509Certificate2(
                cert.Export(X509ContentType.Pfx, TlsConstants.DefaultPfxPassword),
                TlsConstants.DefaultPfxPassword,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }

        public static string ComputeMembershipTagHex(string sharedSecret, PublicKey publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));

            var alg = publicKey.EncodedParameters?.RawData ?? Array.Empty<byte>();
            var key = publicKey.EncodedKeyValue?.RawData ?? Array.Empty<byte>();

            var material = new byte[alg.Length + key.Length];
            Buffer.BlockCopy(alg, 0, material, 0, alg.Length);
            Buffer.BlockCopy(key, 0, material, alg.Length, key.Length);

            var secretBytes = Encoding.UTF8.GetBytes(sharedSecret);
            using var hmac = new HMACSHA256(secretBytes);

            var tag = hmac.ComputeHash(material);
            return Convert.ToHexString(tag);
        }

        public static void SavePfx(X509Certificate2 cert, string pfxPath, string password)
        {
            if (cert == null) throw new ArgumentNullException(nameof(cert));
            if (string.IsNullOrWhiteSpace(pfxPath)) throw new ArgumentException("pfxPath required.", nameof(pfxPath));

            var bytes = cert.Export(X509ContentType.Pfx, password);
            File.WriteAllBytes(pfxPath, bytes);
        }

        private static string EscapeCn(string s)
        {
            return s.Replace(",", "_").Replace(";", "_").Replace("\n", "_").Replace("\r", "_");
        }
    }
}