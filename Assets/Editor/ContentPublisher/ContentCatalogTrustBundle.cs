using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Gacha.Infrastructure.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gacha.EditorTools.Content
{
    public sealed class ContentCatalogTrustKey
    {
        public ContentCatalogTrustKey(string keyId, string subjectPublicKeyInfoBase64)
        {
            KeyId = keyId;
            SubjectPublicKeyInfoBase64 = subjectPublicKeyInfoBase64;
        }

        public string KeyId { get; }
        public string SubjectPublicKeyInfoBase64 { get; }
    }

    /// <summary>
    /// Public-only input used to validate a protected catalog before upload and
    /// to produce the App-first runtime trust configuration. It deliberately has
    /// no private-key, credential, endpoint, or publishing-token field.
    /// </summary>
    public sealed class ContentCatalogTrustBundle
    {
        public const int SupportedSchemaVersion = 1;
        public const int MaximumTrustedKeys = 16;
        private static readonly HashSet<string> RootFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "schemaVersion",
            "currentAppVersion",
            "contentSchemaVersion",
            "ruleSchemaVersion",
            "trustedCatalogKeys"
        };
        private static readonly HashSet<string> KeyFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "keyId",
            "subjectPublicKeyInfoBase64"
        };

        public ContentCatalogTrustBundle(
            string currentAppVersion,
            int contentSchemaVersion,
            int ruleSchemaVersion,
            IEnumerable<ContentCatalogTrustKey> trustedKeys)
        {
            ContentCatalogTrustKey[] keys = (trustedKeys ?? throw new ArgumentNullException(nameof(trustedKeys)))
                .ToArray();
            if (keys.Length == 0)
                throw new ArgumentException("Catalog trust bundle requires at least one public key.", nameof(trustedKeys));
            if (keys.Length > MaximumTrustedKeys)
                throw new ArgumentException(
                    $"Catalog trust bundle cannot contain more than {MaximumTrustedKeys} public keys.",
                    nameof(trustedKeys));
            if (currentAppVersion == null ||
                !string.Equals(currentAppVersion, currentAppVersion.Trim(), StringComparison.Ordinal))
                throw new ArgumentException(
                    "Catalog trust bundle current app version cannot contain surrounding whitespace.",
                    nameof(currentAppVersion));

            var publicKeys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ContentCatalogTrustKey key in keys)
            {
                if (key == null)
                    throw new ArgumentException("Catalog trust bundle contains an empty key.", nameof(trustedKeys));
                if (key.SubjectPublicKeyInfoBase64 == null ||
                    !string.Equals(
                        key.SubjectPublicKeyInfoBase64,
                        key.SubjectPublicKeyInfoBase64.Trim(),
                        StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"Catalog trust key '{key.KeyId}' public key cannot contain surrounding whitespace.",
                        nameof(trustedKeys));
                if (!publicKeys.TryAdd(key.KeyId, key.SubjectPublicKeyInfoBase64))
                    throw new ArgumentException("Catalog trust bundle contains a duplicate keyId: " + key.KeyId,
                        nameof(trustedKeys));
            }

            var verifier = new RsaContentCatalogSignatureVerifier(publicKeys);
            foreach (KeyValuePair<string, string> key in publicKeys)
            {
                byte[] encoded = Convert.FromBase64String(key.Value);
                try
                {
                    RsaSubjectPublicKeyInfo.Decode(encoded);
                }
                catch (CryptographicException exception)
                {
                    throw new ArgumentException(
                        $"Catalog trust key '{key.Key}' is not a supported RSA SubjectPublicKeyInfo.",
                        nameof(trustedKeys),
                        exception);
                }
            }

            Policy = new ContentCatalogCompatibilityPolicy(
                currentAppVersion,
                contentSchemaVersion,
                ruleSchemaVersion,
                verifier);
            CurrentAppVersion = Policy.CurrentAppVersion;
            ContentSchemaVersion = contentSchemaVersion;
            RuleSchemaVersion = ruleSchemaVersion;
            TrustedKeys = publicKeys
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => new ContentCatalogTrustKey(value.Key, value.Value))
                .ToArray();
            IdentitySha256 = ComputeIdentitySha256();
        }

        public string CurrentAppVersion { get; }
        public int ContentSchemaVersion { get; }
        public int RuleSchemaVersion { get; }
        public IReadOnlyList<ContentCatalogTrustKey> TrustedKeys { get; }
        public string IdentitySha256 { get; }
        internal ContentCatalogCompatibilityPolicy Policy { get; }

        public static ContentCatalogTrustBundle Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Catalog trust bundle path cannot be empty.", nameof(path));
            string fullPath = Path.GetFullPath(path.Trim());
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Catalog trust bundle was not found.", fullPath);
            return Parse(File.ReadAllText(fullPath));
        }

        public static ContentCatalogTrustBundle Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("Catalog trust bundle is empty.");
            JObject root;
            try
            {
                root = JObject.Parse(json, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Catalog trust bundle is not valid JSON.", exception);
            }

            RequireExactFields(root, RootFields, "Catalog trust bundle");
            int schemaVersion = RequireInteger(root, "schemaVersion");
            if (schemaVersion != SupportedSchemaVersion)
                throw new InvalidDataException("Catalog trust bundle schemaVersion is unsupported.");
            string currentAppVersion = RequireString(root, "currentAppVersion");
            int contentSchemaVersion = RequireInteger(root, "contentSchemaVersion");
            int ruleSchemaVersion = RequireInteger(root, "ruleSchemaVersion");
            JArray keyArray = root["trustedCatalogKeys"] as JArray ??
                              throw new InvalidDataException("Catalog trust bundle trustedCatalogKeys must be an array.");
            var keys = new List<ContentCatalogTrustKey>();
            foreach (JToken token in keyArray)
            {
                JObject key = token as JObject ??
                              throw new InvalidDataException("Catalog trust bundle keys must be objects.");
                RequireExactFields(key, KeyFields, "Catalog trust bundle key");
                keys.Add(new ContentCatalogTrustKey(
                    RequireString(key, "keyId"),
                    RequireString(key, "subjectPublicKeyInfoBase64")));
            }

            try
            {
                return new ContentCatalogTrustBundle(
                    currentAppVersion,
                    contentSchemaVersion,
                    ruleSchemaVersion,
                    keys);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Catalog trust bundle is invalid: " + exception.Message, exception);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException("Catalog trust bundle contains invalid Base64 public key data.", exception);
            }
        }

        internal JsonContentPackageCatalogReader CreateCatalogReader()
        {
            return new JsonContentPackageCatalogReader(Policy);
        }

        internal JArray RuntimeTrustedKeys()
        {
            var result = new JArray();
            foreach (ContentCatalogTrustKey key in TrustedKeys)
            {
                result.Add(new JObject
                {
                    ["keyId"] = key.KeyId,
                    ["subjectPublicKeyInfoBase64"] = key.SubjectPublicKeyInfoBase64
                });
            }
            return result;
        }

        private string ComputeIdentitySha256()
        {
            var root = new JObject
            {
                ["schemaVersion"] = SupportedSchemaVersion,
                ["currentAppVersion"] = CurrentAppVersion,
                ["contentSchemaVersion"] = ContentSchemaVersion,
                ["ruleSchemaVersion"] = RuleSchemaVersion,
                ["trustedCatalogKeys"] = RuntimeTrustedKeys()
            };
            byte[] bytes = new UTF8Encoding(false).GetBytes(root.ToString(Formatting.None));
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void RequireExactFields(JObject value, ISet<string> allowed, string label)
        {
            string unknown = value.Properties()
                .Select(property => property.Name)
                .FirstOrDefault(name => !allowed.Contains(name));
            if (unknown != null)
                throw new InvalidDataException($"{label} contains unsupported field '{unknown}'.");
            string missing = allowed.FirstOrDefault(name => value.Property(name, StringComparison.Ordinal) == null);
            if (missing != null)
                throw new InvalidDataException($"{label} is missing required field '{missing}'.");
        }

        private static string RequireString(JObject value, string field)
        {
            JToken token = value[field];
            if (token == null || token.Type != JTokenType.String)
                throw new InvalidDataException($"Catalog trust bundle field '{field}' must be a string.");
            string result = token.Value<string>();
            if (string.IsNullOrWhiteSpace(result) ||
                !string.Equals(result, result.Trim(), StringComparison.Ordinal))
                throw new InvalidDataException($"Catalog trust bundle field '{field}' cannot be empty or padded.");
            return result;
        }

        private static int RequireInteger(JObject value, string field)
        {
            JToken token = value[field];
            if (token == null || token.Type != JTokenType.Integer)
                throw new InvalidDataException($"Catalog trust bundle field '{field}' must be an integer.");
            try
            {
                return token.Value<int>();
            }
            catch (Exception exception) when (exception is OverflowException || exception is FormatException)
            {
                throw new InvalidDataException($"Catalog trust bundle field '{field}' is outside Int32 range.", exception);
            }
        }
    }
}
