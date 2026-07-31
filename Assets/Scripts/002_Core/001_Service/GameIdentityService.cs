using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gacha.Application;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using UnityEngine;

public enum GameIdentityBackendOutcome
{
    CurrentPlayerLinked,
    ExistingPlayerSignedIn
}

public enum GameIdentityConnectOutcome
{
    ExternalSetupRequired,
    LinkedCurrentPlayer,
    LinkedCurrentPlayerCloudPending,
    ExistingPlayerReady,
    ConflictPending,
    Failed
}

public enum GameIdentityStatusKind
{
    ExternalSetupRequired,
    Available,
    Connected,
    Busy
}

public sealed class GameIdentityBackendResult
{
    public GameIdentityBackendResult(
        GameIdentityBackendOutcome outcome,
        string activeProfile,
        string playerId,
        string redactedIdentity)
    {
        Outcome = outcome;
        ActiveProfile = activeProfile;
        PlayerId = playerId;
        RedactedIdentity = redactedIdentity;
    }

    public GameIdentityBackendOutcome Outcome { get; }
    public string ActiveProfile { get; }
    public string PlayerId { get; }
    public string RedactedIdentity { get; }
}

public sealed class GameIdentityConnectResult
{
    private GameIdentityConnectResult(
        GameIdentityConnectOutcome outcome,
        string backupPath,
        string redactedIdentity,
        string error)
    {
        Outcome = outcome;
        BackupPath = backupPath;
        RedactedIdentity = redactedIdentity;
        Error = error;
    }

    public GameIdentityConnectOutcome Outcome { get; }
    public string BackupPath { get; }
    public string RedactedIdentity { get; }
    public string Error { get; }
    public bool Succeeded => Outcome != GameIdentityConnectOutcome.ExternalSetupRequired &&
                             Outcome != GameIdentityConnectOutcome.Failed;
    public bool RequiresConflictChoice => Outcome == GameIdentityConnectOutcome.ConflictPending;

    public static GameIdentityConnectResult Complete(
        GameIdentityConnectOutcome outcome,
        string backupPath,
        string redactedIdentity,
        string error = null) =>
        new GameIdentityConnectResult(outcome, backupPath, redactedIdentity, error);

    public static GameIdentityConnectResult SetupRequired(string error) =>
        new GameIdentityConnectResult(GameIdentityConnectOutcome.ExternalSetupRequired, null, null, error);

    public static GameIdentityConnectResult Failure(string backupPath, string error) =>
        new GameIdentityConnectResult(GameIdentityConnectOutcome.Failed, backupPath, null, error);
}

public sealed class GameIdentityStatus
{
    public GameIdentityStatus(GameIdentityStatusKind kind, string redactedIdentity = null)
    {
        Kind = kind;
        RedactedIdentity = redactedIdentity;
    }

    public GameIdentityStatusKind Kind { get; }
    public string RedactedIdentity { get; }
}

public interface IGameIdentityBackend
{
    string CurrentProfile { get; }
    Task<GameIdentityBackendResult> LinkOrSignInAsync(string linkedProfile);
    Task RestoreProfileAsync(string previousProfile);
}

public interface IGameIdentityPlayerTarget
{
    bool HasPendingConflict { get; }
    PlayerRecoveryState Capture();
    string CreateSafetyBackup(PlayerRecoveryState state);
    void Apply(PlayerRecoveryState state);
    Task<CloudInventoryLoadResult> LoadCloudAsync();
    Task<bool> SaveCloudAsync(InventoryData inventory);
    InventoryConflictPreparation PrepareConflict(InventoryData local, InventoryData cloud, bool cloudFound);
}

public interface IGameIdentityProfileStore
{
    string ActiveProfile { get; }
    void SetActiveProfile(string profile);
}

public sealed class GameIdentityLinkCoordinator
{
    private readonly GameIdentityConfiguration configuration;
    private readonly IGameIdentityBackend backend;
    private readonly IGameIdentityPlayerTarget target;
    private readonly IGameIdentityProfileStore profileStore;

    public GameIdentityLinkCoordinator(
        GameIdentityConfiguration configuration,
        IGameIdentityBackend backend,
        IGameIdentityPlayerTarget target,
        IGameIdentityProfileStore profileStore)
    {
        this.configuration = configuration;
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        this.profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
    }

    public async Task<GameIdentityConnectResult> ConnectAsync()
    {
        if (configuration == null || !configuration.Enabled)
        {
            return GameIdentityConnectResult.SetupRequired(
                "Unity Player Accounts must be configured before recoverable sign-in is enabled.");
        }
        if (target.HasPendingConflict)
        {
            return GameIdentityConnectResult.Complete(
                GameIdentityConnectOutcome.ConflictPending,
                null,
                null,
                "Resolve the pending save choice before changing accounts.");
        }

        PlayerRecoveryState original;
        string backupPath;
        try
        {
            original = target.Capture();
            backupPath = target.CreateSafetyBackup(original);
        }
        catch (Exception exception)
        {
            return GameIdentityConnectResult.Failure(null, exception.Message);
        }

        string previousProfile = backend.CurrentProfile;
        string previousStoredProfile = profileStore.ActiveProfile;
        GameIdentityBackendResult identity;
        try
        {
            identity = await backend.LinkOrSignInAsync(configuration.LinkedProfile);
            if (identity == null || string.IsNullOrWhiteSpace(identity.ActiveProfile))
                throw new InvalidOperationException("The identity provider returned an invalid authentication profile.");
        }
        catch (Exception exception)
        {
            return GameIdentityConnectResult.Failure(backupPath, exception.Message);
        }

        if (identity.Outcome == GameIdentityBackendOutcome.CurrentPlayerLinked)
        {
            // Linking cannot be rolled back safely. Persist the profile immediately, then let an
            // ordinary later save retry if the first cloud write is temporarily unavailable.
            profileStore.SetActiveProfile(identity.ActiveProfile);
            bool saved = false;
            string saveError = null;
            try
            {
                saved = await target.SaveCloudAsync(Clone(original.Inventory));
            }
            catch (Exception exception)
            {
                saveError = exception.Message;
            }
            return GameIdentityConnectResult.Complete(
                saved
                    ? GameIdentityConnectOutcome.LinkedCurrentPlayer
                    : GameIdentityConnectOutcome.LinkedCurrentPlayerCloudPending,
                backupPath,
                identity.RedactedIdentity,
                saved ? null : saveError ?? "The initial cloud sync did not complete.");
        }

        try
        {
            CloudInventoryLoadResult cloud = await target.LoadCloudAsync();
            if (cloud == null || !cloud.Succeeded)
                throw new InvalidOperationException("The existing account save could not be loaded safely.");

            InventoryConflictPreparation preparation = target.PrepareConflict(
                Clone(original.Inventory),
                Clone(cloud.Data),
                cloud.Found);
            if (preparation.RequiresChoice)
            {
                profileStore.SetActiveProfile(identity.ActiveProfile);
                return GameIdentityConnectResult.Complete(
                    GameIdentityConnectOutcome.ConflictPending,
                    backupPath,
                    identity.RedactedIdentity);
            }

            var selected = new PlayerRecoveryState(
                Clone(preparation.Selected),
                original.Languages,
                original.Experience);
            target.Apply(selected);
            if (!await target.SaveCloudAsync(Clone(selected.Inventory)))
                throw new InvalidOperationException("The existing account save did not confirm synchronization.");

            profileStore.SetActiveProfile(identity.ActiveProfile);
            return GameIdentityConnectResult.Complete(
                GameIdentityConnectOutcome.ExistingPlayerReady,
                backupPath,
                identity.RedactedIdentity);
        }
        catch (Exception exception)
        {
            string rollbackError = null;
            try
            {
                target.Apply(original);
            }
            catch (Exception rollbackException)
            {
                rollbackError = " Local rollback failed: " + rollbackException.Message;
            }
            try
            {
                await backend.RestoreProfileAsync(previousProfile);
                profileStore.SetActiveProfile(previousStoredProfile);
            }
            catch (Exception rollbackException)
            {
                rollbackError += " Authentication rollback failed: " + rollbackException.Message;
            }
            return GameIdentityConnectResult.Failure(
                backupPath,
                exception.Message + rollbackError);
        }
    }

    private static InventoryData Clone(InventoryData inventory) =>
        CloudInventoryConflictCoordinator.Clone(inventory);
}

public sealed class PlayerPrefsGameIdentityProfileStore : IGameIdentityProfileStore
{
    private const string Key = "universal_gacha.player_identity.active_profile.v1";

    public string ActiveProfile
    {
        get
        {
            string profile = PlayerPrefs.GetString(Key, string.Empty);
            return IsValidProfile(profile) ? profile : string.Empty;
        }
    }

    public void SetActiveProfile(string profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
            return;
        }
        if (!IsValidProfile(profile))
            throw new ArgumentException("The authentication profile is invalid.", nameof(profile));
        PlayerPrefs.SetString(Key, profile);
        PlayerPrefs.Save();
    }

    public static bool IsValidProfile(string profile) =>
        !string.IsNullOrWhiteSpace(profile) && profile.Length <= 30 &&
        profile.All(character => char.IsLetterOrDigit(character) || character == '-' || character == '_');
}

public sealed class UnityGameIdentityBackend : IGameIdentityBackend
{
    public string CurrentProfile => AuthenticationService.Instance.Profile;

    public async Task<GameIdentityBackendResult> LinkOrSignInAsync(string linkedProfile)
    {
        if (!await CloudSaveServiceWrapper.InitializeAsync())
            throw new InvalidOperationException("Unity Authentication is unavailable.");

        IAuthenticationService authentication = AuthenticationService.Instance;
        if (HasUnityIdentity(authentication))
            return CreateResult(GameIdentityBackendOutcome.CurrentPlayerLinked, authentication.Profile);

        IPlayerAccountService playerAccounts = PlayerAccountService.Instance;
        if (!playerAccounts.IsSignedIn)
            await playerAccounts.StartSignInAsync();
        string accessToken = playerAccounts.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Unity Player Accounts did not provide an access token.");

        try
        {
            await authentication.LinkWithUnityAsync(accessToken);
            return CreateResult(GameIdentityBackendOutcome.CurrentPlayerLinked, authentication.Profile);
        }
        catch (AuthenticationException exception)
            when (exception.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
        {
            string previousProfile = authentication.Profile;
            try
            {
                authentication.SignOut(false);
                authentication.SwitchProfile(linkedProfile);
                await authentication.SignInWithUnityAsync(accessToken);
                return CreateResult(GameIdentityBackendOutcome.ExistingPlayerSignedIn, linkedProfile);
            }
            catch
            {
                await RestoreProfileAsync(previousProfile);
                throw;
            }
        }
    }

    public async Task RestoreProfileAsync(string previousProfile)
    {
        IAuthenticationService authentication = AuthenticationService.Instance;
        if (authentication.IsSignedIn)
            authentication.SignOut(false);
        if (!string.Equals(authentication.Profile, previousProfile, StringComparison.Ordinal))
            authentication.SwitchProfile(previousProfile);
        await authentication.SignInAnonymouslyAsync();
    }

    private static bool HasUnityIdentity(IAuthenticationService authentication) =>
        authentication.IsSignedIn && authentication.PlayerInfo?.Identities != null &&
        authentication.PlayerInfo.Identities.Any(identity =>
            string.Equals(identity.TypeId, "unity", StringComparison.OrdinalIgnoreCase));

    private static GameIdentityBackendResult CreateResult(
        GameIdentityBackendOutcome outcome,
        string activeProfile)
    {
        return new GameIdentityBackendResult(
            outcome,
            activeProfile,
            AuthenticationService.Instance.PlayerId,
            GameIdentityService.GetRedactedIdentity());
    }
}

public sealed class UnityGameIdentityPlayerTarget : IGameIdentityPlayerTarget
{
    private readonly UnityPlayerRecoveryTarget recoveryTarget;

    public UnityGameIdentityPlayerTarget()
    {
        if (Inventory.Instance == null || !ApplicationServices.IsConfigured ||
            ApplicationServices.Languages == null || ApplicationServices.ExperienceSettings == null)
        {
            throw new InvalidOperationException("Player recovery services are unavailable.");
        }

        CatalogLoadResult load = ApplicationServices.Catalog.EnsureLoaded();
        recoveryTarget = new UnityPlayerRecoveryTarget(
            Inventory.Instance,
            LocalSaveService.Save,
            ApplicationServices.Languages,
            ApplicationServices.ExperienceSettings,
            load.Succeeded ? load.Catalog : null);
    }

    public bool HasPendingConflict => GameCloudConflictSession.Current.HasPending;
    public PlayerRecoveryState Capture() => recoveryTarget.Capture();

    public string CreateSafetyBackup(PlayerRecoveryState state)
    {
        string directory = Path.Combine(Application.persistentDataPath, "Recovery", "Backups");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            "pre-account-link-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".gachasave");
        new InventoryRecoveryService().Export(path, state, RecoveryInstallIdentity.GetOrCreate());
        return Path.GetFullPath(path);
    }

    public void Apply(PlayerRecoveryState state) => recoveryTarget.Apply(state);
    public Task<CloudInventoryLoadResult> LoadCloudAsync() => CloudSaveServiceWrapper.LoadInventoryAsync();
    public Task<bool> SaveCloudAsync(InventoryData inventory) =>
        CloudSaveServiceWrapper.SaveInventoryForConflictResolutionAsync(inventory);
    public InventoryConflictPreparation PrepareConflict(InventoryData local, InventoryData cloud, bool cloudFound) =>
        GameCloudConflictSession.Current.Prepare(local, cloud, cloudFound);
}

public static class GameIdentityService
{
    private static bool isBusy;

    public static event Action Changed;
    public static bool IsBusy => isBusy;

    public static GameIdentityStatus GetStatus()
    {
        if (!GameIdentityConfiguration.TryLoad(out GameIdentityConfiguration configuration, out _) ||
            !configuration.Enabled)
        {
            return new GameIdentityStatus(GameIdentityStatusKind.ExternalSetupRequired);
        }
        if (isBusy)
            return new GameIdentityStatus(GameIdentityStatusKind.Busy);
        if (AuthenticationService.Instance.IsSignedIn &&
            AuthenticationService.Instance.PlayerInfo?.Identities != null &&
            AuthenticationService.Instance.PlayerInfo.Identities.Any(identity =>
                string.Equals(identity.TypeId, "unity", StringComparison.OrdinalIgnoreCase)))
        {
            return new GameIdentityStatus(GameIdentityStatusKind.Connected, GetRedactedIdentity());
        }
        return new GameIdentityStatus(GameIdentityStatusKind.Available);
    }

    public static async Task<GameIdentityConnectResult> ConnectAsync()
    {
        if (isBusy)
            return GameIdentityConnectResult.Failure(null, "Player identity setup is already in progress.");
        if (!GameIdentityConfiguration.TryLoad(out GameIdentityConfiguration configuration, out string error))
            return GameIdentityConnectResult.SetupRequired(error);

        isBusy = true;
        Changed?.Invoke();
        try
        {
            IGameIdentityPlayerTarget target;
            try
            {
                target = new UnityGameIdentityPlayerTarget();
            }
            catch (Exception exception)
            {
                return GameIdentityConnectResult.Failure(null, exception.Message);
            }
            return await new GameIdentityLinkCoordinator(
                configuration,
                new UnityGameIdentityBackend(),
                target,
                new PlayerPrefsGameIdentityProfileStore()).ConnectAsync();
        }
        finally
        {
            isBusy = false;
            Changed?.Invoke();
        }
    }

    public static string GetRedactedIdentity()
    {
        string email = PlayerAccountService.Instance?.IdTokenClaims?.Email;
        if (!string.IsNullOrWhiteSpace(email))
            return RedactEmail(email);
        string playerId = AuthenticationService.Instance.IsSignedIn
            ? AuthenticationService.Instance.PlayerId
            : null;
        if (string.IsNullOrWhiteSpace(playerId))
            return "PLAYER ID";
        string suffix = playerId.Length <= 6 ? playerId : playerId.Substring(playerId.Length - 6);
        return "PLAYER ID •" + suffix;
    }

    public static string RedactEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "EMAIL";
        int separator = email.IndexOf('@');
        if (separator <= 0 || separator == email.Length - 1) return "EMAIL";
        string local = email.Substring(0, separator);
        string domain = email.Substring(separator + 1);
        int dot = domain.LastIndexOf('.');
        string host = dot > 0 ? domain.Substring(0, dot) : domain;
        string suffix = dot > 0 ? domain.Substring(dot) : string.Empty;
        return local[0] + "***@" + host[0] + "***" + suffix;
    }
}
