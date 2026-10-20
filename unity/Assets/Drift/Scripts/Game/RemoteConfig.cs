using System;
using System.Collections;
using Drift.Core;
using UnityEngine;

namespace Drift.Game
{
    /// <summary>
    /// Tuning values from /config. The last good values are cached on the device, and the
    /// built-in defaults are used when there is no cache, so the game never needs the server.
    /// New values apply from the next race, never mid-race.
    /// </summary>
    public sealed class RemoteConfig
    {
        private const string CacheKey = "drift_tuning_cache";

        public TuningValues Current { get; private set; }
        public string Source { get; private set; }

        public RemoteConfig()
        {
            Current = new TuningValues();
            Source = "built-in defaults";
            string cached = PlayerPrefs.GetString(CacheKey, string.Empty);
            if (!string.IsNullOrEmpty(cached) && TryParse(cached, out TuningValues fromCache))
            {
                Current = fromCache;
                Source = "cached config";
            }
        }

        public IEnumerator Refresh(ApiClient api, Action<TuningValues> onUpdated)
        {
            string json = null;
            yield return api.GetConfig(j => json = j, e => Debug.Log("Remote config unavailable (" + e + "), using " + Source + "."));
            if (json == null) yield break;
            if (!TryParse(json, out TuningValues fresh))
            {
                Debug.LogWarning("Remote config rejected: values out of range.");
                yield break;
            }
            Current = fresh;
            Source = "server config v" + fresh.version;
            PlayerPrefs.SetString(CacheKey, json);
            PlayerPrefs.Save();
            if (onUpdated != null) onUpdated(fresh);
        }

        /// <summary>Overlay JSON on the defaults (missing keys keep their default) and sanity-check it.</summary>
        public static bool TryParse(string json, out TuningValues values)
        {
            values = new TuningValues();
            try
            {
                JsonUtility.FromJsonOverwrite(json, values);
            }
            catch (Exception)
            {
                return false;
            }
            return values.grip_normal > 0f && values.grip_drift > 0f && values.top_speed > 0f
                   && values.nitro_top_speed >= values.top_speed && values.acceleration > 0f
                   && values.combo_window >= 0f && values.max_yaw_rate > 0f
                   && !float.IsNaN(values.grip_normal + values.grip_drift + values.top_speed + values.nitro_top_speed);
        }
    }
}
