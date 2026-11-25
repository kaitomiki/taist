using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

sealed partial class Editor
{
    sealed class UserNotice : Exception { public UserNotice(string message) : base(message) {} }
    string roomUrl = "", lastLesson = "";
    const string GistApi = "https://api.github.com/gists";
    const string PromptFile = "classroom.txt";
    static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToritsuCode"); } }

    static string KeyValue(string supplied)
    {
        string text = supplied.Trim();
        if (text.Equals("Bearer", StringComparison.OrdinalIgnoreCase)) return "";
        if (text.Length > 6 && text.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase) && Char.IsWhiteSpace(text[6]))
            return text.Substring(7).Trim();
        return text;
    }

    static void Reserve(string key)
    {
        using (var sha = SHA256.Create())
        {
            string id = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "");
            using (var gate = new Mutex(false, @"Local\ToritsuCodeUsage" + id))
            {
                try { if (!gate.WaitOne(5000)) throw new UserNotice("ほかの送信の完了後に再試行してください。"); }
                catch (AbandonedMutexException) {}
                try
                {
                    Directory.CreateDirectory(DataDir);
                    string file = Path.Combine(DataDir, id + ".count");
                    int count = 0;
                    if (File.Exists(file) && (!Int32.TryParse(File.ReadAllText(file), out count) || count < 0))
                        throw new UserNotice("回数記録を読み取れません。新しいAPIキーに変更してください。");
                    if (count >= 500) throw new UserNotice("このAPIキーは500回に達しました。APIキーを変更してください。");
                    string tmp = file + ".tmp";
                    File.WriteAllText(tmp, (count + 1).ToString());
                    if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file);
                }
                finally { gate.ReleaseMutex(); }
            }
        }
    }

    static bool IsRoom(string url)
    {
        return Regex.IsMatch(url, @"\Ahttps://gist\.githubusercontent\.com/[a-zA-Z0-9-]+/[a-fA-F0-9]{32}/raw/classroom\.txt\z");
    }

    async Task<string> ReadLesson(string url)
    {
        using (var reply = await client.GetAsync(url))
        {
            if (!reply.IsSuccessStatusCode) throw new UserNotice("教室から指示を取得できません。HTTP " + (int)reply.StatusCode);
            string value = await reply.Content.ReadAsStringAsync();
            if (String.IsNullOrWhiteSpace(value) || value.Length > 8000) throw new UserNotice("教室の指示は1〜8000文字で入力してください。");
            return value;
        }
    }

    async Task<string> WithLesson(string input)
    {
        if (!classroomProject && !teachMode) return input;
        if (!IsRoom(roomUrl)) throw new UserNotice("Teachモードには先生の教室リンクが必要です。「AI設定」から参加してください。");
        string lesson;
        try { lesson = await ReadLesson(roomUrl); }
        catch (HttpRequestException) { throw new UserNotice("教室の配信サーバーに接続できません。"); }
        if (lastLesson != lesson) conversation = "";
        lastLesson = lesson;
        return "先生が設定した授業の指示を優先してください。\n" + lesson + "\n\n今回の依頼\n" + input;
    }

    async Task<string> Publish(string room, string credential, string lesson,string hint=null,Course settings=null)
    {
        if (credential.Length == 0 || String.IsNullOrWhiteSpace(lesson)) throw new UserNotice("先生用トークンと指示を入力してください。");
        string id = room.Length == 0 ? "" : "/" + new Uri(room).Segments[2].Trim('/');
        using (var req = new HttpRequestMessage(id.Length == 0 ? HttpMethod.Post : new HttpMethod("PATCH"), GistApi + id))
        {
            req.Headers.UserAgent.ParseAdd("ToritsuCode/2.0");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            var files = new Dictionary<string,object> { { PromptFile, new { content = lesson } } };
            if(settings!=null)files["course.json"]=new {content=Json.Serialize(settings)};
            if(hint!=null)files["hints.txt"]=new {content=String.IsNullOrWhiteSpace(hint)?DefaultHint:hint.Trim()};
            object data = id.Length == 0 ? (object)new { files = files, description = "授業の指示", @public = false } : new { files = files };
            req.Content = new StringContent(Json.Serialize(data), Encoding.UTF8, "application/json");
            using (var reply = await client.SendAsync(req))
            {
                if (!reply.IsSuccessStatusCode) throw new UserNotice("GitHub 配信エラー HTTP " + (int)reply.StatusCode + "。トークンと教室の所有者を確認してください。");
                var result = Json.Deserialize<Dictionary<string,object>>(await reply.Content.ReadAsStringAsync());
                string url = "https://gist.githubusercontent.com/" + StringField(ObjectField(result,"owner"),"login") + "/" + StringField(result,"id") + "/raw/" + PromptFile;
                if (!IsRoom(url)) throw new UserNotice("教室リンクを取得できません。");
                return url;
            }
        }
    }

    async Task<string> CreateArtwork(string description)
    {
            string key = KeyValue(token.Text);
            string input = ((teachMode || classroomProject)?lastLesson:"") + "\n" + description;
            using (var req = new HttpRequestMessage(HttpMethod.Post, "https://ai-api.metro.tokyo.lg.jp/api/v1/public/image"))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                req.Headers.Accept.ParseAdd("application/json");
                req.Content = new StringContent(Json.Serialize(new { input = input, conversation_id = "" }), Encoding.UTF8, "application/json");
                Reserve(key);
                using (var reply = await client.SendAsync(req))
                {
                    string body = await reply.Content.ReadAsStringAsync();
                    if (IsDisposed) throw new OperationCanceledException();
                    if (!reply.IsSuccessStatusCode) throw new UserNotice(ApiError((int)reply.StatusCode, body, key));
                    var parser = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
                    var image = ObjectField(parser.Deserialize<Dictionary<string,object>>(body),"image");
                    string ext = (StringField(image,"extension") ?? "png").ToLowerInvariant().TrimStart('.');
                    if (!Regex.IsMatch(ext, @"\A(png|jpe?g|webp|gif)\z")) throw new UserNotice("未対応の画像形式です。");
                    byte[] bytes = Convert.FromBase64String(StringField(image,"base64") ?? "");
                    if (bytes.Length == 0) throw new UserNotice("画像が空です。");
                    string name = "illustration-" + Guid.NewGuid().ToString("N").Substring(0,12) + "." + ext;
                    string target = Path.Combine(Path.GetDirectoryName(path), name);
                    reviewImagePath=target;reviewImage=bytes;
                    return name;
                }
            }
    }
}
