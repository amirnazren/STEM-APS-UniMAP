// -----------------------------------------------------------------------------
// NetworkManager.cs — Eco-Bot Builders Unity <-> Node.js Socket.io client.
//
// PACKAGE DEPENDENCY (required for this file to compile):
//   SocketIOUnity by itisnajim — https://github.com/itisnajim/SocketIOUnity
//   Install: Window > Package Manager > "+" > "Add package from git URL..." and enter
//            https://github.com/itisnajim/SocketIOUnity.git
//   It pulls in SocketIOClient (v3.x) and the Newtonsoft JSON serializer adapter
//   (SocketIOClient.Newtonsoft.Json). Unity's "com.unity.nuget.newtonsoft-json" package must also
//   be present (Unity 2022 LTS ships it as a dependency of many packages; add it explicitly if not).
//
// PROTOCOL: event names and payloads mirror schemas/websocket-payloads.json and
// backend/sockets/socketHandler.js exactly:
//   C2S  student:join, student:token_earned, student:level_completed,
//        student:energy_contributed, student:stuck_alert, client:heartbeat
//   S2C  student:joined, student:join_error, class:paused, class:resumed, audio:mute,
//        audio:unmute, COSMETIC_UNLOCKED, COMMUNITY_PROGRESS, TOKEN_EARNED, LEVEL_COMPLETED,
//        teacher:disconnected
//
// THREADING: SocketIOClient raises callbacks on thread-pool threads. Every callback is marshalled
// onto Unity's main thread through a ConcurrentQueue drained in Update(), so subscribers of the
// public C# events may safely touch GameObjects, UI and audio.
//
// RESILIENCE:
//   * Manual reconnection with exponential backoff + jitter (built-in reconnection is disabled so
//     every new connection re-sends student:join and then flushes the offline queue).
//   * Offline queue: game events emitted while disconnected / not yet joined are queued (bounded,
//     persisted in PlayerPrefs across app restarts) and flushed in order after a successful join.
//   * Ack timeouts on every emit + an application heartbeat (client:heartbeat, acked by the server)
//     measure latency and detect half-open connections; repeated misses force a reconnect.
//   * All timers use unscaled time so a teacher pause (Time.timeScale = 0) never stalls networking.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using EcoBotBuilders.Audio;
using EcoBotBuilders.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SocketIOClient;
using SocketIOClient.Newtonsoft.Json;
using UnityEngine;

namespace EcoBotBuilders.Network
{
    #region Payload DTOs (field names match schemas/websocket-payloads.json)

    /// <summary>Four STEM competency scores, each 0..1.</summary>
    [Serializable]
    public class StemCompetencyScores
    {
        public float computationalThinking;
        public float systemicEngineering;
        public float mathModeling;
        public float persistence;
    }

    /// <summary>Editor position of a block (unity-block-program-schema.json#/definitions/uiPosition).</summary>
    [Serializable]
    public class BlockUiPosition
    {
        public float x;
        public float y;
        public float width;
        public float height;
    }

    /// <summary>One serialized block (unity-block-program-schema.json#/definitions/block).</summary>
    [Serializable]
    public class BlockSnapshot
    {
        public string blockId;
        public string blockType;

        /// <summary>string, number or null depending on <see cref="blockType"/>.</summary>
        public object parameter;

        public List<BlockSnapshot> children = new List<BlockSnapshot>();

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public BlockUiPosition uiPosition;
    }

    /// <summary>Telemetry object sent inside student:level_completed (definitions/telemetry).</summary>
    [Serializable]
    public class LevelTelemetryData
    {
        public int retriesCount;
        public float timeSpentSeconds;
        public bool subroutinesUsed;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string completedAt;

        public StemCompetencyScores stemCompetencyScores = new StemCompetencyScores();
        public List<BlockSnapshot> blockProgramSnapshot = new List<BlockSnapshot>();
    }

    /// <summary>student:joined (direct) — also the success ack of student:join.</summary>
    [Serializable]
    public class StudentJoinedData
    {
        public string studentId;
        public string studentName;
        public int currentLevel;
        public int cleanTokensBalance;
        public List<string> unlockedCosmetics = new List<string>();
        public bool isPaused;
        public bool isMuted;
        public int communityProgress;
        public int communityGoal;
    }

    /// <summary>COSMETIC_UNLOCKED (student room).</summary>
    [Serializable]
    public class CosmeticUnlockedData
    {
        public string studentId;
        public string cosmeticId;
        public int levelId;
    }

    /// <summary>COMMUNITY_PROGRESS (room). Legacy aliases are kept by the server and mirrored here.</summary>
    [Serializable]
    public class CommunityProgressData
    {
        public int tokens;
        public int goal;
        public float percentage;
        public int communityProgress;
        public int communityGoal;
        public float percent;
    }

    /// <summary>class:paused / class:resumed / audio:mute / audio:unmute / teacher:disconnected.</summary>
    [Serializable]
    public class RoomSignalData
    {
        public string roomPin;
        public string at;
    }

    /// <summary>TOKEN_EARNED (room).</summary>
    [Serializable]
    public class TokenEarnedBroadcast
    {
        public string studentId;
        public string studentName;
        public int amount;
        public int levelId;
        public int newBalance;
        public int? communityTokens;
    }

    /// <summary>LEVEL_COMPLETED (room).</summary>
    [Serializable]
    public class LevelCompletedBroadcast
    {
        public string studentId;
        public string studentName;
        public int levelId;
        public string cosmeticUnlocked;
        public int currentLevel;
        public bool gameCompleted;
    }

    /// <summary>Generic ack: { ok, error?, ...extra }.</summary>
    [Serializable]
    public class AckResponse
    {
        public bool ok;
        public string error;

        /// <summary>The raw ack object, for event-specific fields (newBalance, telemetryId, ...).</summary>
        [JsonIgnore]
        public JObject raw;
    }

    #endregion

    /// <summary>Connection lifecycle as seen by gameplay code.</summary>
    public enum NetworkState
    {
        /// <summary>No connection wanted (JoinClassroom not called, or Leave/join error).</summary>
        Idle,
        /// <summary>Waiting for the next backoff slot.</summary>
        WaitingToReconnect,
        /// <summary>Transport connection in progress.</summary>
        Connecting,
        /// <summary>Transport connected, student:join sent, waiting for student:joined.</summary>
        Joining,
        /// <summary>Joined the classroom; game events are delivered live.</summary>
        Joined
    }

    /// <summary>
    /// Singleton Socket.io client for the student game. Add it to a bootstrap GameObject (it persists
    /// across scenes), then call <see cref="JoinClassroom"/> from the PIN entry screen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkManager : MonoBehaviour
    {
        // ------------------------------------------------------------------ constants
        /// <summary>Default backend URL (backend PORT=3001).</summary>
        public const string DefaultBackendUrl = "http://localhost:3001";

        /// <summary>Environment variable / command-line override name (-BACKEND_URL=...).</summary>
        public const string BackendUrlEnvVar = "BACKEND_URL";

        private const string PrefsBackendUrl = "EcoBot.BackendUrl";
        private const string PrefsRoomPin = "EcoBot.RoomPin";
        private const string PrefsStudentName = "EcoBot.StudentName";
        private const string PrefsStudentId = "EcoBot.StudentId";
        private const string PrefsOfflineQueue = "EcoBot.OfflineQueue";

        private const int MinLevelId = 1;
        private const int MaxLevelId = 10;
        private const int MaxTokensPerEvent = 100;
        private const float MaxKwPerEvent = 1000f;
        private const int MaxSnapshotBlocks = 500;
        private const float MaxTimeSpentSeconds = 86400f;

        // ------------------------------------------------------------------ inspector
        [Header("Backend")]
        [Tooltip("BACKEND_URL. Overridden (in priority order) by the -BACKEND_URL=<url> command-line arg, the BACKEND_URL env var, then PlayerPrefs.")]
        [SerializeField] private string backendUrl = DefaultBackendUrl;
        [SerializeField, Min(1f)] private float connectTimeoutSeconds = 10f;

        [Header("Reconnect backoff")]
        [SerializeField, Min(0.1f)] private float reconnectBaseDelaySeconds = 1f;
        [SerializeField, Min(1f)] private float reconnectMaxDelaySeconds = 30f;
        [SerializeField, Range(0f, 0.5f)] private float reconnectJitter = 0.3f;

        [Header("Latency / heartbeat")]
        [SerializeField, Min(0.5f)] private float ackTimeoutSeconds = 6f;
        [SerializeField, Min(1f)] private float heartbeatIntervalSeconds = 10f;
        [SerializeField, Min(0.5f)] private float heartbeatTimeoutSeconds = 5f;
        [Tooltip("Consecutive missed heartbeats/acks before the connection is treated as dead and recycled.")]
        [SerializeField, Min(1)] private int maxConsecutiveMisses = 2;
        [Tooltip("Round-trip time above which IsHighLatency becomes true.")]
        [SerializeField, Min(50f)] private float highLatencyThresholdMs = 1500f;

        [Header("Offline queue")]
        [SerializeField, Min(1)] private int maxQueuedEvents = 200;
        [SerializeField] private bool persistQueueAcrossSessions = true;

        [Header("Behaviour")]
        [Tooltip("Apply audio:mute / audio:unmute to AdaptiveAudioEngine automatically.")]
        [SerializeField] private bool applyTeacherMuteToAudio = true;
        [SerializeField] private bool verboseLogging;

        // ------------------------------------------------------------------ public events (main thread)
        /// <summary>Transport connected (before the classroom join completes).</summary>
        public event Action OnConnected;
        /// <summary>Transport lost; argument is the reason reported by the client library.</summary>
        public event Action<string> OnDisconnected;
        /// <summary>student:joined received — the student is in the classroom.</summary>
        public event Action<StudentJoinedData> OnJoined;
        /// <summary>student:join_error (or a failed join ack). Reconnection stops until JoinClassroom is called again.</summary>
        public event Action<string> OnJoinError;
        /// <summary>class:paused.</summary>
        public event Action<RoomSignalData> OnClassPaused;
        /// <summary>class:resumed.</summary>
        public event Action<RoomSignalData> OnClassResumed;
        /// <summary>audio:mute.</summary>
        public event Action<RoomSignalData> OnAudioMuted;
        /// <summary>audio:unmute.</summary>
        public event Action<RoomSignalData> OnAudioUnmuted;
        /// <summary>COSMETIC_UNLOCKED for this student.</summary>
        public event Action<CosmeticUnlockedData> OnCosmeticUnlocked;
        /// <summary>COMMUNITY_PROGRESS (class-wide token goal, default 1000).</summary>
        public event Action<CommunityProgressData> OnCommunityProgress;
        /// <summary>teacher:disconnected — the teacher dashboard closed; gameplay may continue.</summary>
        public event Action<RoomSignalData> OnTeacherDisconnected;
        /// <summary>TOKEN_EARNED broadcast (any student in the room).</summary>
        public event Action<TokenEarnedBroadcast> OnTokenEarnedBroadcast;
        /// <summary>LEVEL_COMPLETED broadcast (any student in the room).</summary>
        public event Action<LevelCompletedBroadcast> OnLevelCompletedBroadcast;
        /// <summary>New round-trip latency sample in milliseconds.</summary>
        public event Action<float> OnLatencyUpdated;
        /// <summary>An emit did not receive its ack in time. Argument is the event name.</summary>
        public event Action<string> OnAckTimeout;
        /// <summary>The server acked an emit with ok:false. Arguments: event name, error.</summary>
        public event Action<string, string> OnEmitRejected;
        /// <summary>The connection state changed.</summary>
        public event Action<NetworkState> OnStateChanged;

        // ------------------------------------------------------------------ public state
        /// <summary>The singleton instance (null until Awake).</summary>
        public static NetworkManager Instance { get; private set; }

        /// <summary>Resolved backend URL. Set before calling JoinClassroom to change it at runtime.</summary>
        public string BackendUrl
        {
            get => backendUrl;
            set
            {
                backendUrl = string.IsNullOrWhiteSpace(value) ? DefaultBackendUrl : value.Trim();
                PlayerPrefs.SetString(PrefsBackendUrl, backendUrl);
            }
        }

        public NetworkState State { get; private set; } = NetworkState.Idle;
        public bool IsJoined => State == NetworkState.Joined;
        public string RoomPin { get; private set; }
        public string StudentName { get; private set; }
        public string StudentId { get; private set; }
        public StudentJoinedData LastJoinedData { get; private set; }
        public bool IsClassPaused { get; private set; }
        public bool IsAudioMuted { get; private set; }
        public float LatencyMs { get; private set; } = -1f;
        public bool IsHighLatency => LatencyMs >= highLatencyThresholdMs;
        public int QueuedEventCount => offlineQueue.Count;

        // ------------------------------------------------------------------ internals
        /// <summary>A queued or in-flight game event.</summary>
        [Serializable]
        private sealed class OutgoingEvent
        {
            public string eventName;
            public JObject payload;
            /// <summary>Re-queue on ack timeout. False for additive events (tokens/energy) to avoid double counting.</summary>
            public bool retryOnAckTimeout;
            public int attempts;
        }

        private sealed class PendingAck
        {
            public OutgoingEvent evt;
            public float deadline;
            public int generation;
        }

        private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();
        private readonly LinkedList<OutgoingEvent> offlineQueue = new LinkedList<OutgoingEvent>();
        private readonly Dictionary<int, PendingAck> pendingAcks = new Dictionary<int, PendingAck>();

        private SocketIOUnity socket;
        private int socketGeneration;        // bumps on every new socket; stale callbacks are ignored
        private int nextAckId;
        private bool wantConnection;
        private int reconnectAttempt;
        private float nextConnectTime;
        private float connectStartedAt;
        private float joinDeadline;
        private bool joinResolved;
        private float nextHeartbeatTime;
        private int consecutiveMisses;

        // ================================================================== Unity lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            backendUrl = ResolveBackendUrl(backendUrl);
            StudentId = PlayerPrefs.GetString(PrefsStudentId, string.Empty);
            RoomPin = PlayerPrefs.GetString(PrefsRoomPin, string.Empty);
            StudentName = PlayerPrefs.GetString(PrefsStudentName, string.Empty);
            LoadPersistedQueue();
        }

        private void Update()
        {
            DrainMainThreadQueue();

            float now = Time.unscaledTime;

            switch (State)
            {
                case NetworkState.WaitingToReconnect:
                    if (wantConnection && now >= nextConnectTime)
                    {
                        BeginConnect();
                    }
                    break;
                case NetworkState.Connecting:
                    if (now - connectStartedAt > connectTimeoutSeconds)
                    {
                        Log("Connect timed out.");
                        HandleConnectionLost("connect timeout");
                    }
                    break;
                case NetworkState.Joining:
                    if (!joinResolved && now > joinDeadline)
                    {
                        Log("student:join ack timed out.");
                        HandleConnectionLost("join timeout");
                    }
                    break;
                case NetworkState.Joined:
                    if (now >= nextHeartbeatTime)
                    {
                        SendHeartbeat();
                    }
                    break;
            }

            CheckAckTimeouts(now);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                PersistQueue();
            }
        }

        private void OnApplicationQuit()
        {
            PersistQueue();
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            PersistQueue();
            DisposeSocket();
            Instance = null;
        }

        // ================================================================== public API

        /// <summary>
        /// Joins the classroom identified by a 4-digit PIN. Connection, reconnection, re-join and
        /// queue flushing are then handled automatically until <see cref="LeaveClassroom"/> is called
        /// or the server rejects the join (OnJoinError).
        /// </summary>
        /// <returns>False when the PIN or name is invalid locally (OnJoinError is also raised).</returns>
        public bool JoinClassroom(string roomPin, string studentName)
        {
            string pin = (roomPin ?? string.Empty).Trim();
            string name = (studentName ?? string.Empty).Trim();
            if (!IsValidPin(pin) || name.Length == 0 || name.Length > 40)
            {
                RaiseSafe(OnJoinError, "Enter a 4-digit room PIN and your name");
                return false;
            }

            // A different classroom/name means a different student identity.
            if (pin != RoomPin || !string.Equals(name, StudentName, StringComparison.Ordinal))
            {
                StudentId = string.Empty;
                PlayerPrefs.DeleteKey(PrefsStudentId);
            }

            RoomPin = pin;
            StudentName = name;
            PlayerPrefs.SetString(PrefsRoomPin, pin);
            PlayerPrefs.SetString(PrefsStudentName, name);
            PlayerPrefs.Save();

            wantConnection = true;
            reconnectAttempt = 0;

            if (State == NetworkState.Joined || State == NetworkState.Joining)
            {
                SendJoin(); // already connected: just (re-)join with the new identity
            }
            else if (State != NetworkState.Connecting)
            {
                BeginConnect();
            }

            return true;
        }

        /// <summary>Re-joins the last classroom stored in PlayerPrefs (e.g. after an app restart).</summary>
        public bool ResumeLastSession()
        {
            return IsValidPin(RoomPin) && !string.IsNullOrEmpty(StudentName) && JoinClassroom(RoomPin, StudentName);
        }

        /// <summary>Disconnects and stops reconnecting. Queued events are kept for the next session.</summary>
        public void LeaveClassroom()
        {
            wantConnection = false;
            DisposeSocket();
            SetState(NetworkState.Idle);
            PersistQueue();
        }

        /// <summary>student:token_earned { roomPin, studentId, amount (1..100), levelId (1..10) }.</summary>
        public void SendTokenEarned(int amount, int levelId)
        {
            if (amount <= 0)
            {
                return;
            }

            // The server caps one event at 100 tokens; split larger rewards into several events.
            int remaining = amount;
            while (remaining > 0)
            {
                int chunk = Math.Min(MaxTokensPerEvent, remaining);
                remaining -= chunk;
                Enqueue("student:token_earned", new JObject
                {
                    ["amount"] = chunk,
                    ["levelId"] = ClampLevel(levelId)
                }, retryOnAckTimeout: false);
            }
        }

        /// <summary>student:level_completed { roomPin, studentId, levelId, telemetry }.</summary>
        public void SendLevelCompleted(int levelId, LevelTelemetryData telemetry)
        {
            telemetry = telemetry ?? new LevelTelemetryData();
            telemetry.retriesCount = Math.Max(0, telemetry.retriesCount);
            telemetry.timeSpentSeconds = Mathf.Clamp(telemetry.timeSpentSeconds, 0f, MaxTimeSpentSeconds);
            telemetry.completedAt = string.IsNullOrEmpty(telemetry.completedAt)
                ? DateTime.UtcNow.ToString("o")
                : telemetry.completedAt;
            telemetry.stemCompetencyScores = ClampScores(telemetry.stemCompetencyScores);
            telemetry.blockProgramSnapshot = BlockProgramSerializer.Truncate(telemetry.blockProgramSnapshot, MaxSnapshotBlocks);

            Enqueue("student:level_completed", new JObject
            {
                ["levelId"] = ClampLevel(levelId),
                ["telemetry"] = JObject.FromObject(telemetry)
            }, retryOnAckTimeout: true);
        }

        /// <summary>student:energy_contributed { roomPin, studentId, kw (0, 1000], levelId }.</summary>
        public void SendEnergyContributed(float kw, int levelId)
        {
            if (!(kw > 0f))
            {
                return;
            }

            Enqueue("student:energy_contributed", new JObject
            {
                ["kw"] = Math.Round(Mathf.Min(kw, MaxKwPerEvent), 3),
                ["levelId"] = ClampLevel(levelId)
            }, retryOnAckTimeout: false);
        }

        /// <summary>student:stuck_alert { roomPin, studentId, levelId, retriesCount }.</summary>
        public void SendStuckAlert(int levelId, int retriesCount)
        {
            Enqueue("student:stuck_alert", new JObject
            {
                ["levelId"] = ClampLevel(levelId),
                ["retriesCount"] = Math.Max(0, retriesCount)
            }, retryOnAckTimeout: true);
        }

        /// <summary>
        /// Schedules an action on Unity's main thread. Safe to call from any thread.
        /// </summary>
        public void RunOnMainThread(Action action)
        {
            if (action != null)
            {
                mainThreadQueue.Enqueue(action);
            }
        }

        // ================================================================== connection

        private void BeginConnect()
        {
            if (!wantConnection)
            {
                return;
            }

            DisposeSocket();
            int generation = ++socketGeneration;
            SetState(NetworkState.Connecting);
            connectStartedAt = Time.unscaledTime;

            try
            {
                var uri = new Uri(backendUrl);
                socket = new SocketIOUnity(uri, new SocketIOOptions
                {
                    // Reconnection is driven by this class so that join + queue flush always follow.
                    Reconnection = false,
                    ConnectionTimeout = TimeSpan.FromSeconds(connectTimeoutSeconds),
                    Transport = SocketIOClient.Transport.TransportProtocol.WebSocket
                });
                socket.JsonSerializer = new NewtonsoftJsonSerializer();
                RegisterSocketHandlers(socket, generation);
                Log($"Connecting to {backendUrl} (attempt {reconnectAttempt + 1})...");

                ObserveTask(socket.ConnectAsync(), generation, "connect");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NetworkManager] Could not start connection to '{backendUrl}': {ex.Message}");
                HandleConnectionLost("connect exception");
            }
        }

        private void RegisterSocketHandlers(SocketIOUnity s, int generation)
        {
            s.OnConnected += (sender, e) => Post(generation, HandleTransportConnected);
            s.OnDisconnected += (sender, reason) => Post(generation, () => HandleConnectionLost(reason ?? "disconnected"));
            s.OnError += (sender, error) => Post(generation, () => Debug.LogWarning($"[NetworkManager] Socket error: {error}"));

            On<StudentJoinedData>(s, generation, "student:joined", HandleJoined);
            On<JObject>(s, generation, "student:join_error", data => HandleJoinError(data?.Value<string>("error")));

            On<RoomSignalData>(s, generation, "class:paused", data =>
            {
                IsClassPaused = true;
                RaiseSafe(OnClassPaused, data);
            });
            On<RoomSignalData>(s, generation, "class:resumed", data =>
            {
                IsClassPaused = false;
                RaiseSafe(OnClassResumed, data);
            });
            On<RoomSignalData>(s, generation, "audio:mute", data =>
            {
                ApplyMute(true);
                RaiseSafe(OnAudioMuted, data);
            });
            On<RoomSignalData>(s, generation, "audio:unmute", data =>
            {
                ApplyMute(false);
                RaiseSafe(OnAudioUnmuted, data);
            });
            On<CosmeticUnlockedData>(s, generation, "COSMETIC_UNLOCKED", data =>
            {
                // Sent to the student room, but guard anyway in case of multiple identities per socket.
                if (data != null && (string.IsNullOrEmpty(StudentId) || data.studentId == StudentId))
                {
                    if (LastJoinedData != null && !LastJoinedData.unlockedCosmetics.Contains(data.cosmeticId))
                    {
                        LastJoinedData.unlockedCosmetics.Add(data.cosmeticId);
                    }

                    RaiseSafe(OnCosmeticUnlocked, data);
                }
            });
            On<CommunityProgressData>(s, generation, "COMMUNITY_PROGRESS", data => RaiseSafe(OnCommunityProgress, data));
            On<RoomSignalData>(s, generation, "teacher:disconnected", data => RaiseSafe(OnTeacherDisconnected, data));
            On<TokenEarnedBroadcast>(s, generation, "TOKEN_EARNED", data => RaiseSafe(OnTokenEarnedBroadcast, data));
            On<LevelCompletedBroadcast>(s, generation, "LEVEL_COMPLETED", data => RaiseSafe(OnLevelCompletedBroadcast, data));
        }

        /// <summary>Registers a typed listener whose handler runs on the main thread.</summary>
        private void On<T>(SocketIOUnity s, int generation, string eventName, Action<T> handler) where T : class
        {
            s.On(eventName, response =>
            {
                T data = null;
                try
                {
                    data = response.GetValue<T>();
                }
                catch (Exception ex)
                {
                    Post(generation, () => Debug.LogWarning($"[NetworkManager] Could not parse '{eventName}': {ex.Message}"));
                    return;
                }

                Post(generation, () => handler(data));
            });
        }

        private void HandleTransportConnected()
        {
            reconnectAttempt = 0;
            consecutiveMisses = 0;
            Log("Connected.");
            RaiseSafe(OnConnected);
            SendJoin();
        }

        private void SendJoin()
        {
            if (socket == null || !IsValidPin(RoomPin) || string.IsNullOrEmpty(StudentName))
            {
                return;
            }

            SetState(NetworkState.Joining);
            joinResolved = false;
            joinDeadline = Time.unscaledTime + ackTimeoutSeconds;

            var payload = new JObject
            {
                ["roomPin"] = RoomPin,
                ["studentName"] = StudentName
            };
            if (!string.IsNullOrEmpty(StudentId))
            {
                payload["studentId"] = StudentId; // resume the same student record after reconnects
            }

            int generation = socketGeneration;
            ObserveTask(socket.EmitAsync("student:join", response =>
            {
                AckResponse ack = ParseAck(response);
                Post(generation, () =>
                {
                    // Success is handled by the student:joined event; only failures matter here.
                    if (ack != null && !ack.ok)
                    {
                        HandleJoinError(ack.error);
                    }
                });
            }, payload), generation, "student:join");
        }

        private void HandleJoined(StudentJoinedData data)
        {
            if (data == null || string.IsNullOrEmpty(data.studentId))
            {
                return;
            }

            joinResolved = true;
            LastJoinedData = data;
            StudentId = data.studentId;
            PlayerPrefs.SetString(PrefsStudentId, StudentId);
            PlayerPrefs.Save();

            IsClassPaused = data.isPaused;
            ApplyMute(data.isMuted);
            SetState(NetworkState.Joined);
            nextHeartbeatTime = Time.unscaledTime + heartbeatIntervalSeconds;

            RaiseSafe(OnJoined, data);

            // Replay the classroom's current state so late joiners pause/mute correctly.
            var signal = new RoomSignalData { roomPin = RoomPin, at = DateTime.UtcNow.ToString("o") };
            if (data.isPaused)
            {
                RaiseSafe(OnClassPaused, signal);
            }

            if (data.isMuted)
            {
                RaiseSafe(OnAudioMuted, signal);
            }

            if (data.communityGoal > 0)
            {
                float pct = Mathf.Min(100f, Mathf.Round(100f * data.communityProgress / data.communityGoal));
                RaiseSafe(OnCommunityProgress, new CommunityProgressData
                {
                    tokens = data.communityProgress,
                    goal = data.communityGoal,
                    percentage = pct,
                    communityProgress = data.communityProgress,
                    communityGoal = data.communityGoal,
                    percent = pct
                });
            }

            FlushQueue();
        }

        private void HandleJoinError(string error)
        {
            if (joinResolved)
            {
                return; // ack and student:join_error both arrive for the same failure
            }

            joinResolved = true;
            string message = string.IsNullOrEmpty(error) ? "Could not join the classroom" : error;
            Debug.LogWarning($"[NetworkManager] Join failed: {message}");

            // A rejected PIN/name will not fix itself — stop retrying until the player re-enters it.
            wantConnection = false;
            DisposeSocket();
            SetState(NetworkState.Idle);
            RaiseSafe(OnJoinError, message);
        }

        private void HandleConnectionLost(string reason)
        {
            bool wasConnected = State == NetworkState.Joining || State == NetworkState.Joined;
            DisposeSocket();
            RequeueInFlightEvents();

            if (wasConnected)
            {
                RaiseSafe(OnDisconnected, reason);
            }

            if (!wantConnection)
            {
                SetState(NetworkState.Idle);
                return;
            }

            ScheduleReconnect(reason);
        }

        private void ScheduleReconnect(string reason)
        {
            // Exponential backoff with +/- jitter: 1s, 2s, 4s, ... capped at reconnectMaxDelaySeconds.
            float exp = reconnectBaseDelaySeconds * Mathf.Pow(2f, Mathf.Min(reconnectAttempt, 10));
            float delay = Mathf.Min(reconnectMaxDelaySeconds, exp);
            delay *= 1f + UnityEngine.Random.Range(-reconnectJitter, reconnectJitter);
            delay = Mathf.Max(0.1f, delay);
            reconnectAttempt++;

            nextConnectTime = Time.unscaledTime + delay;
            SetState(NetworkState.WaitingToReconnect);
            Log($"Connection lost ({reason}); reconnecting in {delay:0.0}s (attempt {reconnectAttempt}).");
        }

        private void DisposeSocket()
        {
            if (socket == null)
            {
                return;
            }

            SocketIOUnity old = socket;
            socket = null;
            socketGeneration++; // invalidate callbacks from the old socket

            try
            {
                Task disconnect = old.DisconnectAsync();
                disconnect.ContinueWith(_ =>
                {
                    try
                    {
                        old.Dispose();
                    }
                    catch (Exception)
                    {
                        // Disposal failures on a dead socket are irrelevant.
                    }
                });
            }
            catch (Exception)
            {
                try
                {
                    old.Dispose();
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }

        // ================================================================== emitting / offline queue

        private void Enqueue(string eventName, JObject payload, bool retryOnAckTimeout)
        {
            var evt = new OutgoingEvent { eventName = eventName, payload = payload, retryOnAckTimeout = retryOnAckTimeout };

            if (IsJoined && socket != null)
            {
                EmitNow(evt);
                return;
            }

            AddToQueue(evt, atFront: false);
            Log($"Queued '{eventName}' while offline ({offlineQueue.Count} queued).");
        }

        private void AddToQueue(OutgoingEvent evt, bool atFront)
        {
            if (atFront)
            {
                offlineQueue.AddFirst(evt);
            }
            else
            {
                offlineQueue.AddLast(evt);
            }

            while (offlineQueue.Count > maxQueuedEvents)
            {
                Debug.LogWarning($"[NetworkManager] Offline queue full; dropping oldest '{offlineQueue.First.Value.eventName}'.");
                offlineQueue.RemoveFirst();
            }
        }

        private void FlushQueue()
        {
            if (offlineQueue.Count == 0)
            {
                return;
            }

            Log($"Flushing {offlineQueue.Count} queued event(s).");
            while (offlineQueue.Count > 0 && IsJoined && socket != null)
            {
                OutgoingEvent evt = offlineQueue.First.Value;
                offlineQueue.RemoveFirst();
                EmitNow(evt);
            }

            PersistQueue();
        }

        private void EmitNow(OutgoingEvent evt)
        {
            // Identity is stamped at send time so events queued before the join (or before a
            // studentId change) are delivered with the current, server-issued identity.
            evt.payload["roomPin"] = RoomPin;
            evt.payload["studentId"] = StudentId;
            evt.attempts++;

            int ackId = ++nextAckId;
            int generation = socketGeneration;
            pendingAcks[ackId] = new PendingAck
            {
                evt = evt,
                deadline = Time.unscaledTime + ackTimeoutSeconds,
                generation = generation
            };

            float sentAt = Time.realtimeSinceStartup;
            ObserveTask(socket.EmitAsync(evt.eventName, response =>
            {
                AckResponse ack = ParseAck(response);
                float rttMs = (Time.realtimeSinceStartup - sentAt) * 1000f;
                RunOnMainThread(() => HandleAck(ackId, ack, rttMs));
            }, evt.payload), generation, evt.eventName);
        }

        private void HandleAck(int ackId, AckResponse ack, float rttMs)
        {
            if (!pendingAcks.TryGetValue(ackId, out PendingAck pending))
            {
                return; // already timed out
            }

            pendingAcks.Remove(ackId);
            consecutiveMisses = 0;
            UpdateLatency(rttMs);

            if (ack != null && !ack.ok)
            {
                string error = ack.error ?? "rejected";
                Debug.LogWarning($"[NetworkManager] '{pending.evt.eventName}' rejected: {error}");

                // The server lost our join (e.g. it restarted): re-queue and re-join.
                if (error.IndexOf("Join the room", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AddToQueue(pending.evt, atFront: true);
                    SendJoin();
                    return;
                }

                RaiseSafe(OnEmitRejected, pending.evt.eventName, error);
            }
        }

        private void CheckAckTimeouts(float now)
        {
            if (pendingAcks.Count == 0)
            {
                return;
            }

            List<int> expired = null;
            foreach (KeyValuePair<int, PendingAck> kv in pendingAcks)
            {
                if (now >= kv.Value.deadline)
                {
                    (expired ??= new List<int>()).Add(kv.Key);
                }
            }

            if (expired == null)
            {
                return;
            }

            foreach (int id in expired)
            {
                PendingAck pending = pendingAcks[id];
                pendingAcks.Remove(id);
                consecutiveMisses++;
                Debug.LogWarning($"[NetworkManager] No ack for '{pending.evt.eventName}' within {ackTimeoutSeconds:0.#}s.");
                RaiseSafe(OnAckTimeout, pending.evt.eventName);

                if (pending.evt.retryOnAckTimeout && pending.evt.attempts < 5)
                {
                    AddToQueue(pending.evt, atFront: true);
                }
            }

            if (consecutiveMisses >= maxConsecutiveMisses && State == NetworkState.Joined)
            {
                Debug.LogWarning("[NetworkManager] Connection unresponsive; recycling socket.");
                HandleConnectionLost("ack timeout");
            }
            else if (IsJoined)
            {
                FlushQueue();
            }
        }

        /// <summary>On disconnect, events still waiting for an ack are re-queued if they are safe to resend.</summary>
        private void RequeueInFlightEvents()
        {
            if (pendingAcks.Count == 0)
            {
                return;
            }

            var inFlight = new List<KeyValuePair<int, PendingAck>>(pendingAcks);
            inFlight.Sort((a, b) => b.Key.CompareTo(a.Key)); // newest first, so AddFirst keeps order
            foreach (KeyValuePair<int, PendingAck> kv in inFlight)
            {
                if (kv.Value.evt.retryOnAckTimeout && kv.Value.evt.attempts < 5)
                {
                    AddToQueue(kv.Value.evt, atFront: true);
                }
            }

            pendingAcks.Clear();
            PersistQueue();
        }

        // ================================================================== heartbeat / latency

        private void SendHeartbeat()
        {
            nextHeartbeatTime = Time.unscaledTime + heartbeatIntervalSeconds;
            if (socket == null)
            {
                return;
            }

            int generation = socketGeneration;
            float sentAt = Time.realtimeSinceStartup;
            bool answered = false;
            float deadline = Time.unscaledTime + heartbeatTimeoutSeconds;

            ObserveTask(socket.EmitAsync("client:heartbeat", response =>
            {
                float rttMs = (Time.realtimeSinceStartup - sentAt) * 1000f;
                Post(generation, () =>
                {
                    answered = true;
                    consecutiveMisses = 0;
                    UpdateLatency(rttMs);
                });
            }, new JObject { ["sentAt"] = (long)(sentAt * 1000f) }), generation, "client:heartbeat");

            StartCoroutine(HeartbeatWatchdog(generation, deadline, () => answered));
        }

        private System.Collections.IEnumerator HeartbeatWatchdog(int generation, float deadline, Func<bool> answered)
        {
            while (Time.unscaledTime < deadline)
            {
                yield return null;
            }

            if (generation != socketGeneration || answered())
            {
                yield break;
            }

            consecutiveMisses++;
            Debug.LogWarning($"[NetworkManager] Heartbeat missed ({consecutiveMisses}/{maxConsecutiveMisses}).");
            if (consecutiveMisses >= maxConsecutiveMisses && State == NetworkState.Joined)
            {
                HandleConnectionLost("heartbeat timeout");
            }
        }

        private void UpdateLatency(float rttMs)
        {
            // Exponential moving average smooths out single slow round trips.
            LatencyMs = LatencyMs < 0f ? rttMs : Mathf.Lerp(LatencyMs, rttMs, 0.3f);
            RaiseSafe(OnLatencyUpdated, LatencyMs);
        }

        // ================================================================== helpers

        private void ApplyMute(bool muted)
        {
            IsAudioMuted = muted;
            if (applyTeacherMuteToAudio && AdaptiveAudioEngine.Instance != null)
            {
                AdaptiveAudioEngine.Instance.SetMuted(muted);
            }
        }

        private void SetState(NetworkState next)
        {
            if (State == next)
            {
                return;
            }

            State = next;
            RaiseSafe(OnStateChanged, next);
        }

        /// <summary>Queue work for the main thread, dropping it if the socket generation changed.</summary>
        private void Post(int generation, Action action)
        {
            mainThreadQueue.Enqueue(() =>
            {
                if (generation == socketGeneration)
                {
                    action();
                }
            });
        }

        private void DrainMainThreadQueue()
        {
            // Bounded per frame so a burst of events cannot stall a frame indefinitely.
            int budget = 256;
            while (budget-- > 0 && mainThreadQueue.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        /// <summary>Surfaces exceptions from fire-and-forget socket tasks on the main thread.</summary>
        private void ObserveTask(Task task, int generation, string label)
        {
            if (task == null)
            {
                return;
            }

            task.ContinueWith(t =>
            {
                string message = t.Exception?.GetBaseException().Message ?? "unknown error";
                Post(generation, () =>
                {
                    Debug.LogWarning($"[NetworkManager] '{label}' failed: {message}");
                    if (label == "connect")
                    {
                        HandleConnectionLost("connect failed");
                    }
                });
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private static AckResponse ParseAck(SocketIOResponse response)
        {
            try
            {
                JObject raw = response.GetValue<JObject>();
                if (raw == null)
                {
                    return null;
                }

                return new AckResponse
                {
                    ok = raw.Value<bool?>("ok") ?? false,
                    error = raw.Value<string>("error"),
                    raw = raw
                };
            }
            catch (Exception)
            {
                return null; // ack without a payload
            }
        }

        private void RaiseSafe(Action handler)
        {
            try { handler?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void RaiseSafe<T>(Action<T> handler, T arg)
        {
            try { handler?.Invoke(arg); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void RaiseSafe<T1, T2>(Action<T1, T2> handler, T1 a, T2 b)
        {
            try { handler?.Invoke(a, b); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void Log(string message)
        {
            if (verboseLogging)
            {
                Debug.Log($"[NetworkManager] {message}");
            }
        }

        private static bool IsValidPin(string pin)
        {
            if (string.IsNullOrEmpty(pin) || pin.Length != 4)
            {
                return false;
            }

            foreach (char c in pin)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static int ClampLevel(int levelId) => Mathf.Clamp(levelId, MinLevelId, MaxLevelId);

        private static StemCompetencyScores ClampScores(StemCompetencyScores s)
        {
            s = s ?? new StemCompetencyScores();
            return new StemCompetencyScores
            {
                computationalThinking = Mathf.Clamp01(s.computationalThinking),
                systemicEngineering = Mathf.Clamp01(s.systemicEngineering),
                mathModeling = Mathf.Clamp01(s.mathModeling),
                persistence = Mathf.Clamp01(s.persistence)
            };
        }

        private static string ResolveBackendUrl(string inspectorValue)
        {
            // 1) Command line: -BACKEND_URL=http://host:3001
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                string prefix = "-" + BackendUrlEnvVar + "=";
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && arg.Length > prefix.Length)
                {
                    return arg.Substring(prefix.Length).Trim();
                }
            }

            // 2) Environment variable (Editor / desktop builds).
            string env = Environment.GetEnvironmentVariable(BackendUrlEnvVar);
            if (!string.IsNullOrWhiteSpace(env))
            {
                return env.Trim();
            }

            // 3) Saved at runtime (e.g. a settings screen) via the BackendUrl property.
            string saved = PlayerPrefs.GetString(PrefsBackendUrl, string.Empty);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                return saved.Trim();
            }

            // 4) Inspector value, then the default.
            return string.IsNullOrWhiteSpace(inspectorValue) ? DefaultBackendUrl : inspectorValue.Trim();
        }

        private void PersistQueue()
        {
            if (!persistQueueAcrossSessions)
            {
                return;
            }

            try
            {
                var list = new List<OutgoingEvent>(offlineQueue);
                PlayerPrefs.SetString(PrefsOfflineQueue, JsonConvert.SerializeObject(list));
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NetworkManager] Could not persist offline queue: {ex.Message}");
            }
        }

        private void LoadPersistedQueue()
        {
            if (!persistQueueAcrossSessions)
            {
                return;
            }

            string json = PlayerPrefs.GetString(PrefsOfflineQueue, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            try
            {
                List<OutgoingEvent> list = JsonConvert.DeserializeObject<List<OutgoingEvent>>(json);
                if (list == null)
                {
                    return;
                }

                foreach (OutgoingEvent evt in list)
                {
                    if (evt != null && !string.IsNullOrEmpty(evt.eventName) && evt.payload != null)
                    {
                        AddToQueue(evt, atFront: false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NetworkManager] Discarding unreadable offline queue: {ex.Message}");
                PlayerPrefs.DeleteKey(PrefsOfflineQueue);
            }
        }
    }

    /// <summary>
    /// Converts the runtime <see cref="LogicBlock"/> tree into the wire format of
    /// schemas/unity-block-program-schema.json (blockType enum such as ACTION_MOVE, LOOP_REPEAT ...).
    /// </summary>
    public static class BlockProgramSerializer
    {
        /// <summary>Serializes a program; Start/End/Unknown wrapper blocks are flattened into their parent.</summary>
        public static List<BlockSnapshot> Serialize(IReadOnlyList<LogicBlock> program, int maxBlocks = 500)
        {
            var result = new List<BlockSnapshot>();
            int budget = Mathf.Max(0, maxBlocks);
            AppendBlocks(program, result, ref budget);
            return result;
        }

        /// <summary>Ensures a snapshot contains at most <paramref name="maxBlocks"/> blocks in total.</summary>
        public static List<BlockSnapshot> Truncate(List<BlockSnapshot> snapshot, int maxBlocks)
        {
            if (snapshot == null)
            {
                return new List<BlockSnapshot>();
            }

            int budget = maxBlocks;
            return TruncateRecursive(snapshot, ref budget);
        }

        private static List<BlockSnapshot> TruncateRecursive(List<BlockSnapshot> blocks, ref int budget)
        {
            var kept = new List<BlockSnapshot>();
            if (blocks == null)
            {
                return kept;
            }

            foreach (BlockSnapshot b in blocks)
            {
                if (b == null || budget <= 0)
                {
                    continue;
                }

                budget--;
                b.children = TruncateRecursive(b.children, ref budget);
                kept.Add(b);
            }

            return kept;
        }

        private static void AppendBlocks(IReadOnlyList<LogicBlock> blocks, List<BlockSnapshot> output, ref int budget)
        {
            if (blocks == null)
            {
                return;
            }

            foreach (LogicBlock block in blocks)
            {
                if (block == null || budget <= 0)
                {
                    continue;
                }

                string wireType = MapBlockType(block, out object parameter);
                if (wireType == null)
                {
                    // Structural wrapper (Start/End/Unknown): keep its children at this level.
                    AppendBlocks(block.Children, output, ref budget);
                    continue;
                }

                budget--;
                var snapshot = new BlockSnapshot
                {
                    blockId = string.IsNullOrEmpty(block.Id) ? Guid.NewGuid().ToString("N") : block.Id,
                    blockType = wireType,
                    parameter = parameter
                };
                AppendBlocks(block.Children, snapshot.children, ref budget);
                output.Add(snapshot);

                // The runtime model stores the else-branch on the If block; the wire format uses a
                // CONDITION_ELSE sibling that directly follows the CONDITION_IF.
                if (block.BlockType == BlockType.If && block.ElseChildren != null && block.ElseChildren.Count > 0 && budget > 0)
                {
                    budget--;
                    var elseBlock = new BlockSnapshot
                    {
                        blockId = snapshot.blockId + "_else",
                        blockType = "CONDITION_ELSE",
                        parameter = null
                    };
                    AppendBlocks(block.ElseChildren, elseBlock.children, ref budget);
                    output.Add(elseBlock);
                }
            }
        }

        /// <summary>Maps a runtime block to its wire blockType; returns null for structural wrappers.</summary>
        public static string MapBlockType(LogicBlock block, out object parameter)
        {
            parameter = null;
            switch (block.BlockType)
            {
                case BlockType.Trigger:
                    return "TRIGGER_ALWAYS_LOOP";
                case BlockType.If:
                    parameter = string.IsNullOrEmpty(block.ConditionKey)
                        ? null
                        : $"{block.ConditionKey} {block.ConditionOperator ?? ">"} {block.ConditionValue}";
                    return "CONDITION_IF";
                case BlockType.Else:
                    return "CONDITION_ELSE";
                case BlockType.Repeat:
                    parameter = Mathf.Clamp(block.RepeatCount, 1, 20);
                    return "LOOP_REPEAT";
                case BlockType.SubroutineCall:
                    parameter = block.SubroutineName;
                    return "SUBROUTINE_CALL";
                case BlockType.Sensor:
                    parameter = Mathf.Clamp01(block.ConditionValue);
                    string sensor = ((block.SensorName ?? string.Empty) + " " + (block.ConditionKey ?? string.Empty)).ToUpperInvariant();
                    return sensor.Contains("BATTERY") ? "INPUT_BATTERY_CHARGE" : "INPUT_MOISTURE_SENSOR";
                case BlockType.Action:
                    return MapAction(block, ref parameter);
                default:
                    return null;
            }
        }

        private static string MapAction(LogicBlock block, ref object parameter)
        {
            string action = (block.ActionName ?? block.DisplayName ?? string.Empty).ToUpperInvariant();
            if (action.Contains("TURBINE") || action.Contains("ROTATE"))
            {
                return "ACTION_ROTATE_TURBINE";
            }

            if (action.Contains("TURN"))
            {
                parameter = action.Contains("LEFT") ? "LEFT" : "RIGHT";
                return "ACTION_TURN";
            }

            if (action.Contains("CLEAN"))
            {
                return "ACTION_CLEAN";
            }

            if (action.Contains("WATER"))
            {
                parameter = 1;
                return "ACTION_WATER";
            }

            parameter = 1;
            return "ACTION_MOVE"; // MOVE / MOVE_FORWARD and any unrecognised locomotion action
        }
    }
}
