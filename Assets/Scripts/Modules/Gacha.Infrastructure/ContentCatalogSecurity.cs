using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Gacha.Application;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gacha.Infrastructure.Content
{
    public interface IContentCatalogSignatureVerifier
    {
        bool Verify(ContentCatalogSignature signature, byte[] canonicalPayload, out string errorMessage);
    }

    public interface IContentCatalogSigner : IContentCatalogSignatureVerifier
    {
        string Algorithm { get; }
        string KeyId { get; }
        string Sign(byte[] canonicalPayload);
    }

    public sealed class ContentCatalogCompatibilityPolicy
    {
        private readonly IContentCatalogSignatureVerifier signatureVerifier;

        public ContentCatalogCompatibilityPolicy(
            string currentAppVersion,
            int supportedContentSchemaVersion,
            int supportedRuleSchemaVersion,
            IContentCatalogSignatureVerifier signatureVerifier)
        {
            if (!SemanticVersion.TryParse(currentAppVersion, out SemanticVersion parsedVersion))
                throw new ArgumentException(
                    "Current app version must be a semantic version.", nameof(currentAppVersion));
            if (supportedContentSchemaVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(supportedContentSchemaVersion));
            if (supportedRuleSchemaVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(supportedRuleSchemaVersion));
            CurrentAppVersion = currentAppVersion.Trim();
            ParsedCurrentAppVersion = parsedVersion;
            SupportedContentSchemaVersion = supportedContentSchemaVersion;
            SupportedRuleSchemaVersion = supportedRuleSchemaVersion;
            this.signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
        }

        public string CurrentAppVersion { get; }
        public int SupportedContentSchemaVersion { get; }
        public int SupportedRuleSchemaVersion { get; }
        private SemanticVersion ParsedCurrentAppVersion { get; }

        public string Validate(
            string minimumAppVersion,
            int contentSchemaVersion,
            int ruleSchemaVersion,
            ContentCatalogSignature signature,
            byte[] canonicalPayload)
        {
            if (minimumAppVersion == null ||
                !string.Equals(minimumAppVersion, minimumAppVersion.Trim(), StringComparison.Ordinal))
                return "Catalog minAppVersion cannot contain surrounding whitespace.";
            if (!SemanticVersion.TryParse(minimumAppVersion, out SemanticVersion minimum))
                return "Catalog minAppVersion must be a semantic version.";
            if (ParsedCurrentAppVersion.CompareTo(minimum) < 0)
                return $"Catalog requires app {minimumAppVersion} or later; current app is {CurrentAppVersion}.";
            if (contentSchemaVersion != SupportedContentSchemaVersion)
                return $"Catalog content schema {contentSchemaVersion} is incompatible; app supports {SupportedContentSchemaVersion}.";
            if (ruleSchemaVersion != SupportedRuleSchemaVersion)
                return $"Catalog rule schema {ruleSchemaVersion} is incompatible; app supports {SupportedRuleSchemaVersion}.";
            if (signature == null)
                return "Catalog signature is missing.";
            if (canonicalPayload == null || canonicalPayload.Length == 0)
                return "Catalog canonical payload is empty.";
            return signatureVerifier.Verify(signature, canonicalPayload, out string error)
                ? null
                : error ?? "Catalog signature is invalid.";
        }
    }

    public sealed class RsaContentCatalogSignatureVerifier : IContentCatalogSignatureVerifier
    {
        public const string SupportedAlgorithm = "RS256";
        private readonly IReadOnlyDictionary<string, byte[]> trustedKeys;

        public RsaContentCatalogSignatureVerifier(IReadOnlyDictionary<string, string> subjectPublicKeys)
        {
            if (subjectPublicKeys == null)
                throw new ArgumentNullException(nameof(subjectPublicKeys));
            var copy = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in subjectPublicKeys)
            {
                string keyId = RequireKeyId(pair.Key);
                if (string.IsNullOrWhiteSpace(pair.Value))
                    throw new ArgumentException($"Trusted catalog key '{keyId}' has no public key.", nameof(subjectPublicKeys));
                byte[] keyBytes;
                try
                {
                    string encodedKey = pair.Value.Trim();
                    keyBytes = Convert.FromBase64String(encodedKey);
                    if (!string.Equals(
                            Convert.ToBase64String(keyBytes),
                            encodedKey,
                            StringComparison.Ordinal))
                        throw new FormatException();
                }
                catch (FormatException exception)
                {
                    throw new ArgumentException(
                        $"Trusted catalog key '{keyId}' is not Base64 SubjectPublicKeyInfo.",
                        nameof(subjectPublicKeys),
                        exception);
                }
                if (!copy.TryAdd(keyId, keyBytes))
                    throw new ArgumentException($"Trusted catalog key id repeats: {keyId}", nameof(subjectPublicKeys));
            }
            trustedKeys = copy;
        }

        public bool Verify(
            ContentCatalogSignature signature,
            byte[] canonicalPayload,
            out string errorMessage)
        {
            errorMessage = null;
            if (signature == null)
            {
                errorMessage = "Catalog signature is missing.";
                return false;
            }
            if (!string.Equals(signature.Algorithm, SupportedAlgorithm, StringComparison.Ordinal))
            {
                errorMessage = $"Catalog signature algorithm '{signature.Algorithm}' is not supported.";
                return false;
            }
            string keyId;
            try
            {
                keyId = RequireKeyId(signature.KeyId);
            }
            catch (ArgumentException exception)
            {
                errorMessage = exception.Message;
                return false;
            }
            if (!trustedKeys.TryGetValue(keyId, out byte[] publicKey))
            {
                errorMessage = $"Catalog signature key '{keyId}' is not trusted by this app.";
                return false;
            }

            byte[] signatureBytes;
            try
            {
                signatureBytes = Convert.FromBase64String(signature.Value);
                if (!string.Equals(
                        Convert.ToBase64String(signatureBytes),
                        signature.Value,
                        StringComparison.Ordinal))
                    throw new FormatException();
            }
            catch (FormatException)
            {
                errorMessage = "Catalog signature value is not Base64.";
                return false;
            }

            try
            {
                using (RSA rsa = RSA.Create())
                {
                    rsa.ImportParameters(RsaSubjectPublicKeyInfo.Decode(publicKey));
                    if (!rsa.VerifyData(
                            canonicalPayload,
                            signatureBytes,
                            HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pkcs1))
                    {
                        errorMessage = "Catalog RS256 signature verification failed.";
                        return false;
                    }
                }
                return true;
            }
            catch (CryptographicException exception)
            {
                errorMessage = "Catalog public key or signature is invalid: " + exception.Message;
                return false;
            }
            catch (PlatformNotSupportedException exception)
            {
                errorMessage = "Catalog RS256 verification is unavailable on this runtime: " + exception.Message;
                return false;
            }
        }

        private static string RequireKeyId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
                throw new ArgumentException("Catalog signature keyId must contain 1-64 characters.", nameof(value));
            string trimmed = value.Trim();
            if (!string.Equals(value, trimmed, StringComparison.Ordinal))
                throw new ArgumentException("Catalog signature keyId cannot contain surrounding whitespace.", nameof(value));
            foreach (char character in trimmed)
            {
                bool asciiLetter = character >= 'A' && character <= 'Z' ||
                                   character >= 'a' && character <= 'z';
                bool allowed = asciiLetter || character >= '0' && character <= '9' || character == '.' ||
                               character == '_' || character == '-';
                if (!allowed)
                    throw new ArgumentException("Catalog signature keyId contains an invalid character.", nameof(value));
            }
            return trimmed;
        }
    }

    /// <summary>
    /// Strict DER codec for rsaEncryption SubjectPublicKeyInfo. Unity's Mono and
    /// some IL2CPP profiles do not expose Import/ExportSubjectPublicKeyInfo, so
    /// the portable contract is decoded to RSAParameters before platform crypto.
    /// </summary>
    public static class RsaSubjectPublicKeyInfo
    {
        private static readonly byte[] RsaEncryptionOid =
            { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01 };

        public static RSAParameters Decode(byte[] subjectPublicKeyInfo)
        {
            if (subjectPublicKeyInfo == null)
                throw new ArgumentNullException(nameof(subjectPublicKeyInfo));
            var root = new DerReader(subjectPublicKeyInfo);
            DerReader spki = root.ReadConstructed(0x30);
            root.RequireEnd();
            DerReader algorithm = spki.ReadConstructed(0x30);
            algorithm.RequireBytes(0x06, RsaEncryptionOid);
            algorithm.RequireBytes(0x05, Array.Empty<byte>());
            algorithm.RequireEnd();
            DerReader keyBits = spki.ReadBitString();
            spki.RequireEnd();
            DerReader rsaKey = keyBits.ReadConstructed(0x30);
            keyBits.RequireEnd();
            byte[] modulus = rsaKey.ReadUnsignedInteger();
            byte[] exponent = rsaKey.ReadUnsignedInteger();
            rsaKey.RequireEnd();
            if (modulus.Length < 256 || modulus.Length > 512)
                throw new CryptographicException("Catalog RSA modulus must be 2048-4096 bits.");
            if (exponent.Length == 0 || exponent.Length > 4)
                throw new CryptographicException("Catalog RSA public exponent is invalid.");
            uint exponentValue = 0;
            foreach (byte item in exponent)
                exponentValue = checked((exponentValue << 8) | item);
            if (exponentValue < 3 || (exponentValue & 1) == 0)
                throw new CryptographicException("Catalog RSA public exponent is invalid.");
            return new RSAParameters { Modulus = modulus, Exponent = exponent };
        }

        public static byte[] Encode(RSAParameters parameters)
        {
            if (parameters.Modulus == null || parameters.Exponent == null)
                throw new ArgumentException("RSA public parameters are incomplete.", nameof(parameters));
            byte[] rsaKey = Constructed(
                0x30,
                Integer(parameters.Modulus),
                Integer(parameters.Exponent));
            byte[] algorithm = Constructed(
                0x30,
                Primitive(0x06, RsaEncryptionOid),
                Primitive(0x05, Array.Empty<byte>()));
            byte[] bitString = Primitive(0x03, Join(new byte[] { 0 }, rsaKey));
            return Constructed(0x30, algorithm, bitString);
        }

        private static byte[] Integer(byte[] value)
        {
            if (value == null || value.Length == 0)
                throw new ArgumentException("DER integer cannot be empty.", nameof(value));
            int first = 0;
            while (first < value.Length - 1 && value[first] == 0)
                first++;
            byte[] normalized = value.Skip(first).ToArray();
            if ((normalized[0] & 0x80) != 0)
                normalized = Join(new byte[] { 0 }, normalized);
            return Primitive(0x02, normalized);
        }

        private static byte[] Constructed(byte tag, params byte[][] children) =>
            Primitive(tag, Join(children));

        private static byte[] Primitive(byte tag, byte[] content)
        {
            byte[] length = EncodeLength(content.Length);
            return Join(new[] { tag }, length, content);
        }

        private static byte[] EncodeLength(int value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (value < 128)
                return new[] { (byte)value };
            var bytes = new List<byte>();
            int remaining = value;
            while (remaining > 0)
            {
                bytes.Insert(0, (byte)(remaining & 0xff));
                remaining >>= 8;
            }
            bytes.Insert(0, (byte)(0x80 | bytes.Count));
            return bytes.ToArray();
        }

        private static byte[] Join(params byte[][] values)
        {
            int length = values.Sum(value => value?.Length ?? 0);
            var result = new byte[length];
            int offset = 0;
            foreach (byte[] value in values)
            {
                if (value == null)
                    continue;
                Buffer.BlockCopy(value, 0, result, offset, value.Length);
                offset += value.Length;
            }
            return result;
        }

        private sealed class DerReader
        {
            private readonly byte[] bytes;
            private readonly int end;
            private int offset;

            public DerReader(byte[] bytes)
                : this(bytes, 0, bytes?.Length ?? 0)
            {
                if (bytes == null)
                    throw new ArgumentNullException(nameof(bytes));
            }

            private DerReader(byte[] bytes, int offset, int length)
            {
                this.bytes = bytes;
                this.offset = offset;
                end = checked(offset + length);
            }

            public DerReader ReadConstructed(byte tag)
            {
                Segment segment = Read(tag);
                return new DerReader(bytes, segment.Offset, segment.Length);
            }

            public DerReader ReadBitString()
            {
                Segment segment = Read(0x03);
                if (segment.Length < 1 || bytes[segment.Offset] != 0)
                    throw new CryptographicException("Catalog RSA BIT STRING is invalid.");
                return new DerReader(bytes, segment.Offset + 1, segment.Length - 1);
            }

            public byte[] ReadUnsignedInteger()
            {
                Segment segment = Read(0x02);
                if (segment.Length == 0 || (bytes[segment.Offset] & 0x80) != 0)
                    throw new CryptographicException("Catalog RSA INTEGER is negative or empty.");
                int start = segment.Offset;
                int length = segment.Length;
                if (length > 1 && bytes[start] == 0)
                {
                    if ((bytes[start + 1] & 0x80) == 0)
                        throw new CryptographicException("Catalog RSA INTEGER is not minimally encoded.");
                    start++;
                    length--;
                }
                var result = new byte[length];
                Buffer.BlockCopy(bytes, start, result, 0, length);
                return result;
            }

            public void RequireBytes(byte tag, byte[] expected)
            {
                Segment segment = Read(tag);
                if (segment.Length != expected.Length)
                    throw new CryptographicException("Catalog RSA algorithm identifier is invalid.");
                for (int index = 0; index < expected.Length; index++)
                {
                    if (bytes[segment.Offset + index] != expected[index])
                        throw new CryptographicException("Catalog RSA algorithm identifier is invalid.");
                }
            }

            public void RequireEnd()
            {
                if (offset != end)
                    throw new CryptographicException("Catalog RSA DER contains trailing data.");
            }

            private Segment Read(byte expectedTag)
            {
                if (offset >= end || bytes[offset++] != expectedTag)
                    throw new CryptographicException("Catalog RSA DER tag is invalid.");
                int length = ReadLength();
                if (length < 0 || offset > end - length)
                    throw new CryptographicException("Catalog RSA DER length is invalid.");
                var segment = new Segment(offset, length);
                offset += length;
                return segment;
            }

            private int ReadLength()
            {
                if (offset >= end)
                    throw new CryptographicException("Catalog RSA DER length is missing.");
                int first = bytes[offset++];
                if ((first & 0x80) == 0)
                    return first;
                int count = first & 0x7f;
                if (count == 0 || count > 4 || offset > end - count || bytes[offset] == 0)
                    throw new CryptographicException("Catalog RSA DER length is invalid.");
                int value = 0;
                for (int index = 0; index < count; index++)
                    value = checked((value << 8) | bytes[offset++]);
                if (value < 128)
                    throw new CryptographicException("Catalog RSA DER length is not minimally encoded.");
                return value;
            }

            private readonly struct Segment
            {
                public Segment(int offset, int length)
                {
                    Offset = offset;
                    Length = length;
                }

                public int Offset { get; }
                public int Length { get; }
            }
        }
    }

    public static class ContentCatalogCanonicalizer
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] Canonicalize(
            int schemaVersion,
            long revision,
            string minimumAppVersion,
            int contentSchemaVersion,
            int ruleSchemaVersion,
            IEnumerable<ContentPackageCatalogEntry> packages)
        {
            if (schemaVersion != ContentPackageCatalog.ProtectedSchemaVersion)
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            if (packages == null)
                throw new ArgumentNullException(nameof(packages));
            var packageArray = new JArray();
            foreach (ContentPackageCatalogEntry entry in packages
                         .OrderBy(value => value.Package.PackageId, StringComparer.Ordinal))
                packageArray.Add(Package(entry));

            var root = new JObject
            {
                ["schemaVersion"] = schemaVersion,
                ["revision"] = revision,
                ["minAppVersion"] = minimumAppVersion,
                ["contentSchemaVersion"] = contentSchemaVersion,
                ["ruleSchemaVersion"] = ruleSchemaVersion,
                ["packages"] = packageArray
            };
            return Utf8.GetBytes(root.ToString(Formatting.None));
        }

        public static string ComputeSha256(byte[] canonicalPayload)
        {
            if (canonicalPayload == null)
                throw new ArgumentNullException(nameof(canonicalPayload));
            using (SHA256 sha = SHA256.Create())
                return BytesToHex(sha.ComputeHash(canonicalPayload));
        }

        private static JObject Package(ContentPackageCatalogEntry entry)
        {
            if (entry == null)
                throw new ArgumentException("Catalog cannot contain an empty package entry.", nameof(entry));
            ContentPackageDescriptor package = entry.Package;
            return new JObject
            {
                ["packageId"] = package.PackageId,
                ["installRelativePath"] = package.InstallRelativePath,
                ["revision"] = package.Revision,
                ["version"] = package.Version,
                ["downloadBytes"] = package.DownloadBytes,
                ["installedBytes"] = package.InstalledBytes,
                ["sha256"] = package.Sha256.ToLowerInvariant(),
                ["archiveUrl"] = entry.CatalogArchiveUrl,
                ["metadata"] = Metadata(entry.Metadata)
            };
        }

        private static JObject Metadata(ContentPackageMetadata metadata)
        {
            var localizedNames = new JObject();
            foreach (KeyValuePair<string, string> pair in metadata.LocalizedNames
                         .OrderBy(value => value.Key, StringComparer.Ordinal))
                localizedNames[pair.Key] = pair.Value;
            return new JObject
            {
                ["kind"] = metadata.Kind,
                ["gameId"] = Value(metadata.GameId),
                ["contentLanguageId"] = Value(metadata.ContentLanguageId),
                ["localizedNames"] = localizedNames,
                ["setId"] = Value(metadata.SetId),
                ["setCode"] = Value(metadata.SetCode),
                ["releaseDate"] = Value(metadata.ReleaseDate?.ToString(
                    "yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ["generationOrder"] = Value(metadata.GenerationOrder),
                ["sortOrdinal"] = Value(metadata.SortOrdinal),
                ["tags"] = new JArray(metadata.Tags.OrderBy(value => value, StringComparer.Ordinal)),
                ["dependencies"] = new JArray(metadata.Dependencies.OrderBy(value => value, StringComparer.Ordinal))
            };
        }

        private static JToken Value(object value) =>
            value == null ? JValue.CreateNull() : JToken.FromObject(value);

        private static string BytesToHex(byte[] value)
        {
            var builder = new StringBuilder(value.Length * 2);
            foreach (byte item in value)
                builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }

    internal sealed class SemanticVersion : IComparable<SemanticVersion>
    {
        private SemanticVersion(int major, int minor, int patch, string[] prerelease)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = prerelease;
        }

        private int Major { get; }
        private int Minor { get; }
        private int Patch { get; }
        private string[] Prerelease { get; }

        public static bool TryParse(string value, out SemanticVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string semantic = value.Trim();
            string[] buildParts = semantic.Split(new[] { '+' }, 2);
            if (buildParts.Length == 2 &&
                (semantic.IndexOf('+') != semantic.LastIndexOf('+') ||
                 buildParts[1].Split('.').Any(item => !Identifier(item, false))))
                return false;
            semantic = buildParts[0];
            string[] releaseParts = semantic.Split(new[] { '-' }, 2);
            string[] core = releaseParts[0].Split('.');
            if (core.Length != 3 || !Number(core[0], out int major) ||
                !Number(core[1], out int minor) || !Number(core[2], out int patch))
                return false;
            string[] prerelease = releaseParts.Length == 1
                ? Array.Empty<string>()
                : releaseParts[1].Split('.');
            if (releaseParts.Length == 2 &&
                (prerelease.Length == 0 || prerelease.Any(item => !Identifier(item, true))))
                return false;
            version = new SemanticVersion(major, minor, patch, prerelease);
            return true;
        }

        public int CompareTo(SemanticVersion other)
        {
            if (other == null)
                return 1;
            int comparison = Major.CompareTo(other.Major);
            if (comparison != 0) return comparison;
            comparison = Minor.CompareTo(other.Minor);
            if (comparison != 0) return comparison;
            comparison = Patch.CompareTo(other.Patch);
            if (comparison != 0) return comparison;
            if (Prerelease.Length == 0) return other.Prerelease.Length == 0 ? 0 : 1;
            if (other.Prerelease.Length == 0) return -1;
            int length = Math.Min(Prerelease.Length, other.Prerelease.Length);
            for (int index = 0; index < length; index++)
            {
                string left = Prerelease[index];
                string right = other.Prerelease[index];
                bool leftNumber = Number(left, out int leftValue);
                bool rightNumber = Number(right, out int rightValue);
                if (leftNumber && rightNumber)
                    comparison = leftValue.CompareTo(rightValue);
                else if (leftNumber != rightNumber)
                    comparison = leftNumber ? -1 : 1;
                else
                    comparison = string.Compare(left, right, StringComparison.Ordinal);
                if (comparison != 0) return comparison;
            }
            return Prerelease.Length.CompareTo(other.Prerelease.Length);
        }

        private static bool Number(string value, out int parsed)
        {
            parsed = 0;
            if (string.IsNullOrEmpty(value) || (value.Length > 1 && value[0] == '0'))
                return false;
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed >= 0;
        }

        private static bool Identifier(string value, bool rejectNumericLeadingZero)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            foreach (char character in value)
            {
                bool asciiLetter = character >= 'A' && character <= 'Z' ||
                                   character >= 'a' && character <= 'z';
                if (!asciiLetter && !(character >= '0' && character <= '9') && character != '-')
                    return false;
            }
            if (rejectNumericLeadingZero && value.All(character => character >= '0' && character <= '9') &&
                value.Length > 1 && value[0] == '0')
                return false;
            return true;
        }
    }
}
