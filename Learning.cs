using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

class AuroraHeader : Panel {
    public AuroraHeader(){Dock=DockStyle.Fill;DoubleBuffered=true;Padding=new Padding(13,1,8,1);BackColor=Color.White;}
    protected override void OnPaintBackground(PaintEventArgs e){
        base.OnPaintBackground(e);var r=new Rectangle(1,1,Width-3,Height-3);if(r.Width<4||r.Height<4)return;
        e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var p=Look.Round(r,16))using(var b=new System.Drawing.Drawing2D.LinearGradientBrush(r,Color.White,Color.White,12f)){
            b.InterpolationColors=new System.Drawing.Drawing2D.ColorBlend{Positions=new[]{0f,.46f,1f},Colors=new[]{Color.FromArgb(219,245,247),Color.FromArgb(235,228,252),Color.FromArgb(255,231,221)}};
            e.Graphics.FillPath(b,p);
        }
    }
}
sealed partial class Editor
{
    enum Phase { Idea, Plan, Structure, Build, Finished }
    sealed class WorkStep {public string File; public string Task;}
    Phase phase;
    readonly List<WorkStep> sequence=new List<WorkStep>();
    string objective="", strategy="", design="", explanation="", lastCoachCode="", lastHint="", errorFile="", errorCode="", trace="";
    int cursor,hintLevel;
    bool switching, stagedImplementation;
    readonly TabControl sideTabs=new TabControl{Dock=DockStyle.Fill};
    readonly RichTextBox reading=new RichTextBox{Dock=DockStyle.Fill,ReadOnly=true,BackColor=Color.White,BorderStyle=BorderStyle.None,DetectUrls=false,Font=new Font("Yu Gothic UI",12)};
    readonly ComboBox topic=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    readonly Label readingTitle=Look.Label("最初の一歩",15,true);
    Button ask;
    dynamic voice;
    void BuildLearning(TableLayoutPanel body,TableLayoutPanel preview)
    {
        var previewPage=new TabPage("実行プレビュー");previewPage.Controls.Add(preview);
        var learn=new TabPage("AIの解説");var panel=Stack();panel.Padding=new Padding(10);panel.BackColor=Color.White;learn.Controls.Add(panel);
        sideTabs.TabPages.Add(previewPage);sideTabs.TabPages.Add(learn);body.Controls.Add(sideTabs,2,0);
        var glow=new Panel{Dock=DockStyle.Fill};glow.Paint+=(s,e)=>{
            var r=glow.ClientRectangle;if(r.Width<2)return;
            using(var b=new System.Drawing.Drawing2D.LinearGradientBrush(r,Color.MediumSlateBlue,Color.Coral,0f)){
                var palette=new System.Drawing.Drawing2D.ColorBlend{Positions=new[]{0f,.35f,.7f,1f},Colors=new[]{Color.FromArgb(100,190,217),Color.FromArgb(141,117,223),Color.FromArgb(224,143,192),Color.FromArgb(239,186,146)}};
                b.InterpolationColors=palette;e.Graphics.FillRectangle(b,r);
            }};
        Row(panel,glow,9);var cover=new AuroraHeader();readingTitle.BackColor=Color.Transparent;cover.Controls.Add(readingTitle);Row(panel,cover,56);Row(panel,reading,0);
        var tools=Bar();AddButton(tools,"読み上げ",Speak);AddButton(tools,"音声停止",StopSpeech);Row(panel,tools,44);
        Row(panel,Caption("学習サポート"),23);
        topic.Items.AddRange(new object[]{"コードの意味を知る","エラーを調べる","問題を小さく分ける","ヒントを一つもらう","完成例を見る"});topic.SelectedIndex=0;Row(panel,topic,34);
        var actions=Bar();ask=AddButton(actions,"AIに聞く",async()=>await Coach());reflect=AddButton(actions,"振り返り",async()=>{if(reflection.Length>0)WriteNote("この制作で学べたこと",reflection);else await Reflect();});Row(panel,actions,44);
        WriteNote("学習ノート","コードの意味やエラーを一緒に考えます。下から相談内容を選んでください。",false);
        Disposed+=(s,e)=>StopSpeech();
    }
    void WriteNote(string title,string text,bool select=true)
    {
        StopSpeech();explanation=text;readingTitle.Text=title;reading.Text=DisplayText(text.Trim());reading.SelectAll();reading.SelectionIndent=5;reading.SelectionRightIndent=6;reading.Select(0,0);
        if(select)sideTabs.SelectedIndex=1;
    }
    void StopSpeech(){try{if(voice!=null)voice.Speak("",3);}catch{}}
    void Speak(){try{if(voice==null){voice=Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice",true));dynamic ja=voice.GetVoices("Language=411","");if(ja.Count>0)voice.Voice=ja.Item(0);}voice.Speak(explanation,19);}catch{status.Text="Windowsの音声を開始できません。音声設定を確認してください。";}}
    void ResetFlow(){
        ResetReview();accepted.Clear();reflection="";phase=Phase.Idea;sequence.Clear();cursor=0;strategy=design=objective="";stagedImplementation=false;
        lastCoachCode=lastHint=trace=errorFile=errorCode="";hintLevel=0;
        if(sideTabs.TabPages.Count>0)WriteNote(teachMode?"学習ノート":"制作の振り返り",teachMode?"先生の授業内容に沿って方針を提案します。":"ここに完成した作品の仕組みと学べたことが表示されます。",false);
    }
    string StepList(){return String.Join("\n",sequence.Select((s,i)=>(i+1)+". "+s.File+"　"+s.Task));}
    void ShowWorkflow(){
        if(phase==Phase.Idea)WriteNote("① 方針","作りたいものを「AIへのお願い」に入力します。");
        else if(phase==Phase.Plan)WriteNote("① 方針",strategy);
        else WriteNote("②-1 構成と実装の順番",design+"\n\n"+StepList());
    }
    async Task<string> AskText(string input)
    {
        string key=KeyValue(token.Text);
        using(var req=new HttpRequestMessage(HttpMethod.Post,Endpoint)){
            req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);req.Headers.Accept.ParseAdd("application/json");
            req.Content=new StringContent(Json.Serialize(new{input=await WithLesson(input),conversation_id=""}),Encoding.UTF8,"application/json");
            Reserve(key);
            using(var reply=await client.SendAsync(req)){
                var body=await reply.Content.ReadAsStringAsync();
                if(!reply.IsSuccessStatusCode)throw new UserNotice(ApiError((int)reply.StatusCode,body,key));
                var msg=StringField(Json.Deserialize<Dictionary<string,object>>(body),"message");
                if(String.IsNullOrWhiteSpace(msg))throw new UserNotice("AIの回答が空です。もう一度お試しください。");
                return msg.Trim();
            }
        }
    }
    static string StripFence(string s){
        if(!s.StartsWith("```"))return s;
        int n=s.IndexOf('\n'),end=s.LastIndexOf("```");if(n<0||end<=n)throw new FormatException();
        return s.Substring(n+1,end-n-1).Trim();
    }
    async Task Advance()
    {
        if(busy||hasProposal)return;
        if(phase==Phase.Finished){ResetFlow();conversation="";RefreshButtons();instruction.Focus();return;}
        if(!teachMode){await Generate();return;}
        if(phase==Phase.Structure){
            if(MessageBox.Show(this,design+"\n\n"+StepList()+"\n\nこの構成で、最初の工程を実装しますか？", "②-1 構成の確定",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            phase=Phase.Build;
        }
        if(phase==Phase.Build){
            try{
                var step=sequence[cursor];string target=ProjectPath(step.File);
                if(!String.Equals(target,path,StringComparison.OrdinalIgnoreCase)){
                    var bytes=File.Exists(target)?ReadBytes(target):null;switching=true;try{SetFile(target,bytes,bytes==null?"":Decode(bytes));}finally{switching=false;}
                }
                await Generate();
            }catch(Exception ex){status.Text=ex.Message;}finally{RefreshButtons();}
            return;
        }
        try{
            if(!ConfirmContext())return;busy=true;RefreshButtons();
            string details=Json.Serialize(new{file=Relative(path),code=source,request=instruction.Text,related=shared.ToDictionary(x=>Relative(x.Key),x=>Decode(x.Value))});
            if(phase==Phase.Idea){
                string answer=await AskText("初学者の制作相談。①方針だけ。コードは提示せず、作品の目標、最小の動作、試し方を日本語で短く示す。\n"+details);
                objective=instruction.Text;instruction.Clear();strategy=answer;phase=Phase.Plan;ShowWorkflow();
            }else{
                string answer=await AskText("②-1 構成案。コードは書かない。方針と依頼を実装・検証しやすい小さな工程に分ける。一工程につき一つのファイルと一つの変更。最初は選択ファイル。アプリ制作では動く最小例と機能追加を別工程にし、全機能を一工程にまとめない。JSONのみ {\"summary\":\"構成の説明\",\"steps\":[{\"path\":\"プロジェクト相対パス\",\"task\":\"作業と確認方法\"}]}。1〜12工程。\n"+objective+"\n"+strategy+"\n"+details);
                var obj=Json.Deserialize<Dictionary<string,object>>(StripFence(answer));object value;
                var items=obj.TryGetValue("steps",out value)?value as IEnumerable:null;
                var steps=new List<WorkStep>();string summary=StringField(obj,"summary");
                if(items==null || String.IsNullOrWhiteSpace(summary))throw new FormatException();
                foreach(var item in items){var row=item as Dictionary<string,object>;var file=StringField(row,"path");var task=StringField(row,"task");ProjectPath(file);
                    if(String.IsNullOrWhiteSpace(task)||steps.Count>=12)throw new FormatException();steps.Add(new WorkStep{File=file,Task=task});}
                if(steps.Count==0)throw new FormatException();
                sequence.Clear();sequence.AddRange(steps);design=summary;cursor=0;phase=Phase.Structure;ShowWorkflow();
            }
        }catch(Exception ex){status.Text=ex is FormatException?"構成を読めませんでした。もう一度お試しください。":ex.Message;}
        finally{busy=false;RefreshButtons();}
    }
    async Task Coach(){
        if(busy||!loaded)return;
        if(KeyValue(token.Text).Length==0){status.Text="「AI設定」にAPIキーを入力してください。";return;}
        string code=hasProposal?proposal.Text:source;int mode=topic.SelectedIndex;
        bool actual=mode==1&&errorFile==path&&errorCode==source&&trace.Length>0;
        if(actual)code=source;
        if(MessageBox.Show(this,Path.GetFileName(path)+" のコードと依頼文"+(actual?"、直近のエラー出力":"")+"を都立AIに送りますか？","学習サポート",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        if(lastCoachCode!=code){hintLevel=0;lastHint="";lastCoachCode=code;}
        var directions=new[]{"代表的な行を引用し、入力例、途中の値、結果とその理由を示す。","エラーの行、原因の候補、自分で確かめる手順を説明。実行記録がなければ推測と明記。","課題を入力・処理・出力と小さな確認作業に分ける。","ヒントを一つだけ。最初は問いかけ、次は注目する行、以降はより具体的な手がかり。完成コードは出さない。","ユーザーが希望した学習用の小さな完成例と、動く理由、試す入力を示す。"};
        try{busy=true;RefreshButtons();status.Text="学習サポートが回答中…";
            string text=await AskText("初心者の学習コーチ。日本語で短い段落に分ける。"+directions[mode]+"\n"+Json.Serialize(new{file=Path.GetFileName(path),code=code,question=instruction.Text,stderr=actual?trace:"",hintNumber=hintLevel+1,previousHint=mode==3?lastHint:""}));
            if(mode==3){hintLevel++;lastHint=text;}WriteNote(mode==3?"ヒント "+hintLevel:topic.Text,text);status.Text="解説を表示しました。";
        }catch(Exception ex){status.Text=ex.Message;}finally{busy=false;RefreshButtons();}
    }
    void StageButtons(){
        send.Text=phase==Phase.Idea?"① 方針を相談":phase==Phase.Plan?"構成案に進む":phase==Phase.Structure?"構成を確定して実装":phase==Phase.Build?"次の一工程を作る":"新しい制作へ";
        send.Enabled=!busy&&!hasProposal&&loaded&&(phase==Phase.Finished||(KeyValue(token.Text).Length>0&&(phase!=Phase.Idea||instruction.Text.Trim().Length>0)));
        if(!teachMode)send.Text=phase==Phase.Finished?"新しい作品へ":needsRevision?"修正して再生成":"作品をまとめて作る";
        if(!teachMode)send.Enabled=!busy&&!hasProposal&&loaded&&(phase==Phase.Finished||(KeyValue(token.Text).Length>0&&instruction.Text.Trim().Length>0));
        if(teachMode && needsRevision && String.IsNullOrWhiteSpace(instruction.Text))send.Enabled=false;
        if(teachMode && phase==Phase.Plan && instruction.Text.Trim().Length>0)send.Enabled=false;
        if(reject!=null)reject.Enabled=!busy&&hasProposal;
        if(updatePlan!=null){updatePlan.Visible=teachMode&&phase==Phase.Plan;updatePlan.Enabled=!busy&&instruction.Text.Trim().Length>0;}
        if(reflect!=null){reflect.Visible=phase==Phase.Finished;reflect.Enabled=!busy;}
        if(ask!=null){ask.Visible=topic.Visible=teachMode;ask.Enabled=!busy&&loaded&&teachMode;}
    }
}



