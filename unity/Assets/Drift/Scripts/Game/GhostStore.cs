using System;
using System.IO;
using Drift.Core;
using UnityEngine;

namespace Drift.Game
{
    /// <summary>The player's best lap ghost, kept on the device so ghosts work fully offline.</summary>
    public static class GhostStore
    {
        private static string GhostPath(string trackId)
        {
            return Path.Combine(Application.persistentDataPath, "ghost_" + trackId + ".bin");
        }

        private static string BestKey(string trackId)
        {
            return "drift_pb_ms_" + trackId;
        }

        public static int PersonalBestMs(string trackId)
        {
            return PlayerPrefs.GetInt(BestKey(trackId), 0);
        }

        public static void Save(string trackId, byte[] ghost, int lapMs)
        {
            try
            {
                File.WriteAllBytes(GhostPath(trackId), ghost);
                PlayerPrefs.SetInt(BestKey(trackId), lapMs);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not save ghost: " + e.Message);
            }
        }

        public static bool TryLoad(string trackId, GhostBuffer into)
        {
            string path = GhostPath(trackId);
            if (!File.Exists(path)) return false;
            try
            {
                byte[] data = File.ReadAllBytes(path);
                if (GhostCodec.TryDecode(data, into, out string error)) return true;
                Debug.LogWarning("Stored ghost is invalid (" + error + "), ignoring it.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not load ghost: " + e.Message);
            }
            return false;
        }

        public static void Clear(string trackId)
        {
            string path = GhostPath(trackId);
            if (File.Exists(path)) File.Delete(path);
            PlayerPrefs.DeleteKey(BestKey(trackId));
        }
    }
}
