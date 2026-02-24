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
        /// <summary>
        /// Load or create a persistent self-signed identity certificate.
        /// The certificate includes a custom extension proving knowledge of the shared secret.
        /// </summary>
        public static X509Certificate2 LoadOrCreateIdentityCertificate(
            string baseDirectory,
            string? peerLabel = null,
            string? pfxPassword = null,
            string? pfxFileName = null)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
                throw new ArgumentException("baseDirectory is required.", nameof(baseDirectory));

            Directory.CreateDirectory(baseDirectory);

            pfxFileName ??= TlsConstants.DefaultPfxFileName;
            pfxPassword ??= TlsConstants.DefaultPfxPassword;

            var pfxPath = Path.Combine(baseDirectory, pfxFileName);

            if (File.Exists(pfxPath))
            {
                var loaded = new X509Certificate2(
                    pfxPath,
                    pfxPassword,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

                // Ensure private key is present (needed for AuthenticateAsServer)
                if (!loaded.HasPrivateKey)
                    throw new InvalidOperationException("Loaded TLS identity cert has no private key.");

                return loaded;
            }

            var created = CreateSelfSignedIdentityCertificate(peerLabel);
            SavePfx(created, pfxPath, pfxPassword);
            return created;
        }

        /// <summary>
        /// Creates a self-signed RSA certificate containing a membership tag extension.
        /// </summary>
        public static X509Certificate2 CreateSelfSignedIdentityCertificate(string? peerLabel = null)
        {
            peerLabel ??= Environment.MachineName;

            using var rsa = RSA.Create(3072);

            // CN is informational only; trust is enforced via custom extension.
            var subject = new X500DistinguishedName($"CN={TlsConstants.SubjectCnPrefix} - {EscapeCn(peerLabel)}");

            var req = new CertificateRequest(
                subject,
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // Basic constraints: not a CA
            req.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, true));

            // Key usage
            req.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    true));

            // EKU: serverAuth + clientAuth (mTLS)
            var eku = new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1"), // serverAuth
                new Oid("1.3.6.1.5.5.7.3.2"), // clientAuth
            };
            req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, true));

            // Subject Key Identifier
            req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

            // Our membership tag extension
            var tagHex = ComputeMembershipTagHex(req.PublicKey);
            var payload = $"{TlsConstants.MembershipTagPrefix}{tagHex}";
            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            // Non-critical so it doesn't break other tooling.
            var membershipExt = new X509Extension(
                new Oid(TlsConstants.MembershipTagOid),
                payloadBytes,
                critical: false);

            req.CertificateExtensions.Add(membershipExt);

            // Validity window
            var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            var notAfter = DateTimeOffset.UtcNow.AddYears(5);

            // Create cert
            var cert = req.CreateSelfSigned(notBefore, notAfter);

            // IMPORTANT: re-import as exportable with private key accessible
            // (some platforms behave better after this step)
            return new X509Certificate2(
                cert.Export(X509ContentType.Pfx, TlsConstants.DefaultPfxPassword),
                TlsConstants.DefaultPfxPassword,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }

        /// <summary>
        /// Computes the membership tag as HMAC(secret, publicKeyBytes) and returns hex.
        /// </summary>
        public static string ComputeMembershipTagHex(PublicKey publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));

            // More stable than EncodedKeyValue alone:
            // include algorithm parameters + key value bytes.
            var alg = publicKey.EncodedParameters?.RawData ?? Array.Empty<byte>();
            var key = publicKey.EncodedKeyValue?.RawData ?? Array.Empty<byte>();

            var material = new byte[alg.Length + key.Length];
            Buffer.BlockCopy(alg, 0, material, 0, alg.Length);
            Buffer.BlockCopy(key, 0, material, alg.Length, key.Length);

            var secretBytes = Encoding.UTF8.GetBytes(TlsConstants.SharedSecret);
            using var hmac = new HMACSHA256(secretBytes);

            var tag = hmac.ComputeHash(material);
            return Convert.ToHexString(tag);
        }

        /// <summary>
        /// Persist to disk as PFX.
        /// </summary>
        public static void SavePfx(X509Certificate2 cert, string pfxPath, string password)
        {
            if (cert == null) throw new ArgumentNullException(nameof(cert));
            if (string.IsNullOrWhiteSpace(pfxPath)) throw new ArgumentException("pfxPath required.", nameof(pfxPath));

            var bytes = cert.Export(X509ContentType.Pfx, password);
            File.WriteAllBytes(pfxPath, bytes);
        }

        private static string EscapeCn(string s)
        {
            // Minimal escaping for commas etc. (CN parsing is not critical anyway)
            return s.Replace(",", "_").Replace(";", "_").Replace("\n", "_").Replace("\r", "_");
        }
    }
}