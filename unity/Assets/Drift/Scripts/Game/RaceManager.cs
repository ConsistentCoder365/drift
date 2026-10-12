using System;
using Drift.Core;
using UnityEngine;

namespace Drift.Game
{
    public enum RaceState
    {
        Idle,
        Countdown,
        Racing,
        Finished,
    }

    /// <summary>Everything the results screen and the server need about a finished run.</summary>
    public sealed class RaceResult
    {
        public int[] LapMs;
        public int BestLapMs;
        public int BestLapIndex;
        public float BestCombo;
        public float TotalPoints;
        public bool NewPersonalBest;
        public int PreviousBestMs;
        /// <summary>Encoded ghost of this run's best lap, or null if no lap was recorded.</summary>
        public byte[] BestLapGhost;
    }

    /// <summary>
    /// Laps, checkpoints and the state machine: Countdown -> Racing -> Finished.
    ///
    /// One FixedUpdate tick (50 Hz) does, in order:
    ///   1. read the pose the physics just produced, track laps and record the ghost sample,
    ///   2. read input, fire nitro, and step the car (forces for the next physics step),
    ///   3. score the drift.
    /// Doing it all from one place keeps lap timing and ghost samples exactly in step:
    /// lap time = steps x 20 ms, and a lap of N steps has N + 1 samples.
    /// </summary>
    public sealed class RaceManager : MonoBehaviour
    {
        public const float StepSeconds = 0.02f;
        public const float CountdownSeconds = 3f;

        private CarController _car;
        private InputReader _input;
        private GhostPlayer _ghost;
        private TrackRuntime _track;
        private TuningValues _t;
        private LapTracker _laps;
        private GhostRecorder _recorder;
        private int[] _lapSteps;
        private int _lapStartStep;
        private Vector2 _prevPos;

        public RaceState State { get; private set; }
        public DriftScorer Scorer { get; private set; }
        public NitroMeter Nitro { get; private set; }
        public float CountdownRemaining { get; private set; }
        public int Step { get; private set; }
        public int TotalLaps { get { return _lapSteps.Length; } }
        public int CompletedLaps { get { return _laps.CompletedLaps; } }
        public int CurrentLap { get { return Mathf.Clamp(_laps.CompletedLaps + 1, 1, TotalLaps); } }
        public bool LapStarted { get { return _laps.Started; } }
        public int BestLapStepsThisRun { get { return _recorder.BestLapSteps; } }
        public int PersonalBestMs { get; set; }
        public CarController Car { get { return _car; } }

        /// <summary>Seconds into the current lap, counted in physics steps like the official lap time.</summary>
        public float CurrentLapTime
        {
            get
            {
                if (State != RaceState.Racing || !_laps.Started) return 0f;
                return (Step - _lapStartStep) * StepSeconds;
            }
        }

        public int LastLapMs { get; private set; }

        public event Action<int, int, bool> LapCompleted; // lap index (0-based), lap ms, best of this run
        public event Action<RaceResult> RaceFinished;
        public event Action Go;

        public void Init(CarController car, InputReader input, GhostPlayer ghost, TrackRuntime track, TuningValues tuning)
        {
            _car = car;
            _input = input;
            _ghost = ghost;
            _track = track;
            _t = tuning;
            _laps = new LapTracker(track.Gates);
            _recorder = new GhostRecorder();
            _lapSteps = new int[Mathf.Max(1, track.Definition.laps)];
            Scorer = new DriftScorer(tuning);
            Nitro = new NitroMeter(tuning);
            State = RaceState.Idle;
        }

        public void ApplyTuning(TuningValues tuning)
        {
            _t = tuning;
            Scorer.SetTuning(tuning);
            Nitro.SetTuning(tuning);
            _car.SetTuning(tuning);
        }

        /// <summary>Put the car on the grid and start the countdown.</summary>
        public void StartCountdown()
        {
            _car.Teleport(_track.SpawnPosition, _track.SpawnRotation);
            _laps.Reset();
            _recorder.Reset();
            Scorer.Reset();
            Nitro.Reset();
            Array.Clear(_lapSteps, 0, _lapSteps.Length);
            Step = 0;
            _lapStartStep = 0;
            LastLapMs = 0;
            _prevPos = Flat(_track.SpawnPosition);
            _input.ClearLatches();
            if (_ghost != null) _ghost.Hold();
            CountdownRemaining = CountdownSeconds;
            State = RaceState.Countdown;
        }

        public void Stop()
        {
            State = RaceState.Idle;
            _car.Teleport(_track.SpawnPosition, _track.SpawnRotation);
        }

        private void Update()
        {
            if (State != RaceState.Countdown) return;
            CountdownRemaining -= Time.deltaTime;
            if (CountdownRemaining <= 0f)
            {
                CountdownRemaining = 0f;
                State = RaceState.Racing;
                _input.ClearLatches();
                if (Go != null) Go();
            }
        }

        private void FixedUpdate()
        {
            if (State == RaceState.Idle || State == RaceState.Countdown) return;
            float dt = Time.fixedDeltaTime;

            // 1. Laps and recording, from the pose physics just produced.
            Vector3 pos = _car.Body.position;
            Quaternion rot = _car.Body.rotation;
            Vector2 cur = Flat(pos);
            if (State == RaceState.Racing) TrackLap(_prevPos, cur, pos, rot);
            _prevPos = cur;

            // 2. Input and forces.
            CarInput input = default(CarInput);
            if (State == RaceState.Racing)
            {
                input.Steer = _input.Steer;
                input.Throttle = 1f;
                input.Drift = _input.DriftHeld;
                if (_input.ConsumeNitro()) Nitro.TryFire();
            }
            Nitro.Tick(dt);
            input.Nitro = Nitro.IsBoosting && State == RaceState.Racing;
            _car.PhysicsStep(input, dt);

            // 3. Drift score and nitro gain.
            if (_car.ConsumeWallHit()) Scorer.WallHit();
            if (State == RaceState.Racing)
            {
                float points = Scorer.Step(dt, _car.DriftAngleRad, _car.Speed);
                Nitro.AddFromPoints(points);
                Step++;
            }
        }

        private void TrackLap(Vector2 prev, Vector2 cur, Vector3 pos, Quaternion rot)
        {
            switch (_laps.Step(prev, cur))
            {
                case LapEvent.LapStarted:
                    BeginLap(pos, rot);
                    break;

                case LapEvent.LapCompleted:
                {
                    int lapIndex = _laps.CompletedLaps - 1;
                    int steps = Step - _lapStartStep;
                    bool best = _recorder.EndLap(Step, pos, rot, steps);
                    _lapSteps[lapIndex] = steps;
                    LastLapMs = StepsToMs(steps);
                    if (LapCompleted != null) LapCompleted(lapIndex, LastLapMs, best);
                    if (_laps.CompletedLaps >= TotalLaps) Finish();
                    else BeginLap(pos, rot);
                    break;
                }

                default:
                    _recorder.Record(Step, pos, rot);
                    break;
            }
        }

        private void BeginLap(Vector3 pos, Quaternion rot)
        {
            _lapStartStep = Step;
            _recorder.BeginLap(Step, pos, rot);
            if (_ghost != null) _ghost.Restart(Time.fixedTime);
        }

        private void Finish()
        {
            State = RaceState.Finished;
            Scorer.Finish();

            var result = new RaceResult
            {
                LapMs = new int[_lapSteps.Length],
                BestCombo = Scorer.BestCombo,
                TotalPoints = Scorer.TotalPoints,
                PreviousBestMs = PersonalBestMs,
            };
            int bestIndex = 0;
            for (int i = 0; i < _lapSteps.Length; i++)
            {
                result.LapMs[i] = StepsToMs(_lapSteps[i]);
                if (_lapSteps[i] < _lapSteps[bestIndex]) bestIndex = i;
            }
            result.BestLapIndex = bestIndex;
            result.BestLapMs = result.LapMs[bestIndex];
            result.NewPersonalBest = PersonalBestMs <= 0 || result.BestLapMs < PersonalBestMs;
            if (_recorder.HasBestLap)
                result.BestLapGhost = GhostCodec.Encode(_recorder.BestLap, _track.GhostBoundsMin, _track.GhostBoundsMax);

            if (RaceFinished != null) RaceFinished(result);
        }

        public static int StepsToMs(int steps)
        {
            return Mathf.RoundToInt(steps * StepSeconds * 1000f);
        }

        private static Vector2 Flat(Vector3 p)
        {
            return new Vector2(p.x, p.z);
        }
    }
}
