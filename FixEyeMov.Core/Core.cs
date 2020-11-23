using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using static Karenia.FixEyeMov.Core.MathfExt;

namespace Karenia.FixEyeMov.Core
{
    public class EyeMovementConfig
    {
        /*
         * tremor_interval (float) – time interval between two tremors in seconds
         * tremor_interval_sd (float) – standard deviation of tremor_interval
         * tremor_amplitude (float) – amplitude of tremor in degree
         * drift_speed (float) – speed of drift in degree / sec
         * drift_speed_sd (float) – standard deviation of drift
         * msaccade_interval (float) – time interval between two micro-saccade in seconds
         * msaccade_interval_sd (float) – standard deviation of micro-saccade interval
         * msaccade_direction_sd (float) – deviation of direction of movement towards fixation point
         * msaccade_speed (float) – micro-saccade speed in degree/second
         */

        //public ConfigEntry<float> TremorInterval { get; private set; }

        //public ConfigEntry<float> TremorStdDev { get; private set; }

        //public ConfigEntry<float> TremorAmplitude { get; private set; }

        public ConfigEntry<float> DriftSpeed { get; private set; }

        public ConfigEntry<float> DriftSpeedStdDev { get; private set; }

        public ConfigEntry<float> DriftDirectionRange { get; private set; }

        public ConfigEntry<float> MSaccadeInterval { get; private set; }

        public ConfigEntry<float> MSaccadeIntervalStdDev { get; private set; }

        public ConfigEntry<float> MSaccadeDirectionDev { get; private set; }

        public ConfigEntry<float> MSaccadeSpeed { get; private set; }

        public ConfigEntry<float> MSaccadeSpeedStdDev { get; private set; }

        public ConfigEntry<float> MSaccadeOvershootDev { get; private set; }

        public ConfigEntry<bool> DebugLog { get; private set; }

        public ConfigEntry<bool> Enabled { get; private set; }

        public void Bind(ConfigFile config)
        {
            const string section = "EyeMovement";
            Enabled = config.Bind(section, nameof(Enabled), true);

            //TremorInterval = config.Bind(section, nameof(TremorInterval), 0.012f);
            //TremorStdDev = config.Bind(section, nameof(TremorStdDev), 0.001f);
            //TremorAmplitude = config.Bind(section, nameof(TremorAmplitude), 0.05f);
            DriftSpeed = config.Bind(section, nameof(DriftSpeed), 800f, "Eye drift mean speed (degrees/second)");
            DriftSpeedStdDev = config.Bind(section, nameof(DriftSpeedStdDev), 200f, "Eye drift speed standard deviation");
            DriftDirectionRange = config.Bind(section, nameof(DriftDirectionRange), 100f, "Eye drift direction change range");
            MSaccadeInterval = config.Bind(section, nameof(MSaccadeInterval), 0.8f, "Micro-saccade mean interval (seconds)");
            MSaccadeIntervalStdDev = config.Bind(section, nameof(MSaccadeIntervalStdDev), 0.6f, "Micro-saccade mean interval");
            MSaccadeDirectionDev = config.Bind(section, nameof(MSaccadeDirectionDev), 40f, "Micro-saccade direction standard deviation (degrees)");
            MSaccadeSpeed = config.Bind(section, nameof(MSaccadeSpeed), 10000f, "Micro-saccade mean speed (degrees/second)");
            MSaccadeSpeedStdDev = config.Bind(section, nameof(MSaccadeSpeedStdDev), 300f, "Micro-saccade speed standard deviation");
            MSaccadeOvershootDev = config.Bind(section, nameof(MSaccadeOvershootDev), 0.6f, "Micro-saccade overshooting standard deviation");

            DebugLog = config.Bind(section, nameof(DebugLog), true);
        }
    }

    public static class MathfExt
    {
        /// <summary>
        /// Generate Gaussian random number
        /// </summary>
        /// <param name="mean">Mean value</param>
        /// <param name="stdDev">Standard Deviation</param>
        /// <returns></returns>
        public static float GaussianRandom(float mean, float stdDev)
        {
            //uniform [0,1] random doubles
            var u1 = UnityEngine.Random.value;
            var u2 = UnityEngine.Random.value;
            //random normal(0,1)
            var randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
            //random normal(mean,stdDev^2)
            return mean + stdDev * randStdNormal;
        }
    }

    /// <summary>
    /// State and algorithm of fixational eye simulator.
    /// 
    /// <para>
    /// The original algorithm comes from https://github.com/hjiang36/pyEyeBall/
    /// </para>
    /// </summary>
    public class EyeMovementState
    {
        public EyeMovementState(EyeMovementConfig config, BepInEx.Logging.ManualLogSource logger = null)
        {
            this.config = config;
            this.logger = logger;
        }

        private readonly EyeMovementConfig config;
        private readonly ManualLogSource logger;

        Vector2 curDelta = Vector2.zero;
        float curDriftDirection = UnityEngine.Random.Range(0f, Mathf.PI * 2);
        float curDriftSpeed = 0;

        //float timeTillNextTremor = 0;
        float timeTillNextSaccade = 0;
        float remainingTimeOfThisSaccade = 0;
        float mSaccadeSpeed;
        float mSaccadeAxis;

        /// <summary>
        /// Generate information for the upcoming micro-saccade
        /// </summary>
        private void SetNextSaccade()
        {
            // Set time according to config
            var time = GaussianRandom(config.MSaccadeInterval.Value, config.MSaccadeIntervalStdDev.Value);
            if (time < config.MSaccadeInterval.Value / 2) time = config.MSaccadeInterval.Value;
            timeTillNextSaccade = time;

            // Micro-saccade is toward center point, but with deviation.
            // Note: this angle calculated is **away from center**, reverse is done afterwards.
            mSaccadeAxis = Mathf.Atan2(curDelta.y, curDelta.x);
            mSaccadeAxis += GaussianRandom(0, config.MSaccadeDirectionDev.Value * Mathf.Deg2Rad);

            // Generate saccade speed
            mSaccadeSpeed = GaussianRandom(config.MSaccadeSpeed.Value, config.MSaccadeSpeedStdDev.Value) * Mathf.Deg2Rad;
            if (mSaccadeSpeed < 0) mSaccadeSpeed = config.MSaccadeSpeed.Value * Mathf.Deg2Rad;

            // Generate move angle with deviation
            var angleToCenter = curDelta.magnitude;
            angleToCenter *= GaussianRandom(1f, config.MSaccadeOvershootDev.Value);

            // Set remaining time
            remainingTimeOfThisSaccade = angleToCenter / mSaccadeSpeed;

            if (config.DebugLog.Value && logger != null)
                logger.LogInfo($"Saccade: {angleToCenter} @ {mSaccadeAxis * Mathf.Rad2Deg}deg");
        }

        /// <summary>
        /// Update movement of micro-saccade if needed
        /// </summary>
        /// <param name="deltaTime"></param>
        private void UpdateSaccade(float deltaTime)
        {
            if (remainingTimeOfThisSaccade > 0)
            {
                var saccadeTime = Mathf.Min(deltaTime, remainingTimeOfThisSaccade);
                remainingTimeOfThisSaccade -= deltaTime;

                // Generate direction vector.
                var quat = new Vector2(Mathf.Cos(mSaccadeAxis), Mathf.Sin(mSaccadeAxis)) * (saccadeTime * mSaccadeSpeed);
                curDelta -= quat;

                if (config.DebugLog.Value && logger != null)
                    logger.LogInfo($"Performing Saccade: {saccadeTime * mSaccadeSpeed * Mathf.Rad2Deg} @ {mSaccadeAxis * Mathf.Rad2Deg}deg");
            }
        }

        /// <summary>
        /// Update movement by random drifting
        /// </summary>
        /// <param name="deltaTime"></param>
        private void UpdateDrift(float deltaTime)
        {
            // Drift direction is current drift direction plus some deviation
            curDriftDirection += (UnityEngine.Random.value * 2 - 1) * config.DriftDirectionRange.Value;
            curDriftDirection %= 2 * Mathf.PI;
            curDriftSpeed = GaussianRandom(config.DriftSpeed.Value, config.DriftSpeedStdDev.Value) * Mathf.Deg2Rad;

            // Calculate drift delta
            curDelta += new Vector2(Mathf.Cos(curDriftDirection), Mathf.Sin(curDriftDirection)) * curDriftSpeed * deltaTime;

            if (config.DebugLog.Value && logger != null)
                logger.LogInfo($"Drift: {curDriftSpeed} @ {curDriftDirection * Mathf.Rad2Deg}deg");
        }

        // Tremors are disabled because they are too fast
        //private void SetNextTremor()
        //{
        //    var time = GaussianRandom(config.TremorInterval.Value, config.TremorStdDev.Value);
        //    if (time < 0.001f) time = 0.001f;

        //    var direction = UnityEngine.Random.Range(0f, 360f);
        //    var axis = new Vector3(Mathf.Cos(direction), 0f, Mathf.Sin(direction));
        //    var quat = Quaternion.AngleAxis(config.TremorAmplitude.Value, axis);

        //    timeTillNextTremor = time;
        //    curDelta *= quat;
        //}

        /// <summary>
        /// Update model and return calculated delta on this frame
        /// </summary>
        /// <param name="deltaTime">Time passed (in seconds) after last <c>Tick()</c></param>
        /// <returns>Euler angle delta in <c>x</c> and <c>z</c> direction relative to idle position</returns>
        public Vector2 Tick(float deltaTime)
        {
            timeTillNextSaccade -= deltaTime;
            if (timeTillNextSaccade <= 0) SetNextSaccade();
            UpdateSaccade(deltaTime);

            UpdateDrift(deltaTime);

            //timeTillNextTremor -= deltaT;
            //if (timeTillNextTremor <= 0) SetNextTremor();

            return this.curDelta;
        }
    }

}
