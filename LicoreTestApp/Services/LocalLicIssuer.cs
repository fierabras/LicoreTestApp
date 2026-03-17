using System.IO;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.OpenSsl;

namespace LicoreTestApp.Services;

/// <summary>
/// Emits a test <c>license.lic</c> signed with the Ed25519 test key (kid="k1").
/// <para>
/// STRICTLY FOR INTEGRATION TESTING — do not use production private keys.
/// </para>
/// <para>
/// Flow: parse .req → build V1 payload → canonicalize (RFC 8785 subset) → sign Ed25519 → wrap in container.
/// The resulting .lic is accepted by <c>lc_install_license</c> and <c>lc_validate_full</c>
/// when the fingerprint, product, and vendor match.
/// </para>
/// </summary>
internal static class LocalLicIssuer
{
    private const string Vendor = "impulso-informatico";
    private const string Family = "desktop-suite";
    private const string Kid    = "k1";

    /// <summary>
    /// Generates a signed .lic JSON string from a .req JSON string.
    /// </summary>
    /// <param name="reqJson">JSON produced by <c>lc_generate_request</c>.</param>
    /// <param name="privateKeyPem">
    ///   PEM content of the Ed25519 test private key (PKCS#8, kid="k1").
    ///   Obtain via <c>LICORE_TEST_SK_PEM_PATH</c>.
    /// </param>
    /// <param name="expirationDate">
    ///   Expiry as <c>YYYY-MM-DD</c>. Defaults to 1 year from today (UTC) when null.
    /// </param>
    /// <returns>Signed .lic container as a JSON string ready to write to disk.</returns>
    public static string Issue(string reqJson, string privateKeyPem, string? expirationDate = null)
    {
        // ── 1. Parse the .req ─────────────────────────────────────────────────
        using var reqDoc = JsonDocument.Parse(reqJson);
        var req = reqDoc.RootElement;

        string productName = req.GetProperty("product").GetProperty("product_name").GetString()!;
        string version     = req.GetProperty("product").GetProperty("version").GetString()!;
        string fingerprint = req.GetProperty("device_binding").GetProperty("fingerprint").GetString()!;

        var ch = req.GetProperty("customer_hint");
        string custName = ch.GetProperty("name").GetString() ?? "";
        string taxId    = ch.GetProperty("tax_id").GetString() ?? "";
        string email    = ch.GetProperty("email").GetString() ?? "";

        // ── 2. Dates ──────────────────────────────────────────────────────────
        string today    = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string expiry   = expirationDate ?? DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd");
        string issuedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // ── 3. Serialize payload ──────────────────────────────────────────────
        // Anonymous-type properties use the exact C# identifier as JSON key (underscores preserved).
        // CanonicalJson.Serialize will sort keys alphabetically before signing,
        // matching the DLL's canonical_json.cpp behavior.
        string payloadJson = JsonSerializer.Serialize(new
        {
            activation = new
            {
                activation_date = issuedAt,
                details         = "",
                id              = 1,
                status          = "ACTIVE",
                type            = "INITIAL",
            },
            customer = new
            {
                email  = email,
                id     = 1,
                name   = custName,
                tax_id = taxId,
            },
            device_binding = new
            {
                binding_type = "fingerprint_sha512",
                fingerprint  = fingerprint,
            },
            family_id = Family,
            issued_at = issuedAt,
            licence = new
            {
                emission_date   = today,
                expiration_date = expiry,
                id              = 1,
                serial_number   = "LC-TEST-001",
                specifications  = "",
                status          = "ACTIVE",
                type            = "STANDARD",
            },
            product = new
            {
                id             = 1,
                product_name   = productName,
                specifications = "",
                version        = version,
            },
            subscription = new
            {
                finish_date = expiry,
                id          = 1,
                start_date  = today,
                status      = "ACTIVE",
                type        = "YEARLY",
            },
            vendor_id = Vendor,
        });

        // ── 4. Canonicalize → sign ────────────────────────────────────────────
        using var payloadDoc = JsonDocument.Parse(payloadJson);
        string canonical     = CanonicalJson.Serialize(payloadDoc.RootElement);
        byte[] message       = Encoding.UTF8.GetBytes(canonical);
        byte[] sig           = SignEd25519(privateKeyPem, message);
        string sigB64        = Convert.ToBase64String(sig); // RFC 4648, no newlines

        // ── 5. Build container ────────────────────────────────────────────────
        // The payload field is the original (non-canonical) JSON — the DLL re-canonicalizes
        // during verification. The signature covers the canonical form computed in step 4.
        string compactPayload = JsonSerializer.Serialize(payloadDoc.RootElement);
        return
            $"{{\"schema_version\":1,\"alg\":\"Ed25519\",\"kid\":\"{Kid}\"," +
            $"\"payload\":{compactPayload}," +
            $"\"signature\":\"{sigB64}\"}}";
    }

    /// <summary>Signs <paramref name="message"/> with an Ed25519 PKCS#8 PEM private key.</summary>
    private static byte[] SignEd25519(string pem, byte[] message)
    {
        var reader  = new PemReader(new StringReader(pem));
        var privKey = (Ed25519PrivateKeyParameters)reader.ReadObject();
        var signer  = new Ed25519Signer();
        signer.Init(forSigning: true, privKey);
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }
}
