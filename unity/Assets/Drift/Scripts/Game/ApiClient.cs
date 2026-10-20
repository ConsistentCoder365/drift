using System;
using System.Collections;
using System.Text;
using Drift.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Drift.Game
{
    // JSON shapes of the REST API. Field names are snake_case to match the server exactly,
    // because JsonUtility maps fields by name.

    [Serializable]
    public class RunSubmission
    {
        public string player;
        public string track;
        public int[] lap_times_ms;
        public string ghost; // base64 of the GhostCodec bytes of the best lap
        public string client_version;
    }

    [Serializable]
    public class RunResponse
    {
        public string run_id;
        public bool validated;
        public string reason;
        public int best_lap_ms;
        public int rank;  // 0 when not on the leaderboard
        public int total;
        public bool personal_best;
    }

    [Serializable]
    public class LeaderboardEntry
    {
        public int rank;
        public string player;
        public int best_lap_ms;
        public string run_id;
    }

    [Serializable]
    public class LeaderboardResponse
    {
        public string track;
        public int total;
        public LeaderboardEntry[] entries;
        public LeaderboardEntry you; // rank 0 when the player has no entry
    }

    /// <summary>
    /// The only class that touches the network. Every call is a coroutine with a short timeout
    /// and reports failure through a callback, so the game keeps working offline.
    /// </summary>
    public sealed class ApiClient
    {
        private const int TimeoutSeconds = 5;

        public string BaseUrl { get; set; }

        public ApiClient(string baseUrl)
        {
            BaseUrl = baseUrl;
        }

        private string Url(string path)
        {
            return (BaseUrl ?? string.Empty).TrimEnd('/') + path;
        }

        public IEnumerator GetConfig(Action<string> onJson, Action<string> onError)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(Url("/config")))
            {
                yield return Send(req);
                if (Failed(req, onError)) yield break;
                onJson(req.downloadHandler.text);
            }
        }

        public IEnumerator SubmitRun(RunSubmission run, Action<RunResponse> onDone, Action<string> onError)
        {
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(run));
            using (var req = new UnityWebRequest(Url("/runs"), UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(body) { contentType = "application/json" };
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                yield return Send(req);
                if (Failed(req, onError)) yield break;
                onDone(JsonUtility.FromJson<RunResponse>(req.downloadHandler.text));
            }
        }

        public IEnumerator GetLeaderboard(string track, int limit, string player, Action<LeaderboardResponse> onDone, Action<string> onError)
        {
            string path = "/leaderboard?track=" + UnityWebRequest.EscapeURL(track) + "&limit=" + limit;
            if (!string.IsNullOrEmpty(player)) path += "&player=" + UnityWebRequest.EscapeURL(player);
            using (UnityWebRequest req = UnityWebRequest.Get(Url(path)))
            {
                yield return Send(req);
                if (Failed(req, onError)) yield break;
                onDone(JsonUtility.FromJson<LeaderboardResponse>(req.downloadHandler.text));
            }
        }

        public IEnumerator GetGhost(string runId, Action<byte[]> onDone, Action<string> onError)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(Url("/ghosts/" + UnityWebRequest.EscapeURL(runId))))
            {
                yield return Send(req);
                if (Failed(req, onError)) yield break;
                onDone(req.downloadHandler.data);
            }
        }

        private static UnityWebRequestAsyncOperation Send(UnityWebRequest req)
        {
            req.timeout = TimeoutSeconds;
            return req.SendWebRequest();
        }

        private static bool Failed(UnityWebRequest req, Action<string> onError)
        {
            if (req.result == UnityWebRequest.Result.Success) return false;
            string message = req.responseCode > 0 ? "HTTP " + req.responseCode + ": " + req.error : req.error;
            if (onError != null) onError(message);
            return true;
        }
    }
}
