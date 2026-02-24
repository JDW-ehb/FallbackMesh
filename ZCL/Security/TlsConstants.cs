using System;

namespace ZCL.Security
{
    internal static class TlsConstants
    {
        // =========================
        // PHASE 1: HARD-CODED SECRET
        // =========================
        // Later: load from SecureStorage / user input / pairing QR / etc.
        public const string SharedSecret = "ZC_DEV_SECRET_CHANGE_ME";

        // Custom extension OID to store our membership proof tag.
        // Pick a private enterprise OID space. This one is arbitrary for dev.
        public const string MembershipTagOid = "1.3.6.1.4.1.55555.1.1";

        // Extension "payload" prefix (helps versioning / parsing)
        public const string MembershipTagPrefix = "ZC-TAG:v1:";

        // Subject CN prefix (fallback if extension isn't found)
        public const string SubjectCnPrefix = "ZC Peer";

        // File names for cert persistence
        public const string DefaultPfxFileName = "zc_tls_identity.pfx";

        // PFX password for local persistence (PHASE 1).
        // Later: store password in SecureStorage (MAUI) or env var on server.
        public const string DefaultPfxPassword = "zc_dev_pfx_password_change_me";
    }
}