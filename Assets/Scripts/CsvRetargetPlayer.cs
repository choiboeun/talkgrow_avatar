using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

[System.Serializable]
public class RetargetBinding
{
    public Transform bone;
    public int startIndex;
    public Vector3 addOffset;
    public Vector3 scale = Vector3.one;

    [Range(0f, 1f)] public float smooth = 0f;
    public Vector3 clampDeg = new Vector3(60f, 60f, 60f);
}

public class CsvRetargetPlayer : MonoBehaviour
{
    [Header("Playback")]
    public int fps = 60;
    [Range(0.25f, 2f)] public float speedMultiplier = 1f;
    public float interTokenHoldSeconds = 0.15f;

    [Header("Bindings")]
    public List<RetargetBinding> bindings = new List<RetargetBinding>();

    [Header("Group Tuning")]
    [Range(0f, 4f)] public float handGain = 1f;
    [Range(0f, 1f)] public float faceSmooth = 0f;

    [Header("Debug (Editor)")]
    public bool playOnSpace = true;
    public string debugTokensLine = "안녕하세요 감사합니다";

    [Header("Debug Sweep")]
    public bool debugSweep = false;
    public Transform testBone;
    public int sweepIndex = 0;
    public int sweepStride = 3;

    float[][] _sequenceFrames;
    int _seqFrame;
    float[] _prevFace;

    Dictionary<Transform, Quaternion> _bind = new Dictionary<Transform, Quaternion>();
    Dictionary<Transform, Vector3> _prevEuler = new Dictionary<Transform, Vector3>();

    string BaseDir => Path.Combine(Application.streamingAssetsPath, "csv_unity_v2_rad");

    void Start()
    {
        Application.targetFrameRate = 60;

        _bind.Clear();
        _prevEuler.Clear();
        foreach (var b in bindings)
        {
            if (b.bone == null) continue;
            _bind[b.bone] = b.bone.localRotation;
            _prevEuler[b.bone] = Vector3.zero;
        }

        _prevFace = new float[411];
        InvokeRepeating(nameof(Tick), 0f, (1f / Mathf.Max(1, fps)) / Mathf.Max(0.01f, speedMultiplier));
    }

    void Update()
    {
        if (playOnSpace && Input.GetKeyDown(KeyCode.Space))
        {
            if (!string.IsNullOrWhiteSpace(debugTokensLine))
            {
                var tokens = System.Text.RegularExpressions.Regex
                    .Split(debugTokensLine, @"[|\s,]+")
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();

                PlayTokens(tokens);
            }
        }
    }

    public void PlayTokens(string[] tokens)
    {
        if (tokens == null || tokens.Length == 0)
        {
            Debug.LogWarning("PlayTokens: empty");
            return;
        }

        BuildSequenceFromTokensEnumerable(tokens.Where(t => !string.IsNullOrWhiteSpace(t)));
        _seqFrame = 0;
        _sequenceFrames = (float[][])_sequenceFrames.Clone();

        Debug.Log($"[CsvRetargetPlayer] Sequence triggered by PlayTokens. Frame reset to 0.");
    }

    public void PlayTokensJson(string jsonOrPipe)
    {
        try
        {
            var arr = MiniJsonToStrings(jsonOrPipe);
            if (arr != null && arr.Length > 0)
            {
                PlayTokens(arr);
                Play();  // 🔧 토큰 설정 후 자동 재생
                return;
            }
        }
        catch { }

        var toks = System.Text.RegularExpressions.Regex
            .Split(jsonOrPipe ?? "", @"[|\s,]+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
        PlayTokens(toks);
        Play();  // 🔧 여기서도 자동 재생
    }

    void Tick()
    {
        if (_sequenceFrames == null || _sequenceFrames.Length == 0) return;

        if (_seqFrame >= _sequenceFrames.Length)
        {
            _sequenceFrames = null;
            return;
        }

        var f = (float[])_sequenceFrames[_seqFrame].Clone();

        for (int i = 99; i < 162; i++) f[i] *= handGain;
        for (int i = 162; i < 225; i++) f[i] *= handGain;

        if (faceSmooth > 0f)
        {
            float a = 1f - faceSmooth;
            for (int i = 225; i < 411; i++)
                f[i] = Mathf.Lerp(_prevFace[i], f[i], a);
            System.Array.Copy(f, _prevFace, 411);
        }

        if (debugSweep && testBone != null)
        {
            int s = Mathf.Clamp(sweepIndex, 0, 408);
            testBone.localRotation = Quaternion.Euler(f[s + 0], f[s + 1], f[s + 2]);
        }

        foreach (var b in bindings)
        {
            if (b.bone == null) continue;

            int s = Mathf.Clamp(b.startIndex, 0, 408);
            float x = f[s + 0] * b.scale.x + b.addOffset.x;
            float y = f[s + 1] * b.scale.y + b.addOffset.y;
            float z = f[s + 2] * b.scale.z + b.addOffset.z;

            x = Mathf.Clamp(x, -b.clampDeg.x, b.clampDeg.x);
            y = Mathf.Clamp(y, -b.clampDeg.y, b.clampDeg.y);
            z = Mathf.Clamp(z, -b.clampDeg.z, b.clampDeg.z);

            if (b.smooth > 0f)
            {
                var prevEuler = _prevEuler.TryGetValue(b.bone, out var p) ? p : Vector3.zero;
                float a = 1f - b.smooth;
                x = Mathf.Lerp(prevEuler.x, x, a);
                y = Mathf.Lerp(prevEuler.y, y, a);
                z = Mathf.Lerp(prevEuler.z, z, a);
                _prevEuler[b.bone] = new Vector3(x, y, z);
            }

            var delta = Quaternion.Euler(x, y, z);
            var bind = _bind.TryGetValue(b.bone, out var q) ? q : b.bone.localRotation;
            b.bone.localRotation = bind * delta;
        }

        _seqFrame++;
    }

    void BuildSequenceFromTokensEnumerable(IEnumerable<string> tokens)
    {
        var map = LoadGloss2AnimMapSafe();
        var list = new List<float[]>();
        int hold = Mathf.Max(0, Mathf.RoundToInt(interTokenHoldSeconds * fps));

        Debug.Log("[CsvRetargetPlayer] === Building animation sequence ===");

        foreach (var tk in tokens)
        {
            string file = null;
            if (map != null && map.TryGetValue(tk, out var mapped))
            {
                file = mapped;
                Debug.Log($"[Token] '{tk}' mapped to: {file}");
            }
            else
            {
                file = tk + "_normalized.csv";
                Debug.Log($"[Token] '{tk}' not mapped — using default: {file}");
            }

            string fullPath = Path.Combine(BaseDir, file);
            var f = LoadCsvSafe(fullPath);
            if (f == null || f.Length == 0)
            {
                Debug.LogWarning($"[CsvRetargetPlayer] Missing or empty CSV: '{file}' at path: {fullPath}");
                continue;
            }

            Debug.Log($"[CsvRetargetPlayer] Loaded CSV for token '{tk}': {f.Length} frames");

            list.AddRange(f);

            if (hold > 0)
            {
                var last = (float[])f.Last().Clone();
                for (int i = 0; i < hold; i++) list.Add(last);
            }
        }

        _sequenceFrames = list.ToArray();
        Debug.Log($"[CsvRetargetPlayer] Sequence built with total frames: {_sequenceFrames.Length}");
    }

    float[][] LoadCsvSafe(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("[CsvRetargetPlayer] Invalid path (empty or null).");
            return null;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
    try
    {
        // ✅ Android에서는 jar 내부 경로를 UnityWebRequest로 접근해야 함
        string androidPath = path;
        if (!path.StartsWith("jar"))
            androidPath = "jar:file://" + path;

        using (var www = UnityEngine.Networking.UnityWebRequest.Get(androidPath))
        {
            var operation = www.SendWebRequest();
            while (!operation.isDone) { }  // 간단한 동기 대기 (프레임 멈춤 방지용)

            if (www.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[CsvRetargetPlayer] Failed to load CSV via UnityWebRequest: {www.error}");
                return null;
            }

            var text = www.downloadHandler.text;
            var lines = text.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries)
                            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                            .ToArray();

            if (lines.Length == 0)
            {
                Debug.LogWarning($"[CsvRetargetPlayer] CSV file '{path}' is empty.");
                return null;
            }

            return lines
                .Select(l => l.Split(',')
                .Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray())
                .ToArray();
        }
    }
    catch (System.Exception ex)
    {
        Debug.LogError($"[CsvRetargetPlayer] Exception while loading CSV on Android: {ex.Message}");
        return null;
    }
#else
        // ✅ PC(Windows, macOS, Editor)에서는 기존 로직 유지
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[CsvRetargetPlayer] File not found: {path}");
            return null;
        }

        var lines = File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
            .ToArray();

        if (lines.Length == 0)
        {
            Debug.LogWarning($"[CsvRetargetPlayer] CSV file '{path}' is empty.");
            return null;
        }

        return lines
            .Select(l => l.Split(',')
            .Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray())
            .ToArray();
#endif
    }


    Dictionary<string, string> LoadGloss2AnimMapSafe()
    {
        try
        {
            var path = Path.Combine(Application.streamingAssetsPath, "gloss2anim.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning("[CsvRetargetPlayer] gloss2anim.json not found.");
                return null;
            }

            var json = File.ReadAllText(path);
            var wrapper = JsonUtility.FromJson<Wrapper>(json);
            if (wrapper == null || wrapper.entries == null)
            {
                Debug.LogWarning("[CsvRetargetPlayer] gloss2anim.json could not be parsed.");
                return null;
            }

            var dict = wrapper.entries.ToDictionary(e => e.key, e => e.path);
            Debug.Log($"[CsvRetargetPlayer] Loaded gloss2anim.json with {dict.Count} entries.");
            return dict;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CsvRetargetPlayer] Failed to load gloss2anim.json: {ex.Message}");
            return null;
        }
    }

    [System.Serializable]
    class Entry
    {
        public string key;
        public string path;
        public int fps;
        public string[] tags;
    }

    [System.Serializable]
    class Wrapper
    {
        public List<Entry> entries;
    }

    public void ResetPose()
    {
        Debug.Log("[CsvRetargetPlayer] ResetPose called");
        _sequenceFrames = null;
        _seqFrame = 0;

        foreach (var kvp in _bind)
            if (kvp.Key != null)
                kvp.Key.localRotation = kvp.Value;
    }

    public void Play()
    {
        Debug.Log("[CsvRetargetPlayer] Play called");
        _seqFrame = 0;
    }

    public void Pause()
    {
        Debug.Log("[CsvRetargetPlayer] Pause called");
        _sequenceFrames = null;
    }

    string[] MiniJsonToStrings(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        json = json.Trim();
        if (!json.StartsWith("[")) return null;
        var list = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(json, "\"([^\"]+)\""))
            list.Add(m.Groups[1].Value);
        return list.ToArray();
    }
}
