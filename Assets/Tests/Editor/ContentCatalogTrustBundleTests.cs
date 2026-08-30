using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Gacha.EditorTools.Content;
using Gacha.Infrastructure.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

public class ContentCatalogTrustBundleTests
{
    private RSACryptoServiceProvider first;
    private RSACryptoServiceProvider second;

    [SetUp]
    public void SetUp()
    {
        first = new RSACryptoServiceProvider(2048);
        second = new RSACryptoServiceProvider(2048);
    }

    [TearDown]
    public void TearDown()
    {
        first.Dispose();
        second.Dispose();
    }

    [Test]
    public void Parse_AcceptsPublicOnlyKeysAndSortsIdentityDeterministically()
    {
        string firstKey = PublicKey(first);
        string secondKey = PublicKey(second);
        ContentCatalogTrustBundle forward = ContentCatalogTrustBundle.Parse(Json(
            Key("z-old", firstKey),
            Key("a-new", secondKey)));
        ContentCatalogTrustBundle reverse = ContentCatalogTrustBundle.Parse(Json(
            Key("a-new", secondKey),
            Key("z-old", firstKey)));

        Assert.That(forward.CurrentAppVersion, Is.EqualTo("0.2.0-rc.1"));
        Assert.That(forward.TrustedKeys.Select(value => value.KeyId),
            Is.EqualTo(new[] { "a-new", "z-old" }));
        Assert.That(forward.IdentitySha256, Is.EqualTo(reverse.IdentitySha256));
        Assert.That(forward.IdentitySha256, Does.Match("^[a-f0-9]{64}$"));
    }

    [Test]
    public void Parse_RejectsUnknownPrivateOrMissingFields()
    {
        JObject root = JObject.Parse(Json(Key("current", PublicKey(first))));
        root["privateKey"] = "forbidden";
        Assert.Throws<InvalidDataException>(() => ContentCatalogTrustBundle.Parse(root.ToString()));

        root.Remove("privateKey");
        root.Remove("ruleSchemaVersion");
        Assert.Throws<InvalidDataException>(() => ContentCatalogTrustBundle.Parse(root.ToString()));

        root["ruleSchemaVersion"] = 1;
        JObject key = (JObject)((JArray)root["trustedCatalogKeys"])[0];
        key["publisherToken"] = "forbidden";
        Assert.Throws<InvalidDataException>(() => ContentCatalogTrustBundle.Parse(root.ToString()));
    }

    [Test]
    public void Parse_RejectsDuplicateKeysAndDuplicateJsonProperties()
    {
        string publicKey = PublicKey(first);
        Assert.Throws<InvalidDataException>(() => ContentCatalogTrustBundle.Parse(Json(
            Key("same", publicKey),
            Key("same", publicKey))));
        Assert.Throws<InvalidDataException>(() => ContentCatalogTrustBundle.Parse(
            "{\"schemaVersion\":1,\"schemaVersion\":1}"));
    }

    [Test]
    public void Parse_RejectsInvalidBase64DerVersionAndSchema()
    {
        Assert.Throws<InvalidDataException>(() => ContentCatalogTrustBundle.Parse(Json(
            Key("bad-base64", "not-base64"))));
        Assert.Throws<InvalidDataException>(() => ContentCatalogTrustBundle.Parse(Json(
            Key("bad-der", Convert.ToBase64String(new byte[256])))));

        JObject invalidVersion = JObject.Parse(Json(Key("current", PublicKey(first))));
        invalidVersion["currentAppVersion"] = "release-1";
        Assert.Throws<InvalidDataException>(() => ContentCatalogTrustBundle.Parse(invalidVersion.ToString()));

        JObject invalidSchema = JObject.Parse(Json(Key("current", PublicKey(first))));
        invalidSchema["contentSchemaVersion"] = 0;
        Assert.Throws<InvalidDataException>(() => ContentCatalogTrustBundle.Parse(invalidSchema.ToString()));
    }

    [Test]
    public void Parse_RejectsEmptyKeySetAndPaddedStrings()
    {
        JObject empty = JObject.Parse(Json(Key("current", PublicKey(first))));
        empty["trustedCatalogKeys"] = new JArray();
        Assert.Throws<InvalidDataException>(() => ContentCatalogTrustBundle.Parse(empty.ToString()));

        JObject padded = JObject.Parse(Json(Key("current", PublicKey(first))));
        ((JObject)((JArray)padded["trustedCatalogKeys"])[0])["keyId"] = " current ";
        Assert.Throws<InvalidDataException>(() => ContentCatalogTrustBundle.Parse(padded.ToString()));
    }

    private static JObject Key(string keyId, string publicKey)
    {
        return new JObject
        {
            ["keyId"] = keyId,
            ["subjectPublicKeyInfoBase64"] = publicKey
        };
    }

    private static string Json(params JObject[] keys)
    {
        return new JObject
        {
            ["schemaVersion"] = ContentCatalogTrustBundle.SupportedSchemaVersion,
            ["currentAppVersion"] = "0.2.0-rc.1",
            ["contentSchemaVersion"] = 1,
            ["ruleSchemaVersion"] = 1,
            ["trustedCatalogKeys"] = new JArray(keys)
        }.ToString(Formatting.None);
    }

    private static string PublicKey(RSA rsa)
    {
        return Convert.ToBase64String(RsaSubjectPublicKeyInfo.Encode(rsa.ExportParameters(false)));
    }
}
