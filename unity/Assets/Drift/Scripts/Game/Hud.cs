using System;
using System.Text;
using Drift.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Game
{
    /// <summary>
    /// All screens, built in code: menu, race HUD with touch controls, pause, results.
    /// The race HUD updates every frame without allocating: numbers go through NumberLabel
    /// and every other string it shows is a constant.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        private static readonly string[] CountdownText = { "GO!", "1", "2", "3" };

        public Action OnRace, OnRetry, OnMenu, OnPause, OnResume, OnRestart, OnGhostCycle;

        public HoldButton Left { get; private set; }
        public HoldButton Right { get; private set; }
        public HoldButton DriftButton { get; private set; }
        public HoldButton NitroButton { get; private set; }

        private RectTransform _race, _touch, _menu, _pause, _results;

        // Race HUD
        private NumberLabel _lap, _lapTime, _bestTime, _speed, _combo, _multiplier;
        private Text _countdown, _toast, _comboTitle, _bestCaption;
        private RectTransform _nitroFill;
        private Image _nitroFillImage;
        private float _toastUntil;
        private float _goUntil;
        private float _lastNitro = -1f;
        private bool _lastBoosting;

        // Menu
        private InputField _name, _server;
        private Text _menuStatus, _menuBoard, _menuBest;

        // Pause and results
        private Text _ghostLabel, _pauseGhostLabel;
        private Text _resultsTitle, _resultsBody, _resultsRank;

        public string PlayerName { get { return _name.text.Trim(); } }
        public string ServerUrl { get { return _server.text.Trim(); } }

        public void Build(int totalLaps, string playerName, string serverUrl)
        {
            UiFactory.EnsureEventSystem();
            Canvas canvas = UiFactory.CreateCanvas("HUD", 0);
            canvas.transform.SetParent(transform, false);
            Transform root = canvas.transform;

            BuildRaceHud(root, totalLaps);
            BuildTouchControls(root);
            BuildMenu(root, playerName, serverUrl);
            BuildPause(root);
            BuildResults(root);
            ShowMenu();
        }

        // ---------------------------------------------------------------- building

        private void BuildRaceHud(Transform root, int totalLaps)
        {
            _race = UiFactory.Fill(root, "Race");
            Color c = UiFactory.TextColor;

            // Top left: lap counter, lap time, best lap.
            Image box = UiFactory.CreateImage(_race, "LapBox", UiFactory.Panel);
            UiFactory.Place(box.rectTransform, TextAnchor.UpperLeft, new Vector2(36f, -30f), new Vector2(430f, 210f));
            UiFactory.Label(box.rectTransform, "LAP", 30, UiFactory.Muted, TextAnchor.UpperLeft, new Vector2(26f, -18f), new Vector2(120f, 40f), TextAnchor.MiddleLeft);
            _lap = new NumberLabel(box.rectTransform, "0", 54, c, TextAnchor.UpperLeft);
            _lap.Root.anchoredPosition = new Vector2(100f, -6f);
            UiFactory.Label(box.rectTransform, "/" + totalLaps, 36, UiFactory.Muted, TextAnchor.UpperLeft, new Vector2(140f, -18f), new Vector2(80f, 44f), TextAnchor.MiddleLeft);
            _lapTime = new NumberLabel(box.rectTransform, "0:00.00", 60, c, TextAnchor.UpperLeft);
            _lapTime.Root.anchoredPosition = new Vector2(22f, -78f);
            _bestCaption = UiFactory.Label(box.rectTransform, "BEST", 26, UiFactory.Muted, TextAnchor.UpperLeft, new Vector2(26f, -158f), new Vector2(90f, 36f), TextAnchor.MiddleLeft);
            _bestTime = new NumberLabel(box.rectTransform, "0:00.00", 34, UiFactory.Accent, TextAnchor.UpperLeft);
            _bestTime.Root.anchoredPosition = new Vector2(112f, -154f);

            // Top right: speed.
            Image speedBox = UiFactory.CreateImage(_race, "SpeedBox", UiFactory.Panel);
            UiFactory.Place(speedBox.rectTransform, TextAnchor.UpperRight, new Vector2(-36f, -30f), new Vector2(300f, 130f));
            _speed = new NumberLabel(speedBox.rectTransform, "##0", 76, c, TextAnchor.MiddleLeft);
            _speed.Root.anchoredPosition = new Vector2(24f, 0f);
            UiFactory.Label(speedBox.rectTransform, "km/h", 30, UiFactory.Muted, TextAnchor.MiddleRight, new Vector2(-22f, -14f), new Vector2(100f, 40f), TextAnchor.MiddleRight);

            // Pause button, top centre.
            Button pause = UiFactory.CreateButton(_race, "II", 44, new Vector2(110f, 90f), () => { if (OnPause != null) OnPause(); });
            UiFactory.Place((RectTransform)pause.transform, TextAnchor.UpperCenter, new Vector2(0f, -30f), new Vector2(110f, 90f));
            pause.GetComponent<Image>().color = UiFactory.ButtonColor;
            pause.GetComponentInChildren<Text>().color = c;

            // Centre: countdown, toasts and the drift combo.
            _countdown = UiFactory.Label(_race, "3", 220, c, TextAnchor.MiddleCenter, new Vector2(0f, 120f), new Vector2(600f, 260f), TextAnchor.MiddleCenter);
            _toast = UiFactory.Label(_race, string.Empty, 64, UiFactory.Accent, TextAnchor.UpperCenter, new Vector2(0f, -170f), new Vector2(900f, 90f), TextAnchor.MiddleCenter);
            _comboTitle = UiFactory.Label(_race, "DRIFT", 38, UiFactory.Muted, TextAnchor.MiddleCenter, new Vector2(0f, 300f), new Vector2(300f, 50f), TextAnchor.MiddleCenter);
            _combo = new NumberLabel(_race, "#####0", 84, UiFactory.Accent, TextAnchor.MiddleCenter);
            _combo.Root.anchoredPosition = new Vector2(-40f, 230f);
            _multiplier = new NumberLabel(_race, "x0", 56, c, TextAnchor.MiddleCenter);
            _multiplier.Root.anchoredPosition = new Vector2(210f, 230f);
        }

        private void BuildTouchControls(Transform root)
        {
            _touch = UiFactory.Fill(root, "Touch");
            Color pad = new Color(1f, 1f, 1f, 0.13f);
            Left = UiFactory.TouchPad(_touch, "<", 110, TextAnchor.LowerLeft, new Vector2(40f, 40f), new Vector2(250f, 250f), pad);
            Right = UiFactory.TouchPad(_touch, ">", 110, TextAnchor.LowerLeft, new Vector2(320f, 40f), new Vector2(250f, 250f), pad);
            DriftButton = UiFactory.TouchPad(_touch, "DRIFT", 54, TextAnchor.LowerRight, new Vector2(-40f, 40f), new Vector2(330f, 250f), new Color(1f, 0.55f, 0.12f, 0.30f));
            NitroButton = UiFactory.TouchPad(_touch, "NITRO", 40, TextAnchor.LowerRight, new Vector2(-400f, 40f), new Vector2(210f, 170f), new Color(0.25f, 0.75f, 1f, 0.30f));

            // Nitro meter above the buttons.
            Image back = UiFactory.CreateImage(_touch, "NitroBack", UiFactory.Panel);
            UiFactory.Place(back.rectTransform, TextAnchor.LowerRight, new Vector2(-40f, 310f), new Vector2(570f, 34f));
            _nitroFillImage = UiFactory.CreateImage(back.rectTransform, "NitroFill", UiFactory.Nitro);
            _nitroFill = _nitroFillImage.rectTransform;
            _nitroFill.anchorMin = Vector2.zero;
            _nitroFill.anchorMax = new Vector2(0f, 1f);
            _nitroFill.offsetMin = new Vector2(4f, 4f);
            _nitroFill.offsetMax = new Vector2(-4f, -4f);
            UiFactory.Label(back.rectTransform, "NITRO", 22, UiFactory.TextColor, TextAnchor.MiddleLeft, new Vector2(12f, 0f), new Vector2(120f, 30f), TextAnchor.MiddleLeft);
        }

        private void BuildMenu(Transform root, string playerName, string serverUrl)
        {
            _menu = UiFactory.Fill(root, "Menu");
            UiFactory.CreateImage(_menu, "Shade", new Color(0.03f, 0.04f, 0.07f, 0.55f)).rectTransform.Fill();

            UiFactory.Label(_menu, "DRIFT", 170, UiFactory.Accent, TextAnchor.UpperCenter, new Vector2(0f, -70f), new Vector2(900f, 200f), TextAnchor.MiddleCenter);
            UiFactory.Label(_menu, "Hold DRIFT through corners to fill nitro. Tap NITRO on the straights.", 32, UiFactory.Muted,
                            TextAnchor.UpperCenter, new Vector2(0f, -265f), new Vector2(1400f, 50f), TextAnchor.MiddleCenter);

            RectTransform col = UiFactory.Column(_menu, "Form", 22f);
            UiFactory.Place(col, TextAnchor.MiddleLeft, new Vector2(220f, -80f), new Vector2(640f, 460f));
            UiFactory.Label(col, "Player name", 28, UiFactory.Muted, TextAnchor.MiddleLeft).rectTransform.sizeDelta = new Vector2(620f, 36f);
            _name = UiFactory.CreateInputField(col, "Your name", playerName, 38, new Vector2(620f, 84f));
            _name.characterLimit = 16;
            UiFactory.Label(col, "Server (optional)", 28, UiFactory.Muted, TextAnchor.MiddleLeft).rectTransform.sizeDelta = new Vector2(620f, 36f);
            _server = UiFactory.CreateInputField(col, "http://192.168.1.10:8000", serverUrl, 30, new Vector2(620f, 70f));
            Button race = UiFactory.CreateButton(col, "RACE", 60, new Vector2(620f, 120f), () => { if (OnRace != null) OnRace(); });
            race.name = "RaceButton";
            _menuStatus = UiFactory.Label(col, string.Empty, 24, UiFactory.Muted, TextAnchor.MiddleLeft);
            _menuStatus.rectTransform.sizeDelta = new Vector2(620f, 60f);
            _menuStatus.horizontalOverflow = HorizontalWrapMode.Wrap;

            Image board = UiFactory.CreateImage(_menu, "Board", UiFactory.Panel);
            UiFactory.Place(board.rectTransform, TextAnchor.MiddleRight, new Vector2(-220f, -80f), new Vector2(640f, 460f));
            UiFactory.Label(board.rectTransform, "LEADERBOARD", 34, UiFactory.Accent, TextAnchor.UpperCenter, new Vector2(0f, -22f), new Vector2(600f, 50f), TextAnchor.MiddleCenter);
            _menuBoard = UiFactory.Label(board.rectTransform, "Offline", 30, UiFactory.TextColor, TextAnchor.UpperCenter, new Vector2(0f, -90f), new Vector2(560f, 280f), TextAnchor.UpperLeft);
            _menuBest = UiFactory.Label(board.rectTransform, string.Empty, 28, UiFactory.Muted, TextAnchor.LowerCenter, new Vector2(0f, 22f), new Vector2(560f, 44f), TextAnchor.MiddleCenter);
        }

        private void BuildPause(Transform root)
        {
            _pause = UiFactory.Fill(root, "Pause");
            UiFactory.CreateImage(_pause, "Shade", new Color(0.02f, 0.03f, 0.05f, 0.78f)).rectTransform.Fill();
            UiFactory.Label(_pause, "PAUSED", 100, UiFactory.TextColor, TextAnchor.UpperCenter, new Vector2(0f, -110f), new Vector2(800f, 130f), TextAnchor.MiddleCenter);
            RectTransform col = UiFactory.Column(_pause, "Buttons", 26f);
            UiFactory.Place(col, TextAnchor.MiddleCenter, new Vector2(0f, -60f), new Vector2(600f, 560f));
            UiFactory.CreateButton(col, "RESUME", 48, new Vector2(560f, 110f), () => { if (OnResume != null) OnResume(); });
            UiFactory.CreateButton(col, "RESTART", 48, new Vector2(560f, 110f), () => { if (OnRestart != null) OnRestart(); });
            Button ghost = UiFactory.CreateButton(col, "GHOST: MINE", 42, new Vector2(560f, 110f), () => { if (OnGhostCycle != null) OnGhostCycle(); });
            _pauseGhostLabel = ghost.GetComponentInChildren<Text>();
            UiFactory.CreateButton(col, "MENU", 48, new Vector2(560f, 110f), () => { if (OnMenu != null) OnMenu(); });
            _ghostLabel = UiFactory.Label(_pause, string.Empty, 26, UiFactory.Muted, TextAnchor.LowerCenter, new Vector2(0f, 40f), new Vector2(1200f, 40f), TextAnchor.MiddleCenter);
        }

        private void BuildResults(Transform root)
        {
            _results = UiFactory.Fill(root, "Results");
            UiFactory.CreateImage(_results, "Shade", new Color(0.02f, 0.03f, 0.05f, 0.80f)).rectTransform.Fill();
            _resultsTitle = UiFactory.Label(_results, "FINISH", 110, UiFactory.Accent, TextAnchor.UpperCenter, new Vector2(0f, -60f), new Vector2(1200f, 140f), TextAnchor.MiddleCenter);
            _resultsBody = UiFactory.Label(_results, string.Empty, 40, UiFactory.TextColor, TextAnchor.MiddleCenter, new Vector2(0f, 40f), new Vector2(900f, 460f), TextAnchor.MiddleLeft);
            _resultsRank = UiFactory.Label(_results, string.Empty, 36, UiFactory.Nitro, TextAnchor.LowerCenter, new Vector2(0f, 230f), new Vector2(1400f, 60f), TextAnchor.MiddleCenter);
            Button retry = UiFactory.CreateButton(_results, "RETRY", 56, new Vector2(420f, 120f), () => { if (OnRetry != null) OnRetry(); });
            UiFactory.Place((RectTransform)retry.transform, TextAnchor.LowerCenter, new Vector2(-230f, 70f), new Vector2(420f, 120f));
            Button menu = UiFactory.CreateButton(_results, "MENU", 56, new Vector2(420f, 120f), () => { if (OnMenu != null) OnMenu(); });
            UiFactory.Place((RectTransform)menu.transform, TextAnchor.LowerCenter, new Vector2(230f, 70f), new Vector2(420f, 120f));
            menu.GetComponent<Image>().color = UiFactory.ButtonColor;
            menu.GetComponentInChildren<Text>().color = UiFactory.TextColor;
        }

        // ---------------------------------------------------------------- screens

        public void ShowMenu()
        {
            SetScreens(menu: true, race: false, pause: false, results: false);
        }

        public void ShowRace()
        {
            SetScreens(menu: false, race: true, pause: false, results: false);
            _toast.text = string.Empty;
            _toastUntil = 0f;
            _goUntil = 0f;
        }

        public void ShowPause(bool paused)
        {
            _pause.gameObject.SetActive(paused);
            _touch.gameObject.SetActive(!paused && _race.gameObject.activeSelf);
        }

        public void ShowResults(RaceResult r, string trackName)
        {
            SetScreens(menu: false, race: false, pause: false, results: true);
            _resultsTitle.text = r.NewPersonalBest ? "NEW BEST LAP!" : "FINISH";
            var sb = new StringBuilder();
            for (int i = 0; i < r.LapMs.Length; i++)
            {
                sb.Append("Lap ").Append(i + 1).Append("      ").Append(FormatTime(r.LapMs[i]));
                if (i == r.BestLapIndex) sb.Append("   best");
                sb.Append('\n');
            }
            int total = 0;
            foreach (int ms in r.LapMs) total += ms;
            sb.Append("Total      ").Append(FormatTime(total)).Append('\n');
            sb.Append("Best combo     ").Append(Mathf.RoundToInt(r.BestCombo)).Append(" pts\n");
            sb.Append("Drift points   ").Append(Mathf.RoundToInt(r.TotalPoints));
            if (r.PreviousBestMs > 0) sb.Append("\nPrevious best  ").Append(FormatTime(r.PreviousBestMs));
            _resultsBody.text = sb.ToString();
            _resultsRank.text = "Submitting to the " + trackName + " leaderboard...";
        }

        public void SetResultsRank(string text)
        {
            _resultsRank.text = text;
        }

        public void SetMenuStatus(string text)
        {
            _menuStatus.text = text;
        }

        public void SetLeaderboard(string text)
        {
            _menuBoard.text = text;
        }

        public void SetMenuBest(int bestMs)
        {
            _menuBest.text = bestMs > 0 ? "Your best lap: " + FormatTime(bestMs) : "No lap set yet";
        }

        public void SetGhostMode(string mode, string detail)
        {
            _pauseGhostLabel.text = "GHOST: " + mode;
            _ghostLabel.text = detail ?? string.Empty;
        }

        public void Toast(string constantText, float seconds)
        {
            _toast.text = constantText;
            _toastUntil = Time.unscaledTime + seconds;
        }

        private void SetScreens(bool menu, bool race, bool pause, bool results)
        {
            _menu.gameObject.SetActive(menu);
            _race.gameObject.SetActive(race);
            _touch.gameObject.SetActive(race);
            _pause.gameObject.SetActive(pause);
            _results.gameObject.SetActive(results);
        }

        // ---------------------------------------------------------------- per frame

        /// <summary>Refresh the race HUD. Allocation-free.</summary>
        public void Tick(RaceManager race, TuningValues tuning)
        {
            if (!_race.gameObject.activeSelf) return;

            if (race.State == RaceState.Countdown)
            {
                _countdown.gameObject.SetActive(true);
                _countdown.text = CountdownText[Mathf.Clamp(Mathf.CeilToInt(race.CountdownRemaining), 0, 3)];
                _goUntil = Time.unscaledTime + 0.8f;
            }
            else
            {
                bool showGo = race.State == RaceState.Racing && Time.unscaledTime < _goUntil;
                if (showGo) _countdown.text = CountdownText[0];
                _countdown.gameObject.SetActive(showGo);
            }

            _lap.SetInt(race.CurrentLap);
            _lapTime.SetTime(Mathf.FloorToInt(race.CurrentLapTime * 100f));
            int runBest = race.BestLapStepsThisRun > 0 ? RaceManager.StepsToMs(race.BestLapStepsThisRun) : 0;
            int pb = race.PersonalBestMs;
            int best = runBest > 0 && (pb <= 0 || runBest < pb) ? runBest : pb;
            bool hasBest = best > 0;
            _bestTime.SetActive(hasBest);
            _bestCaption.gameObject.SetActive(hasBest);
            if (hasBest) _bestTime.SetTime(best / 10);
            _speed.SetInt(Mathf.RoundToInt(race.Car.Speed * 3.6f));

            DriftScorer s = race.Scorer;
            bool showCombo = s.LiveComboPoints >= 1f && race.State == RaceState.Racing;
            _comboTitle.gameObject.SetActive(showCombo);
            _combo.SetActive(showCombo);
            _multiplier.SetActive(showCombo && s.Multiplier > 1);
            if (showCombo)
            {
                _combo.SetInt(Mathf.FloorToInt(s.LiveComboPoints));
                _multiplier.SetInt(s.Multiplier);
            }

            NitroMeter n = race.Nitro;
            float fill = n.IsBoosting && tuning.nitro_full_duration > 0f ? n.BoostRemaining / tuning.nitro_full_duration : n.Fill;
            if (Mathf.Abs(fill - _lastNitro) > 0.002f)
            {
                _lastNitro = fill;
                _nitroFill.anchorMax = new Vector2(Mathf.Clamp01(fill), 1f);
            }
            if (n.IsBoosting != _lastBoosting)
            {
                _lastBoosting = n.IsBoosting;
                _nitroFillImage.color = n.IsBoosting ? Color.white : UiFactory.Nitro;
            }

            if (_toastUntil > 0f && Time.unscaledTime > _toastUntil)
            {
                _toast.text = string.Empty;
                _toastUntil = 0f;
            }
        }

        public static string FormatTime(int ms)
        {
            if (ms <= 0) return "-:--.--";
            int cs = ms / 10;
            return (cs / 6000) + ":" + (cs / 100 % 60).ToString("00") + "." + (cs % 100).ToString("00");
        }
    }

    internal static class RectExtensions
    {
        public static void Fill(this RectTransform rt)
        {
            UiFactory.Stretch(rt);
        }
    }
}
