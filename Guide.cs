using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

sealed partial class Editor
{
    Button setup, previewAction;
    Banner guidance;
    bool savedOnce;

    public bool StartStudent(){
        using(var flow=new Steps("生徒の制作準備")){
            var place=Steps.Page();
            var folder=Look.Label(root??"まだ選んでいません",11,false);
            Steps.Add(place,Look.Label("作品をどこに保存しますか？",16,true),54);
            Steps.Add(place,Look.Label("授業のフォルダーを選びます。新しい作品用のフォルダーも作れます。",11,false),52);
            var pick=new SoftButton{Text="保存するフォルダーを選ぶ",Dock=DockStyle.Left,Width=250};Steps.Add(place,pick,50);Steps.Add(place,folder,56);
            var kind=Steps.Page();Steps.Add(kind,Look.Label("何から始めますか？",16,true),45);
            var options=new ListBox{Dock=DockStyle.Fill,ItemHeight=38,IntegralHeight=false,BorderStyle=BorderStyle.FixedSingle};
            options.Items.AddRange(new object[]{"Webページを作る（HTML）","プログラムを作る（Python）","JavaScriptを作る（ブラウザーで実行）","すでにあるファイルを開く"});options.SelectedIndex=0;
            Steps.Add(kind,options,128);Steps.Add(kind,Look.Label("同じ名前のファイルがあれば、その内容から再開します。",11,false),50);
            var access=Steps.Page();
            var mode=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};mode.Items.AddRange(new object[]{"Create　自宅で作品をまとめて作る","Teach　先生の教室で段階的に学ぶ"});mode.SelectedIndex=startingInRoom?1:0;
            Steps.Add(access,Look.Label("制作モード",14,true),32);Steps.Add(access,mode,36);
            var keyLabel=Look.Label("都立AI APIキー（Createでは必須）",11,true);Steps.Add(access,keyLabel,32);
            var paste=new TextBox{Dock=DockStyle.Fill,Text=token.Text};Steps.Add(access,paste,40);
            var keyHint=Look.Label("",10,false);Steps.Add(access,keyHint,36);
            Button start=null;
            Action validateKey=()=>{
                bool missing=KeyValue(paste.Text).Length==0;
                keyLabel.Text="都立AI APIキー（必須）";
                keyHint.Text=missing?"都立AIの「API利用」で発行したキーを、この欄に貼り付けてください。":"キーは「Bearer 」付きでも入力できます。";
                if(start!=null&&!start.IsDisposed)start.Enabled=!missing&&(mode.SelectedIndex==0||IsRoom(roomUrl));
            };
            paste.TextChanged+=(s,e)=>validateKey();
            mode.SelectedIndexChanged+=(s,e)=>validateKey();
            var join=new SoftButton{Text="先生から受け取った教室リンクを登録",Dock=DockStyle.Left,Width=320};Steps.Add(access,join,52);
            join.Enabled=mode.SelectedIndex==1;mode.SelectedIndexChanged+=(s,e)=>join.Enabled=mode.SelectedIndex==1;
            join.Click+=(s,e)=>{Join(flow);if(roomUrl.Length>0)flow.Note.Text=course.Title+"の教室に参加しました。";validateKey();};
            string selected=root, target=null;
            Action showFolder=null,showFileType=null,showMode=null,finish=null;
            showFolder=()=>{flow.ShowPage(place,"2 / 3　保存場所","作品に使うフォルダーをひとつ選びます。");
                flow.Button("戻る",showMode,false);var next=flow.Button("次へ",()=>showFileType(),true);next.Enabled=selected!=null;flow.AcceptButton=next;
                pick.Tag=next;};
            pick.Click+=(s,e)=>{using(var d=new FolderBrowserDialog{Description="作品の保存場所を選んでください",SelectedPath=selected??"",ShowNewFolderButton=true}){
                if(d.ShowDialog(flow)==DialogResult.OK){selected=Path.GetFullPath(d.SelectedPath);folder.Text=selected;((Button)pick.Tag).Enabled=true;}}};
            showFileType=()=>{flow.ShowPage(kind,"3 / 3　作品の種類","作りたいものを選びます。");flow.Button("戻る",showFolder,false);flow.AcceptButton=flow.Button("制作を始める",()=>{
                try{string name=options.SelectedIndex==0?"index.html":options.SelectedIndex==2?"main.js":"main.py";target=Path.Combine(selected,name);
                    if(options.SelectedIndex==3){using(var d=new OpenFileDialog{InitialDirectory=selected}){if(d.ShowDialog(flow)!=DialogResult.OK)return;target=d.FileName;}}
                    if(!Path.GetFullPath(target).StartsWith(selected.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("手順2で選んだフォルダーの中から選んでください。");
                    finish();
                }catch(Exception ex){flow.Note.Text=ex.Message;}},true);};
            finish=()=>{try{
                if(target==null && mode.SelectedIndex==0){showFolder();return;}
                if(KeyValue(paste.Text).Length==0){validateKey();paste.Focus();return;}
                if(mode.SelectedIndex==1 && (!IsRoom(roomUrl)||String.IsNullOrWhiteSpace(lastLesson))){flow.Note.Text="先生の教室リンクを登録し、授業の指示を受信してください。";return;}
                classroomProject=mode.SelectedIndex==1 && course.mode=="project";teachMode=mode.SelectedIndex==1 && !classroomProject;
                if(classroomProject && target==null){showFolder();return;}
                if(teachMode){root=selected;token.Text=paste.Text.Trim();flow.DialogResult=DialogResult.OK;return;}Text="都立AI スタジオ  |  "+(teachMode?"Teach・授業":classroomProject?"Teach・作品制作":"Create・自由制作");root=selected;string checkedPath=ProjectPath(Relative(target));
                if(!File.Exists(checkedPath)){if(options.SelectedIndex==3)throw new IOException("ファイルをもう一度選んでください。");Commit(checkedPath,null,new byte[0]);}
                byte[] data=ReadBytes(checkedPath);string value=Decode(data);SetFile(checkedPath,data,value);token.Text=paste.Text.Trim();EnableCreateLayout();RefreshTree();
                flow.DialogResult=DialogResult.OK;
            }catch(Exception ex){flow.Note.Text=ex.Message;}};
            showMode=()=>{flow.ShowPage(access,"Create または Teach を選ぶ","Teach は先生の教室を選ぶと、その授業に進めます。");flow.Button("ドリル成績",()=>ShowScores(flow),false);start=flow.Button("次へ",()=>{if(mode.SelectedIndex==1 && course.mode!="project")finish();else showFolder();},true);flow.AcceptButton=start;validateKey();};
            showMode();if(flow.ShowDialog()!=DialogResult.OK)return false;if(teachMode){RunCourse();return false;}return true;
        }
    }
    void Settings(){
        if(busy)return;
        using(var f=new Steps("AIと教室の設定")){
            var p=Steps.Page();Steps.Add(p,Look.Label("都立AI APIキー",14,true),44);var box=new TextBox{Text=token.Text,Dock=DockStyle.Fill};Steps.Add(p,box,40);
            var c=new CheckBox{Text="ほかのソースファイルもAIに送る",Checked=context.Checked,Dock=DockStyle.Fill};Steps.Add(p,c,40);
            var i=new CheckBox{Text="作品に必要なときは挿絵を生成する",Checked=illustrations.Checked,Dock=DockStyle.Fill};Steps.Add(p,i,40);
            f.ShowPage(p,"AIと教室の設定","設定を保存すると、次の依頼から使えます。");
            if(teachMode)f.Button("教室に参加・退出",()=>Join(f),false);var apply=f.Button("設定を保存",()=>{if(!teachMode&&KeyValue(box.Text).Length==0)return;token.Text=box.Text;context.Checked=c.Checked;illustrations.Checked=i.Checked;f.Close();},true);
            Action validate=()=>{apply.Enabled=teachMode||KeyValue(box.Text).Length>0;f.Note.Text=apply.Enabled?"":"CreateではAPIキーが必須です。新しいキーを貼り付けてください。";};
            box.TextChanged+=(s,e)=>validate();validate();f.ShowDialog(this);
        }
    }
    void Guide(){
        if(guidance==null)return;
        string title,detail;
        if(busy){title="AIが作業中です";detail="結果が届いたら、変更案や解説を確認できます。";}
        else if(!loaded){title="作業ファイルを選ぼう";detail="左のファイルをダブルクリックするか、「新規ファイル」を選びます。";}
        else if(hasProposal){title="変更案を確認しよう";detail="中央でコードを確認し、「承認」で保存。「却下」で修正したいことを伝えられます。";}
        else if(needsRevision){title="修正点を伝えよう";detail="「AIへのお願い」に変えたい点を書き、もう一度生成します。";}
        else if(phase==Phase.Finished){title="作品が完成しました";detail=teachMode?"右の「実行」で試し、解説で振り返ります。":"上部の ▶ で実行パネルを開きます。中央で学んだことを振り返れます。";}
        else if(KeyValue(token.Text).Length==0){title="AIを使う準備";detail="「AI設定」に都立AIのAPIキーを貼り付けてください。";}
        else if(!teachMode){title="何を作りたいですか？";detail="中央の「AIへのお願い」に希望を書く → 作品をまとめて作る → 承認して実行";}
        else if(phase==Phase.Idea){title="① 課題の方針を相談";detail="左のヒントを読み、作りたいものを中央の欄に書いて「① 方針を相談」を押します。";}
        else if(phase==Phase.Plan){title="① 方針を確認";detail="右の解説を読み、「構成案に進む」へ。方針を変える場合は中央に書いて「方針に追記」。";}
        else if(phase==Phase.Structure){title="② 構成を確定";detail="ファイルと工程を読んで「構成を確定して実装」を選びます。";}
        else{title="③ 一工程ずつ実装　"+(cursor+1)+" / "+sequence.Count;detail="保存版を右の「実行」で試し、確認できたら中央で次の一工程を作ります。";}
        if(guidance.Text!=title||guidance.Detail!=detail){guidance.Text=title;guidance.Detail=detail;guidance.Invalidate();}
        if(workspaceHero!=null&&(workspaceHero.NextAction!=detail||workspaceHero.StageTitle!=title)){
            workspaceHero.StageTitle=title;workspaceHero.NextAction=detail;workspaceHero.Invalidate();
        }
    }
}
