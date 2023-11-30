using System.Collections.Generic;
using UnityEngine;
using SnowplowTracker;
using SnowplowTracker.Emitters;
using SnowplowTracker.Events;
using SnowplowTracker.Enums;
using SnowplowTracker.Storage;

namespace RuzdAnalytics
{
    public class Analytics : MonoBehaviour
    {
        private static Analytics _instance;
        private IEmitter emitter;
        private IStore store;
        private Tracker tracker;
        private Subject subject;
        private Session session;
        private SystemContext sysContext;
        private bool trackingRunning = false;
        private bool trackingSetup = false;
        private long lastFPSEvent;
        private int FPS_MIN_INTERVAL_SECONDS = 60;
        private readonly string POST_SUFFIX = "/com.ruzd/tp2";
        private readonly string GET_SUFFIX = "/i";
        private string defaultPlayerId;

        public HttpMethod httpMethod = HttpMethod.POST;
        public HttpProtocol httpProtocol = HttpProtocol.HTTPS;
        public string trackingPath;
        public string trackingEndpoint;
        public string ruzdGameId;
        public string customPlayerId;
        public string customVersion;

        private static readonly int MAX_FEEDBACK_LENGTH = 512;
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
                Log.Warning("[RuzdAnalytics] Configuration changed after initial setup. Please make sure Analytics.Setup is only called once.");
            }

            // Setup Emitter
            emitter.SetHttpMethod(httpMethod);
            emitter.SetHttpProtocol(httpProtocol);

            if(!string.IsNullOrEmpty(trackingPath))
            {

                emitter.SetCollectorUri($"{trackingEndpoint}{trackingPath}");
                Log.Debug($"[RuzdAnalytics] Using custom tracking path: {trackingEndpoint}{trackingPath}");
            }
            else
            {
                if (httpMethod == HttpMethod.GET)
                {
                    emitter.SetCollectorUri($"{trackingEndpoint}{GET_SUFFIX}");
                }
                else if (httpMethod == HttpMethod.POST)
                {
                    emitter.SetCollectorUri($"{trackingEndpoint}{POST_SUFFIX}");
                }
            }
            subject = new Subject();
            var screenRes = AnalyticsUtils.GetScreenResolution();
            var windowRes = AnalyticsUtils.GetGameResolution();
            subject.SetScreenResolution(screenRes.width, screenRes.height);
            subject.SetViewPort(windowRes.width, windowRes.height);
            subject.SetUserId(GetPlayerId());
            // subject.SetTimezone(AnalyticsUtils.GetTimezone());
            session = new Session("ruzd_game_session", foregroundTimeout: 1800, backgroundTimeout: 1800, checkInterval: 30, customUserId: GetPlayerId());
            tracker = new Tracker(emitter, GetBuildVersion(), ruzdGameId, subject: subject, session: session, platform: AnalyticsUtils.GetPlatform());         
            trackingSetup = true;
        }

        public void OnTrackingStart()
        {
            if (!trackingSetup)
            {
                Log.Warning("[RuzdAnalytics] Run Analytics.Setup() with valid configuration before starting event tracking.");
                return;
            }
            tracker.StartEventTracking();
            trackingRunning = true;
            Log.Debug("[RuzdAnalytics] Tracking started");
        }

        public void OnTrackingStop()
        {
            if (tracker != null)
                tracker.StopEventTracking();
            trackingSetup = false;
            trackingRunning = false;
            Log.Debug("[RuzdAnalytics] Tracking stopped");
        }

        string GetPlayerId()
        {
            if (!string.IsNullOrEmpty(customPlayerId))
            {
                return customPlayerId;
            }
            return defaultPlayerId;
        }

        string GetBuildVersion()
        {
            if (!string.IsNullOrEmpty(customVersion))
                return customVersion;
            return Application.version;
        }

        public SystemContext GetSystemContext()
        {
            return sysContext;
        }

        public void _TrackEvent(RuzdEvent newEvent)
        {
            if (!trackingSetup)
            {
                Log.Warning("[RuzdAnalytics] Run Analytics.Setup() with valid configuration before tracking Events.");
                return;
            }
            if (!trackingRunning)
            {
                Log.Warning("[RuzdAnalytics] Event will not be recorded because event tracking was not started.");
                return;
            }

            Log.Debug($"[RuzdAnalytics] Tracking event: {newEvent.GetType()}");
            SelfDescribing spEvent = newEvent.GetSPEvent();
            tracker.Track(spEvent.Build());
        }

        public void setBuildVersion(string buildVersion)
        {
            customVersion = buildVersion;
        }

        public void TrackFPS(double averageFPS)
        {
            long checkTime = Utils.GetTimestamp();
            if (Utils.IsTimeInRange(lastFPSEvent, checkTime, FPS_MIN_INTERVAL_SECONDS*1000))
            {
                return;
            }
            GameEvent fpsEvent = new GameEvent("fps", category: "fps",
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

        public static void sendFeedback(int rating, string message = null, List<RuzdContext> contexts = null)
        {
            
            if (message != null && message.Length > MAX_FEEDBACK_LENGTH)
            {
                message = message.Substring(0, MAX_FEEDBACK_LENGTH);
            }


            // todo send feedback directly to API


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

        public static void Setup(string trackingEndpoint, string ruzdGameId, string buildVersion = null,
                                 HttpMethod httpMethod = HttpMethod.POST, HttpProtocol httpProtocol = HttpProtocol.HTTPS,
                                 string customPath = null)
        {
            // Check Game Id
            if (ruzdGameId.Length < 8 || ruzdGameId.Length > 32)
            {
                Log.Error("[RuzdAnalytics] Setup failed, Invalid length of ruzdGameId");
                return;
            }
            // Check Tracking Endpoint (should not end with /)
            if (trackingEndpoint.EndsWith("/"))
            {
                trackingEndpoint = trackingEndpoint.Substring(0, trackingEndpoint.Length - 1);
            }
            if (!string.IsNullOrEmpty(buildVersion))
            {
                Instance.setBuildVersion(buildVersion);
            }
            Instance.trackingEndpoint = trackingEndpoint;
            Instance.ruzdGameId = ruzdGameId;
            Instance.httpMethod = httpMethod;
            Instance.httpProtocol = httpProtocol;
            if (!string.IsNullOrEmpty(customPath))
            {
                Instance.trackingPath = customPath;
            }
            Instance.OnConfigurationChanged();
        }

        public static void StartTracking(bool dontSendStartEvent = false)
        {
            Instance.OnTrackingStart();
            if (!dontSendStartEvent)
            {
                TrackGameStart();
            }
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