using System;
using System.Collections;
using System.Text;
using Drift.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Game
{
    /// <summary>
    /// Composition root. The Main scene holds one GameObject with this component; everything
    /// else (track, car, ghost, camera, UI) is built here at load and wired together.
    /// </summary>
    public sealed class DriftGame : MonoBehaviour
    {
        public const string ClientVersion = "0.1.0";

        [Serializable]
        private class ClientSettings
        {
            public string api_base_url = "http://localhost:8000";
            public string track = "harbor";
        }

        private enum GhostMode { Mine, Top, Off }

        [Header("Performance experiments: flip one, measure, log before/after")]
        [Tooltip("Combine the scenery into static batches at load.")]
        public bool StaticBatchScenery = true;
        [Tooltip("Enable GPU instancing on the shared materials.")]
        public bool GpuInstancing = true;
        [Tooltip("Realtime shadows from the sun (expensive on mobile).")]
        public bool RealtimeShadows = false;
        [Tooltip("Spawn trees, lamps and cones.")]
        public bool SpawnScenery = true;
        [Tooltip("Show the FPS / frame time / GC / draw call overlay.")]
        public bool ShowPerfOverlay = true;

        private const string NameKey = "drift_player_name";
        private const string ServerKey = "drift_server_url";
        private static readonly string[] LapToasts = { "LAP 1", "LAP 2", "LAP 3", "LAP 4", "LAP 5", "LAP 6", "LAP 7", "LAP 8", "LAP 9" };

        private ClientSettings _settings;
        private RemoteConfig _config;
        private TuningValues _tuning;
        private ApiClient _api;
        private TrackRuntime _track;
        private CarController _car;
        private GhostPlayer _ghost;
        private CameraRig _camRig;
        private InputReader _input;
        private Hud _hud;
        private RaceManager _race;
        private GhostBuffer _mineGhost;
        private GhostBuffer _topGhost;
        private string _topGhostLabel;
        private GhostMode _ghostMode = GhostMode.Mine;
        private bool _paused;

        private string TrackId { get { return _track.Definition.id; } }

        private void Awake()
        {
            // Mobile builds can otherwise sit at 30 fps; vsync off so targetFrameRate rules.
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Time.fixedDeltaTime = RaceManager.StepSeconds; // ghost format and lap timing assume 50 Hz
            Physics.reuseCollisionCallbacks = true;          // no Collision garbage per contact
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            _settings = LoadClientSettings();
            _config = new RemoteConfig();
            _tuning = _config.Current;

            TrackDefinition def = LoadTrack(_settings.track);
            _track = TrackBuilder.Build(def, SpawnScenery);
            ApplyPerfOptions();
            BuildLighting();

            _car = BuildCar();
            _ghost = BuildGhost();
            _camRig = BuildCamera();
            _input = gameObject.AddComponent<InputReader>();

            var uiRoot = new GameObject("UI");
            _hud = uiRoot.AddComponent<Hud>();
            _hud.Build(def.laps, LoadPlayerName(), PlayerPrefs.GetString(ServerKey, _settings.api_base_url));
            _input.Bind(_hud.Left, _hud.Right, _hud.DriftButton, _hud.NitroButton);
            if (ShowPerfOverlay)
            {
                Canvas perfCanvas = UiFactory.CreateCanvas("PerfCanvas", 10);
                perfCanvas.transform.SetParent(uiRoot.transform, false);
                perfCanvas.gameObject.AddComponent<PerfOverlay>().Build(perfCanvas.transform);
            }

            _race = gameObject.AddComponent<RaceManager>();
            _race.Init(_car, _input, _ghost, _track, _tuning);
            _race.PersonalBestMs = GhostStore.PersonalBestMs(TrackId);
            _race.LapCompleted += OnLapCompleted;
            _race.RaceFinished += OnRaceFinished;
            _race.Go += () => _hud.Toast(LapToasts[0], 1.2f);

            _hud.OnRace = StartRace;
            _hud.OnRetry = StartRace;
            _hud.OnRestart = StartRace;
            _hud.OnMenu = BackToMenu;
            _hud.OnPause = Pause;
            _hud.OnResume = Resume;
            _hud.OnGhostCycle = CycleGhost;

            int capacity = Mathf.CeilToInt(120f * GhostBuffer.DefaultSampleRate) + 1;
            _mineGhost = new GhostBuffer(capacity);
            _topGhost = new GhostBuffer(capacity);
            if (!GhostStore.TryLoad(TrackId, _mineGhost)) _mineGhost.Clear();
            ApplyGhostMode();

            _api = new ApiClient(_hud.ServerUrl);
            _race.Stop();
            _camRig.Snap();
            _hud.SetMenuBest(_race.PersonalBestMs);
            _hud.SetMenuStatus("Tuning: " + _config.Source);
        }

        private void Start()
        {
            StartCoroutine(RefreshOnline());
        }

        private void Update()
        {
            _hud.Tick(_race, _tuning);
            if (_input.ConsumePause())
            {
                if (_paused) Resume();
                else Pause();
            }
        }

        // ---------------------------------------------------------------- flow

        private void StartRace()
        {
            SavePrefs();
            _paused = false;
            Time.timeScale = 1f;
            _api.BaseUrl = _hud.ServerUrl;

            // Remote config only changes between races.
            _tuning = _config.Current;
            _race.ApplyTuning(_tuning);
            _camRig.SetTuning(_tuning);

            ApplyGhostMode();
            _hud.ShowRace();
            _race.StartCountdown();
            _camRig.Snap();
        }

        private void BackToMenu()
        {
            _paused = false;
            Time.timeScale = 1f;
            _race.Stop();
            _camRig.Snap();
            _hud.ShowMenu();
            _hud.SetMenuBest(_race.PersonalBestMs);
            StartCoroutine(RefreshOnline());
        }

        private void Pause()
        {
            if (_race.State != RaceState.Racing && _race.State != RaceState.Countdown) return;
            _paused = true;
            Time.timeScale = 0f;
            _hud.ShowPause(true);
            UpdateGhostLabel();
        }

        private void Resume()
        {
            if (!_paused) return;
            _paused = false;
            Time.timeScale = 1f;
            _hud.ShowPause(false);
            _input.ClearLatches();
        }

        private void OnLapCompleted(int lapIndex, int lapMs, bool bestOfRun)
        {
            int completed = lapIndex + 1;
            if (completed >= _race.TotalLaps) return;
            bool pb = _race.PersonalBestMs <= 0 || lapMs < _race.PersonalBestMs;
            if ((lapIndex > 0 && bestOfRun) || (pb && _race.PersonalBestMs > 0)) _hud.Toast("BEST LAP", 1.5f);
            else if (completed == _race.TotalLaps - 1) _hud.Toast("FINAL LAP", 1.5f);
            else _hud.Toast(LapToasts[Mathf.Min(completed, LapToasts.Length - 1)], 1.2f);
        }

        private void OnRaceFinished(RaceResult result)
        {
            _hud.ShowResults(result, _track.Definition.name);

            if (result.NewPersonalBest && result.BestLapGhost != null)
            {
                GhostStore.Save(TrackId, result.BestLapGhost, result.BestLapMs);
                if (!GhostCodec.TryDecode(result.BestLapGhost, _mineGhost, out string _)) _mineGhost.Clear();
                _race.PersonalBestMs = result.BestLapMs;
            }

            if (string.IsNullOrEmpty(_api.BaseUrl))
                _hud.SetResultsRank("Offline: your best lap is saved on this device.");
            else
                StartCoroutine(Submit(result));
        }

        private IEnumerator Submit(RaceResult result)
        {
            if (result.BestLapGhost == null)
            {
                _hud.SetResultsRank("No lap recorded, nothing to submit.");
                yield break;
            }
            var run = new RunSubmission
            {
                player = SanitizeName(_hud.PlayerName),
                track = TrackId,
                lap_times_ms = result.LapMs,
                ghost = Convert.ToBase64String(result.BestLapGhost),
                client_version = ClientVersion,
            };
            yield return _api.SubmitRun(run,
                r =>
                {
                    if (!r.validated) _hud.SetResultsRank("Run not accepted by the server: " + r.reason);
                    else if (r.rank > 0) _hud.SetResultsRank("Leaderboard: #" + r.rank + " of " + r.total + (r.personal_best ? "  (new server best)" : string.Empty));
                    else _hud.SetResultsRank("Run saved.");
                },
                e => _hud.SetResultsRank("Offline: your best lap is saved on this device."));
        }

        private IEnumerator RefreshOnline()
        {
            _api.BaseUrl = _hud.ServerUrl;
            if (string.IsNullOrEmpty(_api.BaseUrl))
            {
                _hud.SetLeaderboard("Offline\n\nAdd a server address to see the leaderboard.");
                yield break;
            }
            yield return _config.Refresh(_api, t => _hud.SetMenuStatus("Tuning: " + _config.Source));
            yield return _api.GetLeaderboard(TrackId, 8, SanitizeName(_hud.PlayerName),
                board => _hud.SetLeaderboard(FormatBoard(board)),
                e => _hud.SetLeaderboard("Offline\n\nServer not reachable.\nGhosts and laps still work."));
        }

        private static string FormatBoard(LeaderboardResponse board)
        {
            if (board.entries == null || board.entries.Length == 0) return "No laps yet. Be the first.";
            var sb = new StringBuilder();
            foreach (LeaderboardEntry e in board.entries)
                sb.Append(e.rank.ToString().PadLeft(2)).Append(".  ").Append(Hud.FormatTime(e.best_lap_ms)).Append("   ").Append(e.player).Append('\n');
            if (board.you != null && board.you.rank > board.entries.Length)
                sb.Append("\n").Append(board.you.rank).Append(".  ").Append(Hud.FormatTime(board.you.best_lap_ms)).Append("   you");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- ghosts

        private void CycleGhost()
        {
            _ghostMode = _ghostMode == GhostMode.Mine ? GhostMode.Top : _ghostMode == GhostMode.Top ? GhostMode.Off : GhostMode.Mine;
            ApplyGhostMode();
            if (_ghostMode == GhostMode.Top && _topGhost.Count == 0) StartCoroutine(FetchTopGhost());
        }

        private void ApplyGhostMode()
        {
            switch (_ghostMode)
            {
                case GhostMode.Mine:
                    _ghost.SetGhost(_mineGhost);
                    _ghost.Visible = true;
                    break;
                case GhostMode.Top:
                    _ghost.SetGhost(_topGhost);
                    _ghost.Visible = true;
                    break;
                default:
                    _ghost.Visible = false;
                    break;
            }
            UpdateGhostLabel();
        }

        private void UpdateGhostLabel()
        {
            switch (_ghostMode)
            {
                case GhostMode.Mine:
                    _hud.SetGhostMode("MINE", _mineGhost.Count > 1 ? "Racing your best lap" : "No ghost yet: finish a lap to record one");
                    break;
                case GhostMode.Top:
                    _hud.SetGhostMode("TOP", _topGhost.Count > 1 ? "Racing " + _topGhostLabel : "Fetching the leaderboard's top lap...");
                    break;
                default:
                    _hud.SetGhostMode("OFF", "Ghost hidden");
                    break;
            }
        }

        private IEnumerator FetchTopGhost()
        {
            string runId = null;
            yield return _api.GetLeaderboard(TrackId, 1, null, board =>
            {
                if (board.entries != null && board.entries.Length > 0)
                {
                    runId = board.entries[0].run_id;
                    _topGhostLabel = board.entries[0].player + " (" + Hud.FormatTime(board.entries[0].best_lap_ms) + ")";
                }
            }, e => { });

            if (runId == null)
            {
                _hud.SetGhostMode("TOP", "Top ghost unavailable (offline or no laps yet)");
                yield break;
            }
            yield return _api.GetGhost(runId, bytes =>
            {
                if (!GhostCodec.TryDecode(bytes, _topGhost, out string error))
                {
                    _topGhost.Clear();
                    Debug.LogWarning("Top ghost rejected: " + error);
                }
            }, e => { });
            if (_ghostMode == GhostMode.Top) ApplyGhostMode();
        }

        // ---------------------------------------------------------------- building

        private CarController BuildCar()
        {
            var go = new GameObject("Car");
            go.AddComponent<Rigidbody>();
            var car = go.AddComponent<CarController>();
            CarVisual visual = CarBuilder.Build(go.transform, false);
            car.Init(_tuning, visual);
            car.Teleport(_track.SpawnPosition, _track.SpawnRotation);
            return car;
        }

        private GhostPlayer BuildGhost()
        {
            var go = new GameObject("Ghost");
            var ghost = go.AddComponent<GhostPlayer>();
            CarVisual visual = CarBuilder.Build(go.transform, true);
            ghost.Init(visual.gameObject);
            return ghost;
        }

        private CameraRig BuildCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                go.tag = "MainCamera";
                cam = go.GetComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 600f;
            var rig = cam.gameObject.AddComponent<CameraRig>();
            rig.Bind(cam, _car, _tuning);
            return rig;
        }

        private void BuildLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.15f;
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            sun.shadows = RealtimeShadows ? LightShadows.Soft : LightShadows.None;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.86f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.55f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.30f, 0.25f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.55f, 0.75f, 0.95f);
            RenderSettings.fogStartDistance = 160f;
            RenderSettings.fogEndDistance = 520f;
        }

        private void ApplyPerfOptions()
        {
            MaterialLibrary.SetInstancing(GpuInstancing);
            if (StaticBatchScenery && _track.Props != null) StaticBatchingUtility.Combine(_track.Props);
        }

        // ---------------------------------------------------------------- settings

        private static ClientSettings LoadClientSettings()
        {
            var settings = new ClientSettings();
            var asset = Resources.Load<TextAsset>("drift_client");
            if (asset != null) JsonUtility.FromJsonOverwrite(asset.text, settings);
            return settings;
        }

        private static TrackDefinition LoadTrack(string id)
        {
            var asset = Resources.Load<TextAsset>("Tracks/" + id);
            if (asset == null) throw new InvalidOperationException("Track '" + id + "' not found in Resources/Tracks.");
            return JsonUtility.FromJson<TrackDefinition>(asset.text);
        }

        private static string LoadPlayerName()
        {
            string name = PlayerPrefs.GetString(NameKey, string.Empty);
            if (string.IsNullOrEmpty(name)) name = "Racer" + UnityEngine.Random.Range(1000, 9999);
            return name;
        }

        private void SavePrefs()
        {
            string name = SanitizeName(_hud.PlayerName);
            PlayerPrefs.SetString(NameKey, name);
            PlayerPrefs.SetString(ServerKey, _hud.ServerUrl);
            PlayerPrefs.Save();
        }

        /// <summary>ASCII letters, digits, space, '-' and '_' only, 1 to 16 characters (the server enforces the same).</summary>
        public static string SanitizeName(string raw)
        {
            var sb = new StringBuilder();
            foreach (char c in raw ?? string.Empty)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ' || c == '-' || c == '_') sb.Append(c);
                if (sb.Length == 16) break;
            }
            string s = sb.ToString().Trim();
            return s.Length == 0 ? "Racer" : s;
        }
    }
}
