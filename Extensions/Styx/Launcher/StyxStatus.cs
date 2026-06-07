using System.Text.Json;

namespace D2CompanionMvc.Extensions.Styx.Launcher;

/// <summary>
/// Singleton that tracks live Styx proxy state so controllers and the UI can surface it.
/// Fires <see cref="Changed"/> on every state transition so SSE clients see updates instantly.
/// </summary>
public sealed class StyxStatus
{
    public const string SessionStateNone = "none";
    public const string SessionStateWaiting = "waiting";
    public const string SessionStateConnecting = "connecting";
    public const string SessionStateCharacterSelection = "character-selection";
    public const string SessionStateLobby = "lobby";
    public const string SessionStateInGame = "in-game";

    private volatile bool _running;
    private DateTimeOffset? _lastSnapshotAt;
    private DateTimeOffset? _gameStartedAt;
    private int _totalItemsReceived;
    private string? _lastError;
    private string? _accountName;
    private string? _characterName;
    private string? _gameName;
    private string _sessionState = SessionStateNone;
    private readonly object _lock = new();

    /// <summary>Raised whenever Running, LastSnapshotAt, TotalItemsReceived, or LastError changes.</summary>
    public event Action<StyxStatus>? Changed;

    public bool Running => _running;

    public DateTimeOffset? LastSnapshotAt
    {
        get { lock (_lock) return _lastSnapshotAt; }
    }

    public int TotalItemsReceived
    {
        get { lock (_lock) return _totalItemsReceived; }
    }

    /// <summary>
    /// Last fatal startup/runtime reason for the proxy not being available,
    /// e.g. "Port 20676 already in use". Null when the proxy is healthy or
    /// has never failed in a way the host could classify. Cleared on the
    /// next successful <see cref="SetRunning(bool)"/> with running=true.
    /// </summary>
    public string? LastError
    {
        get { lock (_lock) return _lastError; }
    }

    public string SessionState
    {
        get { lock (_lock) return _sessionState; }
    }

    public string? AccountName
    {
        get { lock (_lock) return _accountName; }
    }

    public string? CharacterName
    {
        get { lock (_lock) return _characterName; }
    }

    public string? GameName
    {
        get { lock (_lock) return _gameName; }
    }

    public DateTimeOffset? GameStartedAt
    {
        get { lock (_lock) return _gameStartedAt; }
    }

    public void SetRunning(bool running)
    {
        var changed = _running != running;
        _running = running;
        if (running)
        {
            // Successful start invalidates any prior startup error.
            lock (_lock) _lastError = null;
            changed = true;
        }
        else
        {
            lock (_lock)
            {
                _sessionState = SessionStateNone;
                _accountName = null;
                _characterName = null;
                _gameName = null;
                _gameStartedAt = null;
            }
        }
        if (changed) Changed?.Invoke(this);
    }

    /// <summary>
    /// Records a fatal Styx startup/runtime error and fires Changed so
    /// connected SSE clients can render it in the status bar. Does not
    /// modify Running — callers are expected to also SetRunning(false).
    /// </summary>
    public void SetError(string? message)
    {
        lock (_lock)
        {
            if (string.Equals(_lastError, message, StringComparison.Ordinal)) return;
            _lastError = message;
        }
        Changed?.Invoke(this);
    }

    public void RecordChannelConnection(string? source = null, string? host = null, int? port = null)
    {
        lock (_lock)
        {
            if (_sessionState == SessionStateInGame && port == 4000)
            {
                return;
            }

            _gameName = null;
            _gameStartedAt = null;
            _sessionState = SessionStateWaiting;
        }
        Changed?.Invoke(this);
    }

    public void RecordConnecting(string? source = null, string? host = null, int? port = null)
    {
        lock (_lock)
        {
            if (_sessionState == SessionStateInGame)
            {
                return;
            }

            _gameName = null;
            _gameStartedAt = null;
            _sessionState = SessionStateConnecting;
        }
        Changed?.Invoke(this);
    }

    public void RecordCharacterSelection(string? source = null, string? accountName = null, string? realm = null)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(accountName)) _accountName = accountName;
            _characterName = null;
            _gameName = null;
            _gameStartedAt = null;
            _sessionState = SessionStateCharacterSelection;
        }
        Changed?.Invoke(this);
    }

    public void RecordLobby(string? source = null, string? accountName = null, string? characterName = null, string? realm = null)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(accountName)) _accountName = accountName;
            if (!string.IsNullOrWhiteSpace(characterName)) _characterName = characterName;
            _gameName = null;
            _gameStartedAt = null;
            _sessionState = SessionStateLobby;
        }
        Changed?.Invoke(this);
    }

    public void RecordDisconnected(string? source = null)
    {
        lock (_lock)
        {
            _sessionState = SessionStateNone;
            _accountName = null;
            _characterName = null;
            _gameName = null;
            _gameStartedAt = null;
        }
        Changed?.Invoke(this);
    }

    public void RecordSnapshot(
        int itemCount,
        string? accountName = null,
        string? characterName = null,
        string? gameName = null,
        string? snapshotPhase = null,
        DateTimeOffset? seenAt = null)
    {
        lock (_lock)
        {
            _lastSnapshotAt = DateTimeOffset.UtcNow;
            _totalItemsReceived += itemCount;
            if (!string.IsNullOrWhiteSpace(accountName)) _accountName = accountName;
            if (!string.IsNullOrWhiteSpace(characterName)) _characterName = characterName;

            var isFinal = string.Equals(snapshotPhase, "final", StringComparison.OrdinalIgnoreCase);
            var hasLiveCharacterSnapshot = !string.IsNullOrWhiteSpace(characterName) && !isFinal;
            if (hasLiveCharacterSnapshot)
            {
                var resolvedGameName = string.IsNullOrWhiteSpace(gameName) ? _gameName : gameName;
                if (!string.Equals(_gameName, resolvedGameName, StringComparison.Ordinal) || _sessionState != SessionStateInGame)
                {
                    _gameStartedAt = seenAt ?? DateTimeOffset.UtcNow;
                }

                _gameName = resolvedGameName;
                _sessionState = SessionStateInGame;
            }
            else if (!string.IsNullOrWhiteSpace(characterName))
            {
                _gameName = null;
                _gameStartedAt = null;
                _sessionState = isFinal ? SessionStateLobby : SessionStateWaiting;
            }
        }
        Changed?.Invoke(this);
    }

    public object ToPayload() => new
    {
        styxRunning = Running,
        lastSnapshotAt = LastSnapshotAt,
        totalItemsReceived = TotalItemsReceived,
        lastError = LastError,
        sessionState = SessionState,
        accountName = AccountName,
        characterName = CharacterName,
        gameName = GameName,
        gameStartedAt = GameStartedAt,
        inGame = string.Equals(SessionState, SessionStateInGame, StringComparison.Ordinal)
    };

    /// <summary>Serialize current state to JSON for SSE payloads.</summary>
    public string ToJson() => JsonSerializer.Serialize(ToPayload());
}
