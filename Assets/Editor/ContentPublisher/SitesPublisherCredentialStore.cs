using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Gacha.EditorTools.Content
{
    public sealed class SitesPublisherCredential
    {
        public SitesPublisherCredential(Uri siteBaseUri, string publisherToken)
        {
            SiteBaseUri = siteBaseUri ?? throw new ArgumentNullException(nameof(siteBaseUri));
            PublisherToken = publisherToken ?? throw new ArgumentNullException(nameof(publisherToken));
        }

        public Uri SiteBaseUri { get; }
        public string PublisherToken { get; }
        public string TokenSha256 => ComputeSha256(PublisherToken);

        internal static string ComputeSha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
                return R2ReleasePublisher.ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
    }

    public static class SitesPublisherCredentialStore
    {
        [Serializable]
        private sealed class CredentialDocument
        {
            public int version;
            public string siteBaseUrl;
            public string publisherToken;
        }

        public static SitesPublisherCredential Generate(Uri siteBaseUri)
        {
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                var bytes = new byte[32];
                random.GetBytes(bytes);
                string token = Convert.ToBase64String(bytes)
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
                return new SitesPublisherCredential(siteBaseUri, token);
            }
        }

        public static SitesPublisherCredential GenerateAndSave(string path, Uri siteBaseUri)
        {
            SitesPublisherCredential credential = Generate(siteBaseUri);
            Save(path, credential);
            return credential;
        }

        public static SitesPublisherCredential Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Publisher credential path cannot be empty.", nameof(path));
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Sites publisher credential was not found.", fullPath);

            CredentialDocument document;
            try
            {
                document = JsonUtility.FromJson<CredentialDocument>(File.ReadAllText(fullPath, Encoding.UTF8));
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("Sites publisher credential is not valid JSON.", exception);
            }
            if (document == null || document.version != 1)
                throw new InvalidDataException("Sites publisher credential version is not supported.");
            if (!Uri.TryCreate(document.siteBaseUrl, UriKind.Absolute, out Uri siteBaseUri))
                throw new InvalidDataException("Sites publisher credential has an invalid Site URL.");
            return new SitesPublisherCredential(siteBaseUri, document.publisherToken);
        }

        public static void Save(string path, SitesPublisherCredential credential)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Publisher credential path cannot be empty.", nameof(path));
            if (credential == null)
                throw new ArgumentNullException(nameof(credential));

            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            string temporaryPath = fullPath + ".tmp";
            string backupPath = fullPath + ".backup";
            var document = new CredentialDocument
            {
                version = 1,
                siteBaseUrl = credential.SiteBaseUri.AbsoluteUri,
                publisherToken = credential.PublisherToken
            };
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(document, true) + "\n", new UTF8Encoding(false));
            try
            {
                if (!File.Exists(fullPath))
                {
                    File.Move(temporaryPath, fullPath);
                    return;
                }

                try
                {
                    File.Replace(temporaryPath, fullPath, backupPath);
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(temporaryPath, fullPath, true);
                    File.Delete(temporaryPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }
}
