using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ChestCounter;

public sealed record CloudResult(bool Ok, string Message);

/// <summary>
/// Cloud save (Render server). Merge is by match: a match missing on one side is copied, a newer one wins.
/// Every await resumes on the UI thread, so the tracker's data is only touched from that thread.
/// </summary>
public sealed class CloudSync
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };
    private readonly Config _config;
    private readonly Store _store;
    private readonly TrackerData _data;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(100) }; // Render free wakes up in ~50 s
    private bool _busy;
    private string _lastFingerprint = "";

    public CloudSync(Store store, Config config, TrackerData data)
    {
        _store = store;
        _config = config;
        _data = data;
    }

    public bool IsLoggedIn => !string.IsNullOrEmpty(_config.CloudToken) && !string.IsNullOrEmpty(_config.CloudUrl);
    public string? User => _config.CloudUser;
    public bool Busy => _busy;
    public DateTime? LastSyncLocal { get; private set; }

    /// <summary>True when something changed locally since the last successful sync.</summary>
    public bool Dirty => IsLoggedIn && Fingerprint() != _lastFingerprint;

    private string Fingerprint() =>
        $"{_data.Matches.Count}|{_data.Matches.Sum(m => m.ChestsOpened)}|{(_data.Matches.Count > 0 ? _data.Matches.Max(m => m.AnalyzedAtUtc.Ticks) : 0)}|{_data.SessionStartUtc?.Ticks}";

    private static string NormalizeUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
        return url;
    }

    private static async Task<string> ReadError(HttpResponseMessage r)
    {
        try
        {
            var j = await r.Content.ReadFromJsonAsync<JsonElement>();
            if (j.TryGetProperty("error", out var e)) return e.GetString() ?? r.ReasonPhrase ?? "Erro";
        }
        catch { }
        return $"Erro {(int)r.StatusCode}";
    }

    public async Task<CloudResult> AuthAsync(string url, string user, string password, bool register)
    {
        try
        {
            url = NormalizeUrl(url);
            var r = await _http.PostAsJsonAsync($"{url}/api/{(register ? "register" : "login")}", new { username = user.Trim(), password });
            if (!r.IsSuccessStatusCode) return new CloudResult(false, await ReadError(r));
            var j = await r.Content.ReadFromJsonAsync<JsonElement>();
            _config.CloudUrl = url;
            _config.CloudUser = j.GetProperty("username").GetString();
            _config.CloudToken = j.GetProperty("token").GetString();
            _store.SaveConfig(_config);
            _lastFingerprint = "";
            return new CloudResult(true, register ? "Conta criada!" : "Login feito!");
        }
        catch (TaskCanceledException) { return new CloudResult(false, "O servidor demorou demais (pode estar acordando). Tente de novo em 1 minuto."); }
        catch (Exception ex) { return new CloudResult(false, "Sem conexão com o servidor: " + ex.Message); }
    }

    public void Logout()
    {
        _config.CloudToken = null;
        _store.SaveConfig(_config);
        _lastFingerprint = "";
    }

    private HttpRequestMessage Req(HttpMethod m, string path, object? body = null)
    {
        var req = new HttpRequestMessage(m, _config.CloudUrl + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.CloudToken);
        if (body != null) req.Content = JsonContent.Create(body, options: Json);
        return req;
    }

    private static long Ms(DateTime d) => (d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d).ToUniversalTime().Ticks / TimeSpan.TicksPerMillisecond;

    /// <summary>Downloads what is missing, uploads what the cloud lacks, and syncs the session start.</summary>
    public async Task<CloudResult> SyncAsync()
    {
        if (!IsLoggedIn) return new CloudResult(false, "Entre na sua conta primeiro.");
        if (_busy) return new CloudResult(false, "Sincronização em andamento...");
        _busy = true;
        try
        {
            // 1. Server index
            var ir = await _http.SendAsync(Req(HttpMethod.Get, "/api/matches/index"));
            if (ir.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Logout();
                return new CloudResult(false, "Sessão expirada. Entre novamente.");
            }
            if (!ir.IsSuccessStatusCode) return new CloudResult(false, await ReadError(ir));
            var remote = (await ir.Content.ReadFromJsonAsync<List<RemoteIndex>>() ?? new()).ToDictionary(x => x.Id, x => Ms(x.At));

            // 2. Download what is missing locally or newer in the cloud
            var local = _data.Matches.ToDictionary(m => m.SessionId, m => m, StringComparer.Ordinal);
            var toDownload = remote.Where(kv => !local.TryGetValue(kv.Key, out var l) || Ms(l.AnalyzedAtUtc) < kv.Value).Select(kv => kv.Key).ToList();
            var downloaded = 0;
            foreach (var chunk in toDownload.Chunk(40))
            {
                var gr = await _http.SendAsync(Req(HttpMethod.Post, "/api/matches/get", new { ids = chunk }));
                if (!gr.IsSuccessStatusCode) return new CloudResult(false, await ReadError(gr));
                var items = await gr.Content.ReadFromJsonAsync<List<MatchRecord>>(Json) ?? new();
                foreach (var m in items)
                {
                    _data.Matches.RemoveAll(x => x.SessionId == m.SessionId);
                    _data.Matches.Add(m);
                    downloaded++;
                }
            }

            // 3. Upload what the cloud lacks or has older
            local = _data.Matches.ToDictionary(m => m.SessionId, m => m, StringComparer.Ordinal);
            var toUpload = local.Values.Where(m => !remote.TryGetValue(m.SessionId, out var at) || Ms(m.AnalyzedAtUtc) > at).ToList();
            foreach (var chunk in toUpload.Chunk(20))
            {
                var pr = await _http.SendAsync(Req(HttpMethod.Put, "/api/matches", new { matches = chunk }));
                if (!pr.IsSuccessStatusCode) return new CloudResult(false, await ReadError(pr));
            }

            // 4. Session start: adopt cloud session on fresh install or newer reset; push local if newer.
            var sessionChanged = false;
            var sr = await _http.SendAsync(Req(HttpMethod.Get, "/api/state"));
            if (sr.IsSuccessStatusCode)
            {
                var st = await sr.Content.ReadFromJsonAsync<JsonElement>();
                DateTime? cloudStart = st.TryGetProperty("sessionStartUtc", out var sv) && sv.ValueKind == JsonValueKind.String ? sv.GetDateTime().ToUniversalTime() : null;
                var localStart = _data.SessionStartUtc.HasValue
                    ? (_data.SessionStartUtc.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(_data.SessionStartUtc.Value, DateTimeKind.Utc) : _data.SessionStartUtc.Value.ToUniversalTime())
                    : (DateTime?)null;

                if (cloudStart.HasValue && (!localStart.HasValue || cloudStart.Value > localStart.Value))
                {
                    _data.SessionStartUtc = cloudStart;
                    ReportWriter.InitSession(_data);
                    sessionChanged = true;
                }
                else
                {
                    if (!localStart.HasValue && _data.Matches.Count > 0)
                    {
                        ReportWriter.EnsureSessionSaved(_data);
                        localStart = _data.SessionStartUtc?.ToUniversalTime();
                        sessionChanged = localStart.HasValue;
                    }
                    if (localStart.HasValue && (!cloudStart.HasValue || localStart.Value > cloudStart.Value))
                    {
                        var sp = await _http.SendAsync(Req(HttpMethod.Put, "/api/state", new { sessionStartUtc = localStart.Value.ToString("O") }));
                        if (!sp.IsSuccessStatusCode) return new CloudResult(false, await ReadError(sp));
                    }
                }
            }

            if (downloaded > 0 || sessionChanged)
            {
                _store.SaveData(_data);
            }
            _lastFingerprint = Fingerprint();
            LastSyncLocal = DateTime.Now;
            var totalChests = _data.Matches.Sum(m => m.ChestsOpened);
            return new CloudResult(true, $"☁ Sincronizado: +{downloaded} baixadas, ↑{toUpload.Count} enviadas • {totalChests:N0} baús ({_data.Matches.Count} partidas)");
        }
        catch (TaskCanceledException) { return new CloudResult(false, "O servidor demorou demais (pode estar acordando). Vai tentar de novo."); }
        catch (Exception ex) { return new CloudResult(false, "Falha na sincronização: " + ex.Message); }
        finally { _busy = false; }
    }

    private sealed record RemoteIndex(string Id, DateTime At);
}
