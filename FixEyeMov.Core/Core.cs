using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using static Karenia.FixEyeMov.Core.MathfExt;

namespace Karenia.FixEyeMov.Core
{
    public class EyeMovementDefaultConfig
    {
        public float DriftSpeed { get; set; } = 800f;
        public float DriftSpeedStdDev { get; set; } = 200f;
        public float DriftDirectionRange { get; set; } = 100f;
        public float MSaccadeInterval { get; set; } = 0.8f;
        public float MSaccadeIntervalStdDev { get; set; } = 0.6f;
        public float MSaccadeDirectionDev { get; set; } = 40f;
        public float MSaccadeSpeed { get; set; } = 10000f;
        public float MSaccadeSpeedStdDev { get; set; } = 300f;
        public float MSaccadeOvershootDev { get; set; } = 0.6f;
        public float MaxOffsetAngle { get; set; } = 5f;
    }

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

        public ConfigEntry<float> DriftSpeed { get; private set; }

        public ConfigEntry<float> DriftSpeedStdDev { get; private set; }

        public ConfigEntry<float> DriftDirectionRange { get; private set; }

        public ConfigEntry<float> MSaccadeInterval { get; private set; }

        public ConfigEntry<float> MSaccadeIntervalStdDev { get; private set; }

        public ConfigEntry<float> MSaccadeDirectionDev { get; private set; }

        public ConfigEntry<float> MSaccadeSpeed { get; private set; }

        public ConfigEntry<float> MSaccadeSpeedStdDev { get; private set; }

        public ConfigEntry<float> MSaccadeOvershootDev { get; private set; }

        public ConfigEntry<float> MaxOffsetAngle { get; private set; }

        public ConfigEntry<bool> DebugLog { get; private set; }

        public ConfigEntry<bool> Enabled { get; private set; }

        public EyeMovementConfig(ConfigFile config, EyeMovementDefaultConfig defaultConfig)
        {
            const string section = "Basic Settings";
            const string fineTuneSection = "Fine Tuning";
            Enabled = config.Bind(section, nameof(Enabled), true);
            DebugLog = config.Bind(section, nameof(DebugLog), false);

            DriftSpeed = config.Bind(fineTuneSection, nameof(DriftSpeed), defaultConfig.DriftSpeed, "Eye drift mean speed (degrees/second)");
            DriftSpeedStdDev = config.Bind(fineTuneSection, nameof(DriftSpeedStdDev), defaultConfig.DriftSpeedStdDev, "Eye drift speed standard deviation");
            DriftDirectionRange = config.Bind(fineTuneSection, nameof(DriftDirectionRange), defaultConfig.DriftDirectionRange, "Eye drift direction change range");
            MSaccadeInterval = config.Bind(fineTuneSection, nameof(MSaccadeInterval), defaultConfig.MSaccadeInterval, "Micro-saccade mean interval (seconds)");
            MSaccadeIntervalStdDev = config.Bind(fineTuneSection, nameof(MSaccadeIntervalStdDev), defaultConfig.MSaccadeIntervalStdDev, "Micro-saccade mean interval");
            MSaccadeDirectionDev = config.Bind(fineTuneSection, nameof(MSaccadeDirectionDev), defaultConfig.MSaccadeDirectionDev, "Micro-saccade direction standard deviation (degrees)");
            MSaccadeSpeed = config.Bind(fineTuneSection, nameof(MSaccadeSpeed), defaultConfig.MSaccadeSpeed, "Micro-saccade mean speed (degrees/second)");
            MSaccadeSpeedStdDev = config.Bind(fineTuneSection, nameof(MSaccadeSpeedStdDev), defaultConfig.MSaccadeSpeedStdDev, "Micro-saccade speed standard deviation");
            MSaccadeOvershootDev = config.Bind(fineTuneSection, nameof(MSaccadeOvershootDev), defaultConfig.MSaccadeOvershootDev, "Micro-saccade overshooting factor standard deviation");

            MaxOffsetAngle = config.Bind(fineTuneSection, nameof(MaxOffsetAngle), 5f, "Max offset angle (degrees)");
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

        public static float ClampedGaussianRandom(float mean, float stdDev, float lowerBound, float higherBound)
        {
            return Mathf.Clamp(GaussianRandom(mean, stdDev), lowerBound, higherBound);
        }

        public static float MuClampedGaussianRandom(float mean, float stdDev, float mu)
        {
            return Mathf.Clamp(GaussianRandom(mean, stdDev), mean - mu * stdDev, mean + mu * stdDev);
        }

        public static float LogNormalRandom(float mean, float stdDev)
        {
            float v = Mathf.Log(1 + (stdDev * stdDev / mean / mean));
            var mu = Mathf.Log(mean) - v / 2;
            var sigma = Mathf.Sqrt(v);
            return Mathf.Exp(GaussianRandom(mu, sigma));
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
        public EyeMovementState(EyeMovementConfig config, BepInEx.Logging.ManualLogSource? logger = null)
        {
            this.config = config;
            this.logger = logger;
        }

        private readonly EyeMovementConfig config;
        private readonly ManualLogSource? logger;

        private Vector2 curDelta = Vector2.zero;
        private float curDriftDirection = UnityEngine.Random.Range(0f, Mathf.PI * 2);
        private float curDriftSpeed = 0;

        private float timeTillNextSaccade = 0;
        private float remainingTimeOfThisSaccade = 0;
        private float mSaccadeSpeed;
        private float mSaccadeAxis;

        /// <summary>
        /// Generate information for the upcoming micro-saccade
        /// </summary>
        private void SetNextSaccade(float overshootDevFactor = 1.0f)
        {
            // Set time according to config
            var time = LogNormalRandom(config.MSaccadeInterval.Value, config.MSaccadeIntervalStdDev.Value);
            timeTillNextSaccade = time;

            // Micro-saccade is toward center point, but with deviation.
            // Note: this angle calculated is **away from center**, reverse is done afterwards.
            mSaccadeAxis = Mathf.Atan2(curDelta.y, curDelta.x);
            mSaccadeAxis += MuClampedGaussianRandom(0, config.MSaccadeDirectionDev.Value * Mathf.Deg2Rad, 3);

            // Generate saccade speed
            mSaccadeSpeed = LogNormalRandom(config.MSaccadeSpeed.Value, config.MSaccadeSpeedStdDev.Value) * Mathf.Deg2Rad;

            // Generate move angle with deviation
            var angleToCenter = curDelta.magnitude;
            var maxOffsetAngle = config.MaxOffsetAngle.Value;
            // Limit return angle inside allowed range
            angleToCenter = ClampedGaussianRandom(
                angleToCenter,
                config.MSaccadeOvershootDev.Value * overshootDevFactor * angleToCenter,
                angleToCenter - maxOffsetAngle,
                angleToCenter + maxOffsetAngle);

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
            curDriftSpeed = LogNormalRandom(config.DriftSpeed.Value, config.DriftSpeedStdDev.Value) * Mathf.Deg2Rad;

            // Calculate drift delta
            curDelta += new Vector2(Mathf.Cos(curDriftDirection), Mathf.Sin(curDriftDirection)) * curDriftSpeed * deltaTime;

            if (config.DebugLog.Value && logger != null)
                logger.LogInfo($"Drift: {curDriftSpeed} @ {curDriftDirection * Mathf.Rad2Deg}deg");
        }

        /// <summary>
        /// Update model and return calculated delta on this frame
        /// </summary>
        /// <param name="deltaTime">Time passed (in seconds) after last <c>Tick()</c></param>
        /// <param name="forceSaccade">Force a microsaccade to occur (e.g. when blinking)</param>
        /// <returns>Euler angle delta in <c>x</c> and <c>z</c> direction relative to idle position</returns>
        public Vector2 Tick(float deltaTime, bool forceSaccade = false)
        {
            timeTillNextSaccade -= deltaTime;
            if (timeTillNextSaccade <= 0 || forceSaccade) SetNextSaccade(forceSaccade ? 0 : 1);
            UpdateSaccade(deltaTime);

            UpdateDrift(deltaTime);

            return this.curDelta;
        }
    }
}
