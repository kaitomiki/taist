using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed partial class Editor
{
    CreateHero workspaceHero;
    readonly StringBuilder logBuffer=new StringBuilder(),consoleBuffer=new StringBuilder();
    Timer outputTick;
    void StartOutputBatch(){
        if(outputTick!=null)return;
        outputTick=new Timer{Interval=75};
        outputTick.Tick+=(s,e)=>FlushOutput();
        Disposed+=(s,e)=>outputTick.Dispose();
    }
    void QueueOutput(StringBuilder buffer,string text){
        if(String.IsNullOrEmpty(text))return;
        buffer.Append(text);if(buffer.Length>65536)buffer.Remove(0,buffer.Length-48000);
        if(outputTick!=null&&!outputTick.Enabled)outputTick.Start();
    }
    static void Drain(StringBuilder buffer,TextBox target,int cap){
        if(buffer.Length==0)return;
        string chunk=buffer.ToString();buffer.Clear();
        if(target.TextLength+chunk.Length>cap){target.Select(0,Math.Min(target.TextLength,target.TextLength+chunk.Length-cap+8000));target.SelectedText="";}
        target.AppendText(chunk);
    }
    void FlushOutput(){
        if(outputTick!=null)outputTick.Stop();
        Drain(logBuffer,activity,64000);Drain(consoleBuffer,console,128000);
    }
    TableLayoutPanel paneGrid;
    Control projectArea,editorArea,promptArea,previewArea,logArea;
    string focusedPane="";
    readonly Dictionary<string,Button> expand=new Dictionary<string,Button>();
    Panel PaneHeading(string text,string id){
        var row=new Panel{Dock=DockStyle.Fill};var title=Section(text);title.Dock=DockStyle.Fill;row.Controls.Add(title);
        var button=new SoftButton{Text="拡大",Width=86,Dock=DockStyle.Right,AutoSize=false,MinimumSize=Size.Empty,AccessibleName=text+"を拡大・復帰"};
        button.Click+=(s,e)=>FocusPane(focusedPane==id?"":id);row.Controls.Add(button);expand[id]=button;return row;
    }
    void AttachFocus(TableLayoutPanel grid,Control project,Control editor,Control prompt,Control preview,Control log){
        paneGrid=grid;projectArea=project;editorArea=editor;promptArea=prompt;previewArea=preview;logArea=log;
    }
    void FocusPane(string id){
        if(paneGrid==null)return;if(detachedRun){FocusCreate(id);return;}paneGrid.SuspendLayout();
        try{
            focusedPane=id;bool all=id=="";
            projectArea.Visible=all;editorArea.Visible=all||id=="code";promptArea.Visible=all;
            var middle=(TableLayoutPanel)editorArea;
            middle.RowStyles[0].Height=all?160:0;
            previewArea.Visible=all||id=="preview";logArea.Visible=all||id=="log";
            float[] widths=all?new float[]{19,50,31}:id=="preview"?new float[]{0,0,100}:new float[]{0,100,0};
            for(int i=0;i<3;i++)paneGrid.ColumnStyles[i].Width=widths[i];
            paneGrid.RowStyles[0].SizeType=id=="log"?SizeType.Absolute:SizeType.Percent;paneGrid.RowStyles[0].Height=id=="log"?0:100;
            paneGrid.RowStyles[1].SizeType=id=="log"?SizeType.Percent:SizeType.Absolute;paneGrid.RowStyles[1].Height=id=="log"?100:all?111:0;
            foreach(var item in expand)item.Value.Text=item.Key==id?"元に戻す":"拡大";
        }finally{paneGrid.ResumeLayout(true);}
        LayoutBrowser();
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys key){
        if(key==Keys.Escape && focusedPane!=""){FocusPane("");return true;}
        return base.ProcessCmdKey(ref msg,key);
    }

    TextBox hintText;
    string hintSource="",classHint="";
    const string DefaultHint="ファイルはダブルクリックで開きます。AIに頼むときは、作りたいものや修正点を具体的に書きましょう。";
    string CurrentHint(){return String.IsNullOrWhiteSpace(classHint)?DefaultHint:classHint;}
    async Task<string> FetchHint(string room){
        if(!IsRoom(room))throw new UserNotice("このアプリで発行した教室リンクを入力してください。");
        string url=room.Substring(0,room.Length-PromptFile.Length)+"hints.txt";
        using(var reply=await client.GetAsync(url)){
            if(reply.StatusCode==HttpStatusCode.NotFound)return "";
            if(!reply.IsSuccessStatusCode)throw new UserNotice("ヒントの取得に失敗しました。HTTP "+(int)reply.StatusCode);
            var value=(await reply.Content.ReadAsStringAsync()).Trim();
            if(value.Length>2000)throw new UserNotice("ヒントの上限は2000文字です。");
            return value;
        }
    }
    void SetHint(string url,string text){
        hintSource=url;classHint=text;if(hintText!=null)hintText.Text=CurrentHint();
    }
    Timer hintPoll;
    bool pollingHint;
    static string HintPreference{get{return Path.Combine(DataDir,"create-hint-link.txt");}}
    static string HintRoom(string value){
        var m=System.Text.RegularExpressions.Regex.Match(value.Trim(),@"^https://gist\.github\.com/([a-zA-Z0-9-]+)/([0-9a-fA-F]{32})/?(?:#.*)?$");
        if(m.Success)return "https://gist.githubusercontent.com/"+m.Groups[1].Value+"/"+m.Groups[2].Value+"/raw/"+PromptFile;
        const string suffix="/raw/hints.txt";
        if(value.EndsWith(suffix,StringComparison.Ordinal))return value.Substring(0,value.Length-"hints.txt".Length)+PromptFile;
        return value.Trim();
    }
    void SaveHintPreference(string value){
        if(!Directory.Exists(DataDir))Directory.CreateDirectory(DataDir);
        string temp=HintPreference+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,value);if(File.Exists(HintPreference))File.Replace(temp,HintPreference,null);else File.Move(temp,HintPreference);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    void StartHints(){
        if(!teachMode&&!classroomProject){
            try{string saved=File.Exists(HintPreference)?File.ReadAllText(HintPreference):"";if(IsRoom(saved))SetHint(saved,"");}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        hintPoll=new Timer{Interval=180000};hintPoll.Tick+=async(s,e)=>await PollHint();hintPoll.Start();
        Shown+=async(s,e)=>await PollHint();Disposed+=(s,e)=>hintPoll.Dispose();
    }
    async Task PollHint(){
        if(pollingHint||IsDisposed)return;
        string url=(teachMode||classroomProject)?roomUrl:hintSource;if(!IsRoom(url))return;
        pollingHint=true;
        try{string hint=await FetchHint(url);if(!IsDisposed && url==((teachMode||classroomProject)?roomUrl:hintSource))SetHint(url,hint);}
        catch(HttpRequestException){}catch(TaskCanceledException){}catch(UserNotice){}
        finally{pollingHint=false;}
    }
    async Task UpdateHint(){
        if(teachMode||classroomProject){
            try{var room=roomUrl;var text=await FetchHint(room);if(!IsDisposed&&room==roomUrl){SetHint(room,text);status.Text="ヒントを更新しました。";}}catch(Exception ex){if(!IsDisposed)status.Text=ex.Message;}
            return;
        }
        using(var f=new Steps("ヒントの配信元")){
            var p=Steps.Page();Steps.Add(p,Look.Label("先生の教室リンク、または Gist のリンク",12,true),50);
            var box=new TextBox{Text=hintSource,Dock=DockStyle.Fill};Steps.Add(p,box,42);
            Steps.Add(p,Look.Label("Gist の hints.txt を左欄に表示します。リンクの登録後は3分ごとに更新します。",11,false),64);
            f.ShowPage(p,"ヒントの配信元","自宅制作でも、先生が公開したヒントを使えます。");
            f.Button("標準に戻す",()=>{try{SaveHintPreference("");SetHint("","");f.Close();}catch(Exception ex){f.Note.Text=ex.Message;}},false);
            f.Button("取得して保存",async()=>{
                string url=HintRoom(box.Text);if(!IsRoom(url)){f.Note.Text="教室またはGitHub Gistのリンクを入力してください。";return;}
                f.Actions.Enabled=box.Enabled=false;
                try{var text=await FetchHint(url);SaveHintPreference(url);SetHint(url,text);status.Text="ヒントの配信元を保存しました。";f.DialogResult=DialogResult.OK;}
                catch(Exception ex){f.Note.Text=ex.Message;}finally{f.Actions.Enabled=box.Enabled=true;}
            },true);
            f.FormClosing+=(s,e)=>{if(!f.Actions.Enabled&&f.DialogResult!=DialogResult.OK)e.Cancel=true;};f.ShowDialog(this);
        }
    }
}
