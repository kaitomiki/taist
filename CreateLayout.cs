using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

sealed class StudioCard : Panel
{
    public StudioCard(){Dock=DockStyle.Fill;Padding=new Padding(9);Margin=new Padding(4);DoubleBuffered=true;BackColor=Color.White;}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);var r=new Rectangle(0,0,Width-1,Height-1);if(r.Width<2||r.Height<2)return;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=Look.Round(r,12))using(var pen=new Pen(Color.FromArgb(227,232,241)))e.Graphics.DrawPath(pen,p);
    }
}
class CreateHero : Panel
{
    readonly Image art;
    public bool Classroom;
    public string StageTitle="何を作りたいですか？";
    public string NextAction="AIへのお願いを書いて、作品を作りましょう。";
    public CreateHero(){Dock=DockStyle.Fill;DoubleBuffered=true;art=Load("CreateHeader.png");}
    public static Image Load(string name){using(var stream=typeof(Editor).Assembly.GetManifestResourceStream(name)){if(stream==null)return null;using(var img=Image.FromStream(stream))return new Bitmap(img);}}
    protected override void Dispose(bool disposing){if(disposing&&art!=null)art.Dispose();base.Dispose(disposing);}
    protected override void OnPaintBackground(PaintEventArgs e){
        var r=ClientRectangle;if(r.Width<=0||r.Height<=0)return;
        using(var b=new LinearGradientBrush(r,Color.FromArgb(246,252,255),Color.FromArgb(235,229,253),6f))e.Graphics.FillRectangle(b,r);
        int artHeight=Height-46;int artWidth=artHeight*254/132;if(art!=null)e.Graphics.DrawImage(art,new Rectangle(Width-artWidth,0,artWidth,artHeight));
        using(var title=new Font("Segoe UI",24,FontStyle.Bold))
        using(var mid=new Font("Yu Gothic UI",12,FontStyle.Bold)){
            TextRenderer.DrawText(e.Graphics,Classroom?"Teach":"Create",title,new Point(17,9),Color.FromArgb(39,75,161));
            TextRenderer.DrawText(e.Graphics,StageTitle,mid,new Point(146,18),Look.Ink);
            TextRenderer.DrawText(e.Graphics,NextAction,Font,new Rectangle(148,44,Math.Max(50,Width-380),43),Color.FromArgb(86,101,132),TextFormatFlags.WordBreak);}
    }
}
class PreviewWelcome : Panel
{
    readonly Image art=CreateHero.Load("PreviewCard.png");
    public PreviewWelcome(){Dock=DockStyle.Fill;DoubleBuffered=true;BackColor=Color.FromArgb(239,244,253);}
    protected override void Dispose(bool disposing){if(disposing&&art!=null)art.Dispose();base.Dispose(disposing);}
    protected override void OnPaint(PaintEventArgs e){
        if(Width<10||Height<20)return;
        int w=Math.Min(Width-28,Math.Min(230,(int)(Height*.62)));int h=w*210/246,y=Math.Max(6,(Height-h-74)/2);
        if(art!=null)e.Graphics.DrawImage(art,(Width-w)/2,y,w,h);
        TextRenderer.DrawText(e.Graphics,"ここにプレビューを表示します",Font,new Rectangle(0,y+h+8,Width,27),Look.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics,"保存後に「実行」を押してください。",Font,new Rectangle(0,y+h+35,Width,26),Color.SlateGray,TextFormatFlags.HorizontalCenter);
    }
}
class LineRail : Control
{
    readonly SourceBox box;
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr a,IntPtr b);
    public LineRail(SourceBox target){box=target;Dock=DockStyle.Left;Width=42;DoubleBuffered=true;BackColor=Color.FromArgb(246,248,252);box.VScroll+=(s,e)=>Invalidate();box.TextChanged+=(s,e)=>Invalidate();box.Resize+=(s,e)=>Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        if(!box.IsHandleCreated)return;
        int first=(int)SendMessage(box.Handle,0xCE,IntPtr.Zero,IntPtr.Zero);
        int lines=(int)SendMessage(box.Handle,0xBA,IntPtr.Zero,IntPtr.Zero);
        for(int i=first;i<lines;i++){
            int index=box.GetFirstCharIndexFromLine(i);if(index<0)break;
            int y=box.GetPositionFromCharIndex(index).Y;if(y>Height)break;
            TextRenderer.DrawText(e.Graphics,(i+1).ToString(),box.Font,new Rectangle(0,y,Width-6,box.Font.Height+3),Color.SlateGray,TextFormatFlags.Right|TextFormatFlags.NoPadding);
        }
    }
}
sealed partial class Editor
{
    Button folderAction,manualEdit,stopAction;
    Panel createRoot;
    PreviewWelcome welcome;
    readonly TextBox activity=new TextBox{Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,ScrollBars=ScrollBars.Vertical,BackColor=Color.White,ForeColor=Look.Ink};
    int viewport;
    static StudioCard Card(TableLayoutPanel host,int col,int row,Control content){
        var card=new StudioCard();host.Controls.Add(card,col,row);card.Controls.Add(content);return card;
    }
    static Label Section(string text){return Look.Label(text,12,true);}
    static void Tint(Control parent){
        foreach(Control c in parent.Controls){var b=c as SoftButton;if(b!=null){b.Lavender=true;b.Invalidate();}Tint(c);}
    }
    void LogText(string text){
        if(createRoot!=null)QueueOutput(logBuffer,text);
    }
    void LayoutBrowser(){
        if(createRoot==null||browser==null||browser.IsDisposed)return;
        var a=previewHost.ClientRectangle;int w=viewport==0?a.Width:viewport==1?(int)(a.Width*.83):(int)(a.Width*.59);
        browser.Dock=DockStyle.None;browser.Bounds=new Rectangle((a.Width-w)/2,0,w,a.Height);
    }
    void BrowserLog(CoreWebView2 core,string eventName){
        var listener=core.GetDevToolsProtocolEventReceiver(eventName);
        listener.DevToolsProtocolEventReceived+=(s,e)=>{
            if(browser==null||browser.IsDisposed||browser.CoreWebView2!=core)return;
            try{var evt=Json.Deserialize<Dictionary<string,object>>(e.ParameterObjectAsJson);
                if(eventName.EndsWith("exceptionThrown")){
                    var d=ObjectField(evt,"exceptionDetails");LogText((StringField(ObjectField(d,"exception"),"description")??StringField(d,"text")??"JavaScript エラー")+"\r\n");
                }else{
                    object args;if(!evt.TryGetValue("args",out args)||!(args is IEnumerable))return;
                    var parts=new List<string>();
                    foreach(var obj in (IEnumerable)args){var item=obj as Dictionary<string,object>;object v;
                        if(item!=null)parts.Add(item.TryGetValue("value",out v)?Convert.ToString(v):StringField(item,"description")??"");}
                    LogText(String.Join(" ",parts)+"\r\n");
                }
            }catch(ArgumentException){}
        };
    }
    void SetupFileIcons(){
        var icons=new ImageList{ImageSize=new Size(19,19),ColorDepth=ColorDepth.Depth32Bit};
        string[] keys={"folder","html","js","py","file"};string[] symbols={"▰","<>","JS","Py","•"};
        Color[] colors={Color.FromArgb(102,146,227),Color.Coral,Color.Goldenrod,Color.SteelBlue,Color.SlateGray};
        using(var font=new Font("Segoe UI",7,FontStyle.Bold)){
            for(int i=0;i<keys.Length;i++){var bmp=new Bitmap(19,19);
                using(var g=Graphics.FromImage(bmp))using(var path=Look.Round(new Rectangle(1,2,17,15),3))using(var brush=new SolidBrush(colors[i])){
                    g.SmoothingMode=SmoothingMode.AntiAlias;g.FillPath(brush,path);
                    TextRenderer.DrawText(g,symbols[i],font,new Rectangle(0,0,19,19),Color.White,TextFormatFlags.NoPadding|TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter);}
                icons.Images.Add(keys[i],bmp);
            }
        }
        files.ImageList=icons;files.ImageKey=files.SelectedImageKey="folder";Disposed+=(s,e)=>icons.Dispose();
    }
    static string TreeIcon(FileSystemInfo info){
        if(info is DirectoryInfo)return "folder";switch(info.Extension.ToLowerInvariant()){case ".html":case ".htm":return "html";case ".js":case ".mjs":return "js";case ".py":return "py";default:return "file";}
    }
    static void FitTables(Control parent){
        var table=parent as TableLayoutPanel;
        if(table!=null && table.ColumnCount==1 && table.ColumnStyles.Count==0)table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(Control item in parent.Controls)FitTables(item);
    }
    void EnableCreateLayout(){
        if(createRoot!=null)return;
        SuspendLayout();
        var old=Controls[0];old.Visible=false;
        BackColor=Color.FromArgb(247,249,252);
        var screen=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,BackColor=BackColor,Padding=new Padding(6)};
        createRoot=screen;Controls.Add(screen);screen.BringToFront();
        var hero=new CreateHero{Classroom=teachMode};workspaceHero=hero;Row(screen,hero,143);
        var toolbar=Bar();toolbar.Dock=DockStyle.Bottom;toolbar.Height=46;toolbar.Padding=new Padding(8,0,0,0);toolbar.BackColor=Color.FromArgb(241,242,254);hero.Controls.Add(toolbar);
        AddButton(toolbar,"教室を変更",()=>ChangeRoom(this));
        create.Text="＋  新規ファイル";folderAction.Text="＋  新規フォルダー";setup.Text="⚙  AI設定";
        foreach(var c in new[]{create,folderAction,open,setup,reset})toolbar.Controls.Add(c);
        var help=AddButton(toolbar,"使い方を見る",()=>MessageBox.Show(this,teachMode?"① 教室の課題を読み、方針を相談する\n② 構成を確定する\n③ 一工程ずつ承認・実行する\n④ 解説で振り返る":"① 「AIへのお願い」に作りたい作品を書く\n② 変更案を確認し、承認して保存する\n③ 上部の ▶ から実行パネルを開き、作品を試す\n④ 中央でAIの解説を読む",teachMode?"Teach の使い方":"Create の使い方"));
        var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=2};
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,19));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,31));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,111));Row(screen,grid,0);
        var nav=Stack();var treeCard=Card(grid,0,0,nav);grid.SetRowSpan(treeCard,2);Row(nav,Section("プロジェクト"),35);Row(nav,files,0);
        SetupFileIcons();files.ItemHeight=27;files.ShowLines=false;files.FullRowSelect=true;files.BackColor=Color.White;
        Row(nav,Section("使い方のヒント"),30);
        hintText=new TextBox{ReadOnly=true,Multiline=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=Color.FromArgb(244,246,252),Text=CurrentHint()};Row(nav,hintText,84);
        var hintActions=Bar();AddButton(hintActions,teachMode?"ヒントを更新":"教室のヒント",async()=>await UpdateHint());Row(nav,hintActions,44);
        var middle=Stack();middle.Padding=Padding.Empty;grid.Controls.Add(middle,1,0);
        var prompt=Stack();var promptCard=new StudioCard();promptCard.Controls.Add(prompt);Row(middle,promptCard,160);
        Row(prompt,Section("AIへのお願い"),28);
        var promptField=new Panel{Dock=DockStyle.Fill,BackColor=Color.White};instruction.BorderStyle=BorderStyle.FixedSingle;promptField.Controls.Add(instruction);Row(prompt,promptField,0);
        var cue=new Label{Text="作りたいものや実現したいことを書いてください…",AutoSize=true,ForeColor=Color.SlateGray,BackColor=Color.White,Location=new Point(6,4)};promptField.Controls.Add(cue);cue.BringToFront();
        Action cueState=()=>cue.Visible=!instruction.Focused&&instruction.TextLength==0;cue.Click+=(s,e)=>instruction.Focus();
        instruction.GotFocus+=(s,e)=>cueState();instruction.LostFocus+=(s,e)=>cueState();instruction.TextChanged+=(s,e)=>cueState();cueState();
        var sendRow=new Panel{Dock=DockStyle.Fill};
        var count=Caption("");count.Dock=DockStyle.Right;count.Width=teachMode?0:105;count.TextAlign=ContentAlignment.MiddleRight;
        Action counter=()=>count.Text=instruction.TextLength+" / "+instruction.MaxLength;instruction.TextChanged+=(s,e)=>counter();counter();sendRow.Controls.Add(count);
        var commands=Bar();commands.Dock=DockStyle.Left;commands.Width=teachMode?310:185;commands.Controls.Add(send);if(teachMode)commands.Controls.Add(updatePlan);sendRow.Controls.Add(commands);Row(prompt,sendRow,44);
        var edit=Stack();var editCard=new StudioCard();editCard.Controls.Add(edit);Row(middle,editCard,0);
        Row(edit,PaneHeading("コードエディタ","code"),42);Row(edit,fileLabel,24);Row(edit,editorTabs,0);
        foreach(TabPage tab in editorTabs.TabPages){var code=tab.Controls.OfType<SourceBox>().First();tab.Controls.Add(new LineRail(code));}
        var review=Bar();foreach(var b in new[]{save,reject,manualEdit})review.Controls.Add(b);Row(edit,review,44);
        var side=Stack();var sideCard=Card(grid,2,0,side);Row(side,PaneHeading("実行・解説","preview"),40);Row(side,sideTabs,0);
        var prev=Stack();prev.BackColor=Color.White;var previewPage=sideTabs.TabPages[0];previewPage.Text="プレビュー";var previousPane=previewPage.Controls[0];previousPane.Visible=false;previewPage.Controls.Add(prev);prev.BringToFront();
        var widths=Bar();var buttons=new SoftButton[3];
        string[] labels={"PC","中幅","小幅"};string[] descriptions={"PCの表示幅","タブレットの表示幅","スマートフォンの表示幅"};
        for(int i=0;i<3;i++){int size=i;var choice=new SoftButton{Text=labels[i],AccessibleName=descriptions[i],MinimumSize=Size.Empty,Size=new Size(42,33),Margin=new Padding(1,4,1,0),Primary=i==0};buttons[i]=choice;
            choice.Click+=(s,e)=>{viewport=size;for(int j=0;j<3;j++){buttons[j].Primary=j==size;buttons[j].Invalidate();}LayoutBrowser();};widths.Controls.Add(choice);}
        previewAction.Text="実行";widths.Controls.Add(previewAction);Row(prev,widths,46);
        Row(prev,previewHost,0);previewHost.BackColor=Color.FromArgb(235,241,251);previewHost.Resize+=(s,e)=>LayoutBrowser();welcome=new PreviewWelcome();previewHost.Controls.Add(welcome);welcome.BringToFront();
        var inputRow=stdin.Parent;Row(prev,inputRow,34);Action showInput=()=>{prev.RowStyles[2].Height=stdin.Enabled?34:0;inputRow.Visible=stdin.Enabled;};stdin.EnabledChanged+=(s,e)=>showInput();showInput();
        var stops=Bar();stopAction.Text="プレビューを停止";stops.Controls.Add(stopAction);Row(prev,stops,44);
        var log=Stack();var logCard=Card(grid,1,1,log);grid.SetColumnSpan(logCard,2);
        var logTop=new Panel{Dock=DockStyle.Fill};var logTitle=PaneHeading("実行ログ","log");logTitle.Dock=DockStyle.Left;logTitle.Width=190;logTop.Controls.Add(logTitle);
        var clear=Bar();clear.Dock=DockStyle.Right;clear.Width=94;var clearAction=AddButton(clear,"クリア",()=>{logBuffer.Clear();activity.Clear();});logTop.Controls.Add(clear);Row(log,logTop,42);Row(log,activity,0);
        Row(screen,status,26);status.TextChanged+=(s,e)=>LogText(status.Text+"\r\n");
        AttachFocus(grid,treeCard,middle,promptCard,sideCard,logCard);
        if(!teachMode)ArrangeCreate(grid,middle,editCard,sideCard,logCard,prev,toolbar,help,clearAction);StartOutputBatch();StartHints();if(loaded&&root!=null)fileLabel.Text=Relative(path);FitTables(screen);Tint(screen);ResumeLayout(true);RefreshButtons();
    }
}
