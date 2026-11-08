using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kroshka;

internal sealed class ReleaseCheck(Form owner, HeaderIcon button, Action closeForUpdate)
{
    const string Repo = "WintageOFFC/Kroshka";
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    static ReleaseCheck() => http.DefaultRequestHeaders.UserAgent.ParseAdd("Kroshka/"+Program.Version);
    Task<JsonDocument?>? pending;
    bool manual;
    static async Task<JsonDocument?> Latest()
    {
        using var response=await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        if(response.StatusCode==HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
    public async Task Check(bool interactive)
    {
        manual |= interactive;
        if(pending!=null) return;
        try {
            pending=Latest(); using var doc=await pending;
            if(owner.IsDisposed) return;
            var tag=doc?.RootElement.GetProperty("tag_name").GetString() ?? "";
            bool newer=Version.TryParse(tag.TrimStart('v'),out var release) && release>Version.Parse(Program.Version);
            button.UpdateAvailable=newer;button.Invalidate();
            if(!manual) return;
            if(!newer) {MessageBox.Show(owner,doc==null?"В репозитории пока нет релизов.":"Установлена актуальная версия "+Program.Version+".","Обновления");return;}
            var name="Kroshka-Setup-"+release+".exe";
            var asset=doc!.RootElement.GetProperty("assets").EnumerateArray().First(a=>a.GetProperty("name").GetString()==name);
            string url=$"https://github.com/{Repo}/releases/download/{Uri.EscapeDataString(tag)}/{name}";
            if(asset.GetProperty("browser_download_url").GetString()!=url) throw new InvalidDataException("Неожиданный адрес установщика.");
            if(MessageBox.Show(owner,$"Скачать и установить Крошку {release}?","Обновление",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            button.Enabled=false;
            string dir=Directory.CreateTempSubdirectory("Kroshka-").FullName, file=Path.Combine(dir,name);
            byte[] bytes=await http.GetByteArrayAsync(url);
            string digest="sha256:"+Convert.ToHexString(SHA256.HashData(bytes));
            if(!digest.Equals(asset.GetProperty("digest").GetString(),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Контрольная сумма загрузки не совпала.");
            await File.WriteAllBytesAsync(file,bytes);
            Process.Start(new ProcessStartInfo(file){UseShellExecute=true});
            closeForUpdate();
        } catch(Exception ex) { if(manual && !owner.IsDisposed)MessageBox.Show(owner,ex.Message,"Не удалось проверить или установить обновление"); }
        finally {pending=null;manual=false;if(!button.IsDisposed)button.Enabled=true;}
    }
}
