using System.Collections.Generic;
using UnityEngine;
using SnowplowTracker;
using SnowplowTracker.Emitters;
using SnowplowTracker.Events;
using SnowplowTracker.Enums;
using SnowplowTracker.Storage;
using System.Text;
using System.Threading.Tasks;

namespace RuzdAnalytics
{
    public class Analytics : MonoBehaviour
    {
        private static Analytics _instance;


        private IEmitter emitter;
        private IStore store;
        private APIClient apiClient;
        private Tracker tracker;
        private Subject subject;
        private GameSession session;
        private SystemContext sysContext;
        private bool sendStartEvent = true;
        private bool trackingEnabledUser = false;
        private bool trackingEnabledServer = false;
        private bool trackingRunning = false;
        private bool trackingSetup = false;
        private bool disablePing = false;
        private long lastFPSEvent;
        private int FPS_MIN_INTERVAL_SECONDS = 60;
        private readonly string POST_SUFFIX = "/com.ruzd/tp2";
        private readonly string GET_SUFFIX = "/i";
        private string defaultPlayerId;

        public HttpMethod httpMethod = HttpMethod.POST;
        public HttpProtocol httpProtocol = HttpProtocol.HTTPS;
        public string customTrackingPath;
        public string customTrackingEndpoint;
        public string serverTrackingEndpoint;
        public string ruzdGameId;
        public string customPlayerId;
        public string customVersion;
        public TrackingLevel trackingLevel = TrackingLevel.NORMAL;

        private static readonly int MAX_FEEDBACK_LENGTH = 512;
        private static readonly string SDK_NAME = "ruzd-unity";
        private static readonly string SDK_VERSION = "1.2.0";
        private static readonly object Lock = new object();
        public static bool Quitting { get; private set; }

        // Singleton Pattern Instance
        public static Analytics Instance
        {
            get
            {
                if (Quitting)
                {
                    Log.Warning("[RuzdAnalytics] Not returning Analytics instance because the application is quitting.");
                    return null;
                }
                lock (Lock)
                {
                    if (_instance != null)
                    {
                        return _instance;
                    }

                    // Searching for GameObjects
                    var instances = FindObjectsOfType<Analytics>();
                    var count = instances.Length;
                    if (count > 0)
                    {
                        if (count == 1)
                            return _instance = instances[0];
                        Log.Warning($"[RuzdAnalytics] There should never be more than one Analytics in the scene, but {count} were found. The first instance found will be used, and all others will be destroyed.");
                        for (var i = 1; i < instances.Length; i++)
                            Destroy(instances[i]);
                        return _instance = instances[0];
                    }

                    Log.Debug($"[RuzdAnalytics] An instance is needed in the scene and no existing instances were found, so a new instance will be created. Avoid this by creating a GameObject manualy.");
                    return _instance = new GameObject("Ruzd Analytics").AddComponent<Analytics>();
                }
            }
        }

        // Methods called by Unity
        void OnApplicationQuit()
        {
            Quitting = true;
        }

        private void OnDestroy()
        {
            Quitting = true;
            OnTrackingStop();
        }

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            OnAwake();
        }

        // Tracker Setup
        // OnAwake is called when the instance is created
        void OnAwake()
        {
            // Setup Event Store
            if (Application.platform == RuntimePlatform.tvOS)
            {
                store = new InMemoryEventStore();
            }
            else
            {
                BoundedEventStore bStore = new BoundedEventStore(maxSize: 500);
                // Delete old Events
                bStore.DeleteOldEvents(ttlSeconds: 60*60*24);
                store = bStore;
            }

            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                emitter = new WebGlEmitter("localhost", HttpProtocol.HTTP, httpMethod, eventStore: store);
            }
            else
            {
                emitter = new AsyncEmitter("localhost", HttpProtocol.HTTP, httpMethod, eventStore: store);
            }

            // Get or generate a unique user identifier
            defaultPlayerId = PlayerPrefs.GetString("ruzd_player_id", System.Guid.NewGuid().ToString());
            PlayerPrefs.SetString("ruzd_player_id", defaultPlayerId);
            PlayerPrefs.Save();

            // Preset FPS timer
            lastFPSEvent = Utils.GetTimestamp();
            sysContext = new SystemContext();

            Log.Debug("[RuzdAnalytics] Inititalized");
        }

        // OnConfigurationChanged is called after the user setup
        public void OnConfigurationChanged()
        {
            Log.Debug("[RuzdAnalytics] Configuration changed.");
            // Log warning if this is called again after the setup
            if (trackingSetup)
            {
                Log.Warning("[RuzdAnalytics] Configuration changed after initial Configure. Please make sure Analytics.Configure is only called once.");
            }

            // Setup API Client
            apiClient = new APIClient(this, APIClient.DEFAULT_API_ENDPOINT, ruzdGameId);
            apiClient.GetRemoteTrackingConfig().ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Log.Error("[RuzdAnalytics] Failed to get remote tracking config.");
                    // we set the trackingEnabledServer to true because we still want to track if there is a server error
                    trackingEnabledServer = true;
                }
                RemoteTrackingConfig config = task.Result;
                if (config != null && config.valid)
                {
                    Log.Debug("[RuzdAnalytics] Got valid Remote tracking config.");
                    trackingEnabledServer = config.enabled;
                    if (config.level != TrackingLevel.NOTSET)
                    {
                        Log.Debug($"[RuzdAnalytics] Remote tracking level: {config.level}");
                        trackingLevel = config.level;
                    }
                    if (!string.IsNullOrEmpty(config.trackingEndpoint))
                    {
                        Log.Debug($"[RuzdAnalytics] Remote tracking endpoint: {config.trackingEndpoint}");
                        serverTrackingEndpoint = config.trackingEndpoint;
                    }
                }
                else
                {
                    Log.Error("[RuzdAnalytics] Invalid remote tracking config.");
                    // we set the trackingEnabledServer to true because we still want to track if there is a server error
                    trackingEnabledServer = true;
                }
                OnTrackingStart();
            });

            // Basic Setup Emitter
            emitter.SetHttpMethod(httpMethod);
            emitter.SetHttpProtocol(httpProtocol);
            subject = new Subject();
            var screenRes = AnalyticsUtils.GetScreenResolution();
            var windowRes = AnalyticsUtils.GetGameResolution();
            subject.SetScreenResolution(screenRes.width, screenRes.height);
            subject.SetViewPort(windowRes.width, windowRes.height);
            subject.SetUserId(GetPlayerId());
            session = new GameSession(this, enablePing: !disablePing);
            tracker = new Tracker(emitter, GetBuildVersion(), ruzdGameId, subject: subject, session: session, platform: AnalyticsUtils.GetPlatform());         
            tracker.SetRuzdSDKVersion(GetRuzdVersionIdentifier());
            trackingSetup = true;
        }

        public void OnTrackingStart()
        {
            if (!trackingSetup)
            {
                Log.Warning("[RuzdAnalytics] Run Analytics.Configure() with valid configuration before starting event tracking.");
                return;
            }
            if (!trackingEnabledUser)
            {
                Log.Debug("[RuzdAnalytics] Tracking was not enabled by the user.");
                return;
            }
            if (!trackingEnabledServer)
            {
                Log.Debug("[RuzdAnalytics] Tracking was not enabled by the server.");
                return;
            }

            // Check collector uri
            if (string.IsNullOrEmpty(GetCollectorUri()))
            {
                Log.Error("[RuzdAnalytics] Cannot start tracking, invalid collector uri.");
                return;
            }

            emitter.SetCollectorUri(GetCollectorUri());
            tracker.StartEventTracking();
            trackingRunning = true;
            Log.Debug("[RuzdAnalytics] Tracking started");
            if (sendStartEvent)
            {
                TrackGameStart();
            }
        }

        public void OnTrackingStop()
        {
            if (tracker != null)
                tracker.StopEventTracking();
            trackingSetup = false;
            trackingRunning = false;
            Log.Debug("[RuzdAnalytics] Tracking stopped");
        }

        public string GetRuzdVersionIdentifier()
        {
            string v = $"{SDK_NAME}-{SDK_VERSION}";
            return v;
        }

        public string GetPlayerId()
        {
            if (!string.IsNullOrEmpty(customPlayerId))
            {
                return customPlayerId;
            }
            return defaultPlayerId;
        }

        public string GetBuildVersion()
        {
            if (!string.IsNullOrEmpty(customVersion))
                return customVersion;
            return Application.version;
        }

        string GetCollectorUri()
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(customTrackingEndpoint))
            {
                // Check Tracking Endpoint (should not end with /)
                if (customTrackingEndpoint.EndsWith("/"))
                {
                    sb.Append(customTrackingEndpoint.Substring(0, customTrackingEndpoint.Length - 1));
                } else
                {
                    sb.Append(customTrackingEndpoint);
                }
            }
            else if (!string.IsNullOrEmpty(serverTrackingEndpoint))
            {
                sb.Append(serverTrackingEndpoint);
            }
            else
            {
                return null;
            }
            // Add Path
            if (!string.IsNullOrEmpty(customTrackingPath))
            {
                sb.Append(customTrackingPath);
                Log.Debug($"[RuzdAnalytics] Using custom tracking path: {sb.ToString()}");
            }
            else
            {
                if (httpMethod == HttpMethod.GET)
                {
                    sb.Append(GET_SUFFIX);
                }
                else if (httpMethod == HttpMethod.POST)
                {
                    sb.Append(POST_SUFFIX);
                }
            }
            return sb.ToString();
        }

        public SystemContext GetSystemContext()
        {
            return sysContext;
        }

        public void _TrackEvent(RuzdEvent newEvent)
        {
            if (!trackingSetup)
            {
                Log.Warning("[RuzdAnalytics] Run Analytics.Configure() with valid configuration before tracking Events.");
                return;
            }
            if (!trackingRunning)
            {
                Log.Warning("[RuzdAnalytics] Event will not be recorded because event tracking was not started yet.");
                return;
            }

            Log.Debug($"[RuzdAnalytics] Tracking event: {newEvent.GetType()}");
            SelfDescribing spEvent = newEvent.GetSPEvent();
            tracker.Track(spEvent.Build());
        }

        public void SendPingEvent()
        {
            // only send ping if tracking is enabled
            if (!trackingSetup || !trackingRunning)
            {
                // fail silently
                return;
            }
            GameEvent pingEvent = new GameEvent("ping", category: "builtin", label: "ruzd_internal");
            _TrackEvent(pingEvent);
        }

        public void SetCustomBuildVersion(string buildVersion)
        {
            customVersion = buildVersion;
        }

        public void SetTrackingEndpoint(string trackingEndpoint)
        {
            this.customTrackingEndpoint = trackingEndpoint;
        }

        public void TrackFPS(double averageFPS)
        {
            long checkTime = Utils.GetTimestamp();
            if (Utils.IsTimeInRange(lastFPSEvent, checkTime, FPS_MIN_INTERVAL_SECONDS*1000))
            {
                return;
            }
            GameEvent fpsEvent = new GameEvent("fps", category: "builtin", label: "ruzd_internal", 
                value: averageFPS.ToString(System.Globalization.CultureInfo.InvariantCulture));
            FpsContext fpsContext = new FpsContext(averageFPS);
            fpsEvent.addContext(fpsContext);
            lastFPSEvent = checkTime;
            _TrackEvent(fpsEvent);
        }


        // Public static methods called usually by the user
        public static void TrackEvent(RuzdEvent ev)
        {
            Instance._TrackEvent(ev);
        }

        public static void TrackError(ErrorSeverity severity, string message)
        {
            ErrorEvent e = new ErrorEvent((int)severity, message);
            Instance._TrackEvent(e);
        }

        public static void TrackRunProgressEvent(string runIdentifier, long runPlayTimeSeconds,
            string action, string category = null, string label = null, string value = null)
        {
            ProgressEvent e = new ProgressEvent(action, category: category, label: label, value: value);
            RunContext c = new RunContext(runIdentifier, runPlayTimeSeconds);
            e.addContext(c);
            Instance._TrackEvent(e);
        }

        public static void TrackRunResourceEvent(string runIdentifier, long runPlayTimeSeconds, string name,
            double amount, string category = null, string label = null)
        {
            string value = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ResourceEvent e = new ResourceEvent(name, value: value, category: category, label: label);
            RunContext c = new RunContext(runIdentifier, runPlayTimeSeconds);
            e.addContext(c);
            Instance._TrackEvent(e);
        }

        public static void TrackRunEvent(string runIdentifier, long runPlayTimeSeconds, string action,
            string category = null, string label = null, string value = null)
        {
            GameEvent e = new GameEvent(action, category: category, label: label, value: value);
            RunContext c = new RunContext(runIdentifier, runPlayTimeSeconds);
            e.addContext(c);
            Instance._TrackEvent(e);
        }

        public static async Task<bool> SendRunFeedback(string runIdentifier, long runPlayTimeSeconds, int rating, string message = null)
        {
            RunContext c = new RunContext(runIdentifier, runPlayTimeSeconds);
            Task<bool> async_result = SendFeedback(rating, message, new List<RuzdContext> { c });
            return await async_result;
        }

        public static async Task<bool> SendFeedback(int rating, string message = null, List<RuzdContext> contexts = null)
        {
            
            if (message != null && message.Length > MAX_FEEDBACK_LENGTH)
            {
                message = message.Substring(0, MAX_FEEDBACK_LENGTH);
            }

            // add extra data
            Dictionary<string, string> extra_data = new Dictionary<string, string>();
            // check if we have a run context in contexts
            if (contexts != null)
            {
                foreach (RuzdContext c in contexts)
                {
                    if (c.GetType() == typeof(RunContext))
                    {
                        extra_data.Add("run_id", ((RunContext)c).GetRunId());
                        break;
                    }
                }
            }
            // add session_id
            extra_data.Add("session_id", Instance.session.GetSessionId());

            // send feedback directly to API
            Feedback f = new Feedback(Instance.GetPlayerId(), rating, message, extra: extra_data);
            Task<bool> async_result = Instance.apiClient.PostFeedback(f);

            GameEvent e = new GameEvent("feedback", category: "builtin",
                value: rating.ToString(System.Globalization.CultureInfo.InvariantCulture), label: "ruzd_internal");
            // add contexts if any
            if (contexts != null)
            {
                foreach (RuzdContext c in contexts)
                {
                    e.addContext(c);
                }
            }
            Instance._TrackEvent(e);

            bool result = await async_result;
            return result;
        }

        public static void TrackGameEvent(string action, string category = null, string label = null, string value = null)
        {
            GameEvent e = new GameEvent(action, category: category, label: label, value: value);
            Instance._TrackEvent(e);
        }

        public static void TrackGameStart()
        {
            GameEvent e = new GameEvent("start", category: "builtin", label: "ruzd_internal");
            SystemContext systemContext = Instance.GetSystemContext();
            e.addContext(systemContext);
            Instance._TrackEvent(e);
        }

        public static void Configure(string ruzdGameId, string trackingEndpoint = null, string buildVersion = null,
                                     HttpMethod httpMethod = HttpMethod.POST, HttpProtocol httpProtocol = HttpProtocol.HTTPS,
                                     string customPath = null, bool disablePing = false)
        {
            // Check Game Id
            if (ruzdGameId.Length < 8 || ruzdGameId.Length > 32)
            {
                Log.Error("[RuzdAnalytics] Configure failed, Invalid length of ruzdGameId");
                return;
            }
            Instance.SetTrackingEndpoint(trackingEndpoint);
            Instance.SetCustomBuildVersion(buildVersion);
            Instance.customTrackingEndpoint = trackingEndpoint;
            Instance.ruzdGameId = ruzdGameId;
            Instance.httpMethod = httpMethod;
            Instance.httpProtocol = httpProtocol;
            Instance.customTrackingPath = customPath;
            Instance.disablePing = disablePing;
            Instance.OnConfigurationChanged();
        }

        public static void SetBuildVersion(string buildVersion)
        {
            Instance.SetCustomBuildVersion(buildVersion);
        }

        public static void Setup(string trackingEndpoint, string ruzdGameId, string buildVersion = null,
                                 HttpMethod httpMethod = HttpMethod.POST, HttpProtocol httpProtocol = HttpProtocol.HTTPS,
                                 string customPath = null)
        {
            Log.Warning("[RuzdAnalytics] Analytics.Setup(...) is deprecated, please use Analytics.Configure(...) instead.");
            Configure(ruzdGameId, trackingEndpoint, buildVersion, httpMethod, httpProtocol, customPath);
        }

        public static void StartTracking(bool dontSendStartEvent = false)
        {
            Instance.trackingEnabledUser = true;
            Instance.sendStartEvent = !dontSendStartEvent;
            Instance.OnTrackingStart();
        }

        public static void StopTracking()
        {
            Instance.OnTrackingStop();
        }

        public static void SetLogLevel(int logLevel)
        {
            Log.SetLogLevel(logLevel);
        }

        public static bool IsInitialized()
        {
            return Instance.trackingSetup;
        }

        public static bool IsRunning()
        {
            return Instance.trackingRunning;
        }

        public static bool CheckTrackingLevel(TrackingLevel level)
        {
            // Check if tracking is enabled for this level
            // example: if level is TrackingLevel.FINE (20) and trackingLevel is TrackingLevel.NORMAL (30)
            // then tracking is not enabled for this level
            // example: if level is TrackingLevel.CRITICAL (50) and trackingLevel is TrackingLevel.NORMAL (30)
            // then tracking is enabled for this level
            if (level >= Instance.trackingLevel)
            {
                return true;
            }
            return false;
        }

        public static bool TrackingLevelFiner()
        {
            return CheckTrackingLevel(TrackingLevel.FINER);
        }

        public static bool TrackingLevelFine()
        {
            return CheckTrackingLevel(TrackingLevel.FINE);
        }

        public static bool TrackingLevelNormal()
        {
            return CheckTrackingLevel(TrackingLevel.NORMAL);
        }

        public static bool TrackingLevelImportant()
        {
            return CheckTrackingLevel(TrackingLevel.IMPORTANT);
        }


        public static bool TrackingLevelCritical()
        {
            return CheckTrackingLevel(TrackingLevel.CRITICAL);
        }

        public static void SetPingEnabled(bool enabled)
        {
            Instance.session.SetPingEnabled(enabled);
        }

        public static void SetBackground(bool isBackground)
        {
            Instance.session.SetBackground(isBackground);
        }

        public static string GenerateRunId()
        {
            return AnalyticsUtils.generateId();
        }
    }

    public class AnalyticsUtils
    {
        public static string GetTimezone()
        {
            System.TimeZoneInfo infos = System.TimeZoneInfo.Local;
            return infos.Id;
        }

        public static Resolution GetGameResolution()
        {
            Resolution res = new Resolution();
            res.height = Screen.height;
            res.width = Screen.width;
            return res;
        }

        public static Resolution GetScreenResolution()
        {
            return Screen.currentResolution;
        }

        public static string GetTrackerName()
        {
            string unityVersion = Application.unityVersion;
            if (Application.isEditor)
            {
                return $"unity-editor-{unityVersion}";
            }
            return $"unity-{unityVersion}";
        }

        public static DevicePlatforms GetPlatform()
        {
            var platform = Application.platform;
            if (platform == RuntimePlatform.WebGLPlayer)
            {
                return DevicePlatforms.Web;
            }
            else if (platform == RuntimePlatform.IPhonePlayer || platform == RuntimePlatform.Android)
            {
                return DevicePlatforms.Mobile;
            }
            else if (platform == RuntimePlatform.PS4 || platform == RuntimePlatform.PS5 || platform == RuntimePlatform.XboxOne || platform == RuntimePlatform.Switch)
            {
                return DevicePlatforms.GameConsole;
            }
            return DevicePlatforms.Desktop;
        }

        public static string generateId()
        {
            return Utils.GetGUID();
        }
    }

    public class GameRun
    {
        public string runId;
        public long playTimeSeconds;

        public GameRun(string runId, long playTimeSeconds)
        {
            this.runId = runId;
            this.playTimeSeconds = playTimeSeconds;
        }

        public void update(string runId, long playTimeSeconds)
        {
            if (runId == this.runId && playTimeSeconds < this.playTimeSeconds)
            {
                Log.Warning("[RuzdAnalytics] The current run was updated but the playTime decreased.");
            }
            this.runId = runId;
            this.playTimeSeconds = playTimeSeconds;
        }

        public RunContext GetContext()
        {
            RunContext c = new RunContext(runId, playTimeSeconds);
            return c;
        }
    }

    public class FPSTracker
    {
        int frameCounter = 0;
        float timeCounter = 0.0f;
        float lastFramerate = 0.0f;
        float refreshTime;
        public int sendIntervalSeconds;
        bool trackingEnabled;
        long lastEventTime;

        public FPSTracker(float refreshTime, int sendIntervalSeconds = 180, bool sendEvents = true)
        {
            // Check if refreshTime is between 0.1 and 10 seconds
            if (refreshTime < 0.1f)
            {
                this.refreshTime = 0.1f;
            }
            else if (refreshTime > 10.0f)
            {
                this.refreshTime = 10.0f;
            }
            else
            {
                this.refreshTime = refreshTime;
            }
            this.sendIntervalSeconds = sendIntervalSeconds;
            this.trackingEnabled = sendEvents;
            this.lastEventTime = Utils.GetTimestamp();
        }

        public void tick(int frameCount, float timePassed)
        {
            if (timeCounter < refreshTime)
            {
                timeCounter += timePassed;
                frameCounter += frameCount;
            }
            else if (timeCounter > 0.0f && frameCounter > 0)
            {
                lastFramerate = (float)frameCounter / timeCounter;
                frameCounter = 0;
                timeCounter = 0.0f;
                Debug.Log($"Framerate: {lastFramerate}");
            }

            if (!trackingEnabled)
            {
                return;
            }

            var currentTime = Utils.GetTimestamp();
            if (!Utils.IsTimeInRange(lastEventTime, currentTime, sendIntervalSeconds*1000) && lastFramerate > 0.0f)
            {
                Log.Debug($"Framerate Event: {lastFramerate}");
                Analytics.Instance.TrackFPS((int)lastFramerate);
            }
        }

        public void OnFrame()
        {
            tick(1, Time.deltaTime);
        }

        public float GetFPS()
        {
            return lastFramerate;
        }
    }

    public enum ErrorSeverity
    {
        WARNING = 30,
        ERROR = 40,
        CRITICAL = 50
    }

    public enum LogLevel
    {
        DEBUG = 10,
        INFO = 20,
        WARNING = 30,
        ERROR = 40,
        CRITICAL = 50
    }

    public enum TrackingLevel
    {
        NOTSET = 0,
        FINER = 10,
        FINE = 20,
        NORMAL = 30,
        IMPORTANT = 40,
        CRITICAL = 50
    }
}