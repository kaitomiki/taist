using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

sealed partial class Editor
{
    readonly TextBox console = new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,WordWrap=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(26,42,39),ForeColor=Color.White,Font=new Font("Consolas",10)};
    readonly TextBox stdin = new TextBox{Dock=DockStyle.Fill};
    WebView2 browser;
    Process running;
    int previewVersion;
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    static extern IntPtr LoadLibrary(string file);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool FreeLibrary(IntPtr handle);
    static bool loaderReady;
    static void ConfigureBrowser(){
        if(loaderReady)return;
        foreach(string arch in new[]{"arm64","x64"}){
            string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"browser",arch);
            IntPtr module=LoadLibrary(Path.Combine(dir,"WebView2Loader.dll"));
            if(module==IntPtr.Zero)continue;
            FreeLibrary(module);CoreWebView2Environment.SetLoaderDllFolderPath(dir);loaderReady=true;return;
        }
        throw new InvalidOperationException("ブラウザー部品を読み込めません。ZIP全体を展開してください。");
    }
    void InitPreview(TableLayoutPanel right)
    {
        console.Text="HTML・Python・JavaScript を保存して「プレビューする」を押してください。";
        previewHost.Controls.Add(console);
        var bar=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,78));
        bar.Controls.Add(stdin,0,0);var button=new Button{Text="入力送信",Dock=DockStyle.Fill};bar.Controls.Add(button,1,0);
        Row(right,bar,36);
        Action submit=()=>{
            if(running==null || running.HasExited)return;
            try{byte[] bytes=Encoding.UTF8.GetBytes(stdin.Text+"\n");running.StandardInput.BaseStream.Write(bytes,0,bytes.Length);running.StandardInput.BaseStream.Flush();Output(stdin.Text+"\r\n");stdin.Clear();}catch(IOException){}
        };
        button.Click+=(s,e)=>submit();stdin.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;submit();}};
        stdin.Enabled=false;
    }
    void Output(string value){LogText(value);if(outputTick==null)console.AppendText(value);else QueueOutput(consoleBuffer,value);}
    async Task Pump(StreamReader reader,int version,StringBuilder errors=null)
    {
        char[] buffer=new char[2048];int n;var turn=Stopwatch.StartNew();
        while((n=await reader.ReadAsync(buffer,0,buffer.Length))>0){if(version!=previewVersion||IsDisposed)return;string chunk=new string(buffer,0,n);Output(chunk);if(errors!=null){errors.Append(chunk);if(errors.Length>6000)errors.Remove(0,errors.Length-6000);}if(turn.ElapsedMilliseconds>=16){await Task.Delay(1);turn.Restart();}}
    }
    void StopPreview()
    {
        consoleBuffer.Clear();previewVersion++;ClearJavascript();if(browser!=null){browser.Dispose();browser=null;}
        if(running!=null){try{if(!running.HasExited)running.Kill();}catch(InvalidOperationException){}running=null;}
        stdin.Enabled=false;console.Visible=true;console.BringToFront();console.Text="";if(welcome!=null)welcome.BringToFront();
    }
    async Task PreviewFixed()
    {
        if(!loaded||!File.Exists(path)){status.Text="プレビューするファイルを先に保存してください。";return;}
        string ext=Path.GetExtension(path).ToLowerInvariant();
        if(ext!=".py" && ext!=".html" && ext!=".htm" && ext!=".js" && ext!=".mjs"){status.Text="HTML・Python・JavaScript ファイルを選んでください。";return;}
        if(ext==".py" && MessageBox.Show(this,"このPCで "+Path.GetFileName(path)+" を実行しますか？",Text,MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        await RunPreview(path,ext);
    }
    async Task RunPreview(string target,string ext)
    {
        StopPreview();console.BringToFront();int version=previewVersion;
        try{
            if(ext!=".py"){
                ConfigureBrowser();
                var view=new WebView2{Dock=DockStyle.Fill,CreationProperties=new CoreWebView2CreationProperties{UserDataFolder=Path.Combine(DataDir,"WebView")}};
                browser=view;previewHost.Controls.Add(view);view.BringToFront();await view.EnsureCoreWebView2Async(null);if(version!=previewVersion)return;
                view.CoreWebView2.NewWindowRequested+=(s,e)=>{e.Handled=true;};
                view.CoreWebView2.Settings.AreHostObjectsAllowed=false;
                if(createRoot!=null){
                    LayoutBrowser();
                    BrowserLog(view.CoreWebView2,"Runtime.consoleAPICalled");BrowserLog(view.CoreWebView2,"Runtime.exceptionThrown");
                    await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.enable","{}");if(version!=previewVersion)return;
                }
                if(ext==".js" || ext==".mjs")OpenJavascript(view,target);else view.CoreWebView2.Navigate(new Uri(target).AbsoluteUri);
            }else{
                var dir=AppDomain.CurrentDomain.BaseDirectory;
                var start=new ProcessStartInfo(Path.Combine(dir,"python","python.exe"),"-u \""+Path.Combine(dir,"preview.py")+"\" \""+target+"\""){
                    UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(target),
                    RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                using(var proc=Process.Start(start)){
                    string executed=Decode(ReadBytes(target));var errors=new StringBuilder();trace="";running=proc;stdin.Enabled=true;await Task.WhenAll(Pump(proc.StandardOutput,version),Pump(proc.StandardError,version,errors));
                    if(version!=previewVersion)return;
                    await Task.Run(()=>proc.WaitForExit());if(version==previewVersion){if(proc.ExitCode!=0){errorFile=target;errorCode=executed;trace=errors.ToString();status.Text="AIの解説 → エラーを調べる で相談できます。";topic.SelectedIndex=1;}Output("\r\n終了コード "+proc.ExitCode);FlushOutput();running=null;stdin.Enabled=false;}
                }
            }
        }catch(Exception ex){if(version==previewVersion&&!IsDisposed){StopPreview();console.BringToFront();LogText(ex.Message+"\r\n");console.Text="プレビューを開始できませんでした。\r\n"+ex.Message;}}
    }
}









