using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;

public sealed class GameIdentityConfigurationException : Exception
{
    public GameIdentityConfigurationException(string message) : base(message) { }
    public GameIdentityConfigurationException(string message, Exception innerException)
        : base(message, innerException) { }
}

[Serializable]
public sealed class GameIdentityConfiguration
{
    public const int CurrentSchemaVersion = 1;
    public const string ExpectedNamespace = "universal-gacha-simulator/player-identity";
    public const string ExpectedProjectId = "bb0a14f0-ed17-4861-a308-431876143865";
    public const string ResourcePath = "Data/GameIdentityConfiguration";

    private static readonly HashSet<string> RequiredScopes = new HashSet<string>(StringComparer.Ordinal)
    {
        "openid",
        "email",
        "offline_access"
    };

    public int schemaVersion;
    public bool enabled;
    public string projectId;
    public string linkedProfile;
    public string saveNamespace;
    public List<string> scopes;

    [JsonIgnore] public int SchemaVersion => schemaVersion;
    [JsonIgnore] public bool Enabled => enabled;
    [JsonIgnore] public string ProjectId => projectId;
    [JsonIgnore] public string LinkedProfile => linkedProfile;
    [JsonIgnore] public string SaveNamespace => saveNamespace;
    [JsonIgnore] public IReadOnlyList<string> Scopes =>
        scopes != null ? (IReadOnlyList<string>)scopes : Array.Empty<string>();

    public static GameIdentityConfiguration Load()
    {
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
            throw new GameIdentityConfigurationException("The player identity configuration is missing.");
        return Parse(asset.text);
    }

    public static bool TryLoad(out GameIdentityConfiguration configuration, out string error)
    {
        try
        {
            configuration = Load();
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            configuration = null;
            error = exception.Message;
            return false;
        }
    }

    public static GameIdentityConfiguration Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new GameIdentityConfigurationException("The player identity configuration is empty.");

        GameIdentityConfiguration configuration;
        try
        {
            configuration = JsonConvert.DeserializeObject<GameIdentityConfiguration>(
                json,
                new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
        }
        catch (Exception exception)
        {
            throw new GameIdentityConfigurationException(
                "The player identity configuration is not valid JSON.",
                exception);
        }

        Validate(configuration);
        return configuration;
    }

    private static void Validate(GameIdentityConfiguration configuration)
    {
        if (configuration == null || configuration.schemaVersion != CurrentSchemaVersion)
            throw new GameIdentityConfigurationException("The player identity schema is not supported.");
        if (!string.Equals(configuration.projectId, ExpectedProjectId, StringComparison.Ordinal))
            throw new GameIdentityConfigurationException("The player identity project id does not match this game.");
        if (!string.Equals(configuration.saveNamespace, ExpectedNamespace, StringComparison.Ordinal))
            throw new GameIdentityConfigurationException("The player identity namespace does not match this game.");
        if (string.IsNullOrWhiteSpace(configuration.linkedProfile) ||
            !Regex.IsMatch(configuration.linkedProfile, "^[a-zA-Z0-9_-]{1,30}$"))
        {
            throw new GameIdentityConfigurationException("The linked authentication profile is invalid.");
        }

        string[] configuredScopes = (configuration.scopes ?? new List<string>()).ToArray();
        if (configuredScopes.Length != RequiredScopes.Count ||
            configuredScopes.Any(string.IsNullOrWhiteSpace) ||
            configuredScopes.Distinct(StringComparer.Ordinal).Count() != configuredScopes.Length ||
            !RequiredScopes.SetEquals(configuredScopes))
        {
            throw new GameIdentityConfigurationException(
                "Player identity may request only openid, email, and offline_access scopes.");
        }
    }
}
