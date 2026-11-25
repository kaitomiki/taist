using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

sealed class Roles : Form
{
    public Roles()
    {
        Text = "都立AI スタジオ"; Font = new Font("Yu Gothic UI", 11); Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition=FormStartPosition.CenterScreen;ClientSize=new Size(680,368);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;BackColor=Look.Paper;
        Controls.Add(new Banner{Text="都立AI スタジオ",Detail="作品づくりと、授業の教室管理",Dock=DockStyle.None,Location=new Point(24,24),Size=new Size(632,104)});
        Controls.Add(new Label{Text="どちらで始めますか？",Location=new Point(28,158),AutoSize=true,ForeColor=Look.Ink,Font=new Font(Font.FontFamily,16,FontStyle.Bold)});
        Role("生徒として始める\n作品を作る",24,false);Role("先生として始める\n教室を管理する",350,true);
    }
    void Role(string text,int left,bool teacher)
    {
        var button=new SoftButton{Text=text,Primary=!teacher,Location=new Point(left,220),Size=new Size(306,100),Font=new Font(Font.FontFamily,14,FontStyle.Bold)};
        Controls.Add(button);
        button.Click+=(s,e)=> {Hide(); try { if(teacher){using(var editor=new Editor(false))editor.Teach();}else Editor.StudentSession(this); } finally {Show();} };
    }
}

sealed partial class Editor
{
    string root,createReply;
    readonly TreeView files = new TreeView {Dock=DockStyle.Fill,HideSelection=false,BorderStyle=BorderStyle.None};
    readonly Panel previewHost = new Panel {Dock=DockStyle.Fill,BackColor=Color.White};
    readonly CheckBox illustrations = new CheckBox {Text="必要なら挿絵も作る",AutoSize=true,Margin=new Padding(8,10,0,0),Checked=true};
    readonly TabControl editorTabs = new TabControl {Dock=DockStyle.Fill};
    readonly Dictionary<string,byte[]> shared = new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,byte[]> staged = new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
    readonly CheckBox context = new CheckBox { Text="関連コードも送る",AutoSize=true };
    readonly Queue<KeyValuePair<string,string>> pending = new Queue<KeyValuePair<string,string>>();
    static readonly Color Green = Look.Accent;
    static TableLayoutPanel Stack() {return new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,Padding=new Padding(5)};}
    static Label Heading(string value) {var l=Caption(value); l.ForeColor=Green; l.Font=new Font("Yu Gothic UI",12,FontStyle.Bold);return l;}
    static FlowLayoutPanel Bar() {return new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};}
    void BuildStudio()
    {
        Text="都立AI スタジオ  |  生徒の制作室"; Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font=new Font("Yu Gothic UI",10); BackColor=Color.FromArgb(242,247,244);
        StartPosition=FormStartPosition.CenterScreen; MinimumSize=new Size(950,690); ClientSize=new Size(1230,760);
        var page=Stack();page.Padding=new Padding(12);Controls.Add(page);
        guidance=new Banner();Row(page,guidance,78);
        var top=Bar(); top.Controls.Add(new Label {Text="制作室",AutoSize=true,Font=new Font(Font.FontFamily,17,FontStyle.Bold),ForeColor=Green,Margin=new Padding(4,0,18,0)});
        open=AddButton(top,"フォルダーを開く",ChooseRoot);
        create=AddButton(top,"新規ファイル",NewProjectFile);
        folderAction=AddButton(top,"新規フォルダー",NewDirectory);
        setup=AddButton(top,"AI設定",Settings);
        reset=AddButton(top,"方針からやり直す",()=>{if(!busy && !hasProposal){conversation="";ResetFlow();RefreshButtons();}});
        Row(page,top,48);
        var body=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,195)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));
        Row(page,body,0);
        var nav=Stack();nav.BackColor=Color.FromArgb(227,239,232);body.Controls.Add(nav,0,0);
        Row(nav,Heading("プロジェクト"),36);Row(nav,files,0);
        files.BeforeExpand+=(s,e)=> {if(e.Node.Nodes.Count==1 && e.Node.Nodes[0].Tag==null) Populate(e.Node);};
        files.NodeMouseDoubleClick+=(s,e)=>OpenNode(e.Node); files.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter)OpenNode(files.SelectedNode);};
        var center=Stack();body.Controls.Add(center,1,0);
        Row(center,Heading("AIへのお願い"),34);instruction.Dock=DockStyle.Fill;Row(center,instruction,64);
        var promptBar=Bar();send=AddButton(promptBar,"作品を作る",async()=>{savedOnce=false;await Advance();});((SoftButton)send).Primary=true;
        updatePlan=AddButton(promptBar,"方針に追記",async()=>await Refine());Row(center,promptBar,46);
        Row(center,Heading("コード"),34); Row(center,fileLabel,28);
        foreach(var pair in new[]{new {Title="現在のファイル",Box=original},new {Title="変更案・編集",Box=proposal}}) {
            var tab=new TabPage(pair.Title);tab.Controls.Add(pair.Box);editorTabs.TabPages.Add(tab);
        }
        Row(center,editorTabs,0);
        var review=Bar();save=AddButton(review,"承認",async()=>await Approve());reject=AddButton(review,"却下",Reject);
        manualEdit=AddButton(review,"自分で編集",()=>{if(!busy && loaded && !hasProposal){proposal.Text=original.Text;hasProposal=true;editorTabs.SelectedIndex=1;RefreshButtons();}});Row(center,review,46);
        token.TextChanged+=(s,e)=>{conversation="";RefreshButtons();};instruction.TextChanged+=(s,e)=>RefreshButtons();
        var right=Stack();right.BackColor=Color.White;BuildLearning(body,right);
        Row(right,Heading("プレビュー"),34); var run=Bar();previewAction=AddButton(run,"プレビューする",async()=>await PreviewFixed());stopAction=AddButton(run,"停止",StopPreview);Row(right,run,46);
        Row(right,previewHost,0); InitPreview(right);
        Row(page,status,32);status.Text="フォルダーを開いて制作を始めましょう。";fileLabel.Text="ファイルを選んでください";
        FormClosing+=(s,e)=> {if(busy || !AllowSwitch()) e.Cancel=true; else StopPreview();};
        proposal.TextChanged+=(s,e)=>{if(hasProposal) editorTabs.SelectedIndex=1;};
    }

    bool AllowSwitch() {return pending.Count==0 ? ConfirmDiscard() : MessageBox.Show(this,"残りの変更案を破棄しますか？",Text,MessageBoxButtons.YesNo)==DialogResult.Yes;}
    string Relative(string target) {return target.Substring(root.TrimEnd('\\').Length+1).Replace('\\','/');}
    string ProjectPath(string relative)
    {
        if(root==null || String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))throw new UserNotice("プロジェクト内の相対パスを指定してください。");
        string current=root;
        foreach(string part in relative.Replace('\\','/').Split('/')) {
            if(part.Length==0 || part.StartsWith(".") || part.EndsWith(".") || part.EndsWith(" ") || part.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase))throw new UserNotice("使用できないファイル名です。");
            current=Path.Combine(current,part);
            if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new UserNotice("リンク先は編集対象外です。");
        }
        return current;
    }
    void ChooseRoot()
    {
        using(var d=new FolderBrowserDialog {Description="制作に使うプロジェクトフォルダー",ShowNewFolderButton=true}) {
            if(d.ShowDialog(this)!=DialogResult.OK || !AllowSwitch())return;
            StopPreview();ResetFlow();root=Path.GetFullPath(d.SelectedPath);pending.Clear();path=null;loaded=hasProposal=false;baseline=null;original.Clear();proposal.Clear();conversation="";
            fileLabel.Text="ファイルを開くか、新規作成してください。";RefreshTree();RefreshButtons();
        }
    }
    void Populate(TreeNode node)
    {
        node.Nodes.Clear();
        try {
            foreach(var item in new DirectoryInfo((string)node.Tag).EnumerateFileSystemInfos().OrderByDescending(x=>x is DirectoryInfo).ThenBy(x=>x.Name)) {
                if((item.Attributes&(FileAttributes.Hidden|FileAttributes.ReparsePoint))!=0 || item.Name.StartsWith("."))continue;
                var child=node.Nodes.Add(item.Name);child.Tag=item.FullName;if(createRoot!=null)child.SelectedImageKey=child.ImageKey=TreeIcon(item);if(item is DirectoryInfo)child.Nodes.Add("…");
            }
        } catch(Exception ex){status.Text=ex.Message;}
    }
    void RefreshTree()
    {
        files.Nodes.Clear();if(root==null)return;
        var node=files.Nodes.Add(new DirectoryInfo(root).Name);node.Tag=root;Populate(node);node.Expand();
    }
    void OpenNode(TreeNode node)
    {
        if(busy || node==null || !(node.Tag is string) || !File.Exists((string)node.Tag))return;
        try {var target=ProjectPath(Relative((string)node.Tag));var bytes=ReadBytes(target);var value=Decode(bytes);
            if(!AllowSwitch())return;pending.Clear();StopPreview();SetFile(target,bytes,value);editorTabs.SelectedIndex=0;
        }catch(Exception ex){status.Text=ex.Message;}
    }
    void NewProjectFile()
    {
        if(root==null){ChooseRoot();if(root==null)return;}
        using(var d=new SaveFileDialog{InitialDirectory=SelectedDirectory(),AddExtension=false,OverwritePrompt=false}){
            if(d.ShowDialog(this)!=DialogResult.OK)return;
            try{
                if(!InsideRoot(d.FileName))throw new UserNotice("開いているフォルダー内で作成してください。");
                var target=ProjectPath(Relative(d.FileName));if(File.Exists(target))throw new UserNotice("同名のファイルがあります。");
                if(!AllowSwitch())return;Commit(target,null,new byte[0]);pending.Clear();SetFile(target,new byte[0],"");RefreshTree();editorTabs.SelectedIndex=0;
            }catch(Exception ex){status.Text=ex.Message;}
        }
    }
    bool InsideRoot(string name){return Path.GetFullPath(name).StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);}
    string SelectedDirectory(){string val=files.SelectedNode==null?null:files.SelectedNode.Tag as string;return val==null?root:Directory.Exists(val)?val:Path.GetDirectoryName(val);}
    void NewDirectory()
    {
        if(busy||root==null)return;
        string name=Microsoft.VisualBasic.Interaction.InputBox("作成するフォルダー名","新規フォルダー","",-1,-1);
        if(String.IsNullOrWhiteSpace(name))return;
        try{string selected=SelectedDirectory();string prefix=String.Equals(root,selected,StringComparison.OrdinalIgnoreCase)?"":Relative(selected)+"/";Directory.CreateDirectory(ProjectPath(prefix+name));RefreshTree();}catch(Exception ex){status.Text=ex.Message;}
    }
    IEnumerable<string> VisiblePaths(TreeNodeCollection nodes){
        foreach(TreeNode n in nodes){if(n.Tag is string && File.Exists((string)n.Tag))yield return Relative((string)n.Tag);foreach(string p in VisiblePaths(n.Nodes))yield return p;}
    }
    bool ConfirmContext(){
        shared.Clear();
        if(context.Checked){
            var folders=new Stack<string>();folders.Push(root);int total=0;
            while(folders.Count>0){
                foreach(var item in new DirectoryInfo(folders.Pop()).EnumerateFileSystemInfos()){
                    if(item.Name.StartsWith(".") || (item.Attributes&(FileAttributes.ReparsePoint|FileAttributes.Hidden))!=0)continue;
                    if(item is DirectoryInfo){if(!new[]{"node_modules","venv","__pycache__","bin","obj"}.Contains(item.Name,StringComparer.OrdinalIgnoreCase))folders.Push(item.FullName);continue;}
                    if(String.Equals(item.FullName,path,StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(item.Extension,@"^\.(html?|css|js|jsx|ts|tsx|py|cs|md)$",RegexOptions.IgnoreCase))continue;
                    byte[] bytes=ReadBytes(item.FullName);total+=Decode(bytes).Length;
                    if(shared.Count>=40 || total>100000)throw new UserNotice("関連コードが上限を超えました。小さいフォルダーを開くか「関連コードも送る」を外してください。");
                    shared[item.FullName]=bytes;
                }
            }
        }
        return MessageBox.Show(this,"都立AIに編集指示と次のファイルの内容を送信します。\n"+Relative(path)+"\n"+String.Join("\n",shared.Keys.Select(Relative))+"\n\n一覧のファイル名も送ります。",Text,MessageBoxButtons.OKCancel)==DialogResult.OK;
    }
    string ProjectPrompt(){
        return "\n選択中の相対パス: "+Relative(path)+"\nプロジェクト内の表示ファイル名: "+String.Join(", ",VisiblePaths(files.Nodes).Take(300))+
            "\n選択ファイルをcontentに返す。外部ライブラリ・OSS・素材を採用する場合はdependencies配列に各要素{name,version,url,license,notice}を入れる。noticeは権利者表示・再配布に必要な原文。出典・条件を確認できない素材を推測で使用しない。licence.txtはアプリがこの配列から作る。参考にできる関連コード: "+Json.Serialize(shared.ToDictionary(p=>Relative(p.Key),p=>Decode(p.Value)))+"" +
            (illustrations.Checked?"挿絵が必要なら同じJSONに illustration_prompt を指定し、メインのcontentでは画像パスを __ILLUSTRATION__ とする。画像はメインと同じ場所に作成する。":"画像生成は使わない。");
    }
    async Task<string> PrepareDraft(string body){
        string ignored;string main=ParseReply(body,out ignored);
        var data=Json.Deserialize<Dictionary<string,object>>(body);string message=StringField(data,"message").Trim();
        if(message.StartsWith("```"))message=message.Substring(message.IndexOf('\n')+1,message.LastIndexOf("```")-message.IndexOf('\n')-1);
        var edit=Json.Deserialize<Dictionary<string,object>>(message);
        string why=StringField(edit,"explanation");createReply=StringField(edit,"reply");
        var next=new List<KeyValuePair<string,string>>();object extras;
        if(edit.TryGetValue("files",out extras) && extras is System.Collections.IEnumerable){
            foreach(object entry in (System.Collections.IEnumerable)extras){
                if(teachMode && phase==Phase.Build)throw new UserNotice("複数ファイルの変更が返りました。この工程だけでもう一度依頼してください。");
                var item=entry as Dictionary<string,object>;var target=ProjectPath(StringField(item,"path"));string value=StringField(item,"content");
                if((File.Exists(target) && !shared.ContainsKey(target)) || String.Equals(target,path,StringComparison.OrdinalIgnoreCase) || Directory.Exists(target)||value==null||value.Length>Limit||value.Contains("\0")||next.Count>=24||next.Any(p=>String.Equals(p.Key,target,StringComparison.OrdinalIgnoreCase)))throw new UserNotice("追加ファイルの名前またはサイズを確認して再試行してください。");
                next.Add(new KeyValuePair<string,string>(target,value));
            }
        }
        licenceBaseline=null;AttachLicences(edit,next);
        string desc=StringField(edit,"illustration_prompt");
        if(illustrations.Checked && !String.IsNullOrWhiteSpace(desc) && main.Contains("__ILLUSTRATION__")){
            status.Text="コードに使用する挿絵を生成中…";main=main.Replace("__ILLUSTRATION__",await CreateArtwork(desc));RefreshTree();
        }
        pending.Clear();staged.Clear();foreach(var entry in next){pending.Enqueue(entry);byte[] bytes;if(shared.TryGetValue(entry.Key,out bytes))staged[entry.Key]=bytes;}editorTabs.SelectedIndex=1;
        if(teachMode && String.IsNullOrWhiteSpace(why) && phase==Phase.Build){
            try{why=await AskText("以下の変更案の重要な行、入力から結果が出る理由、確認方法を初心者に日本語で説明してください。\n"+main);}
            catch(Exception ex){why="解説の取得に失敗しました。「コードの意味を知る」で再試行できます。\n"+ex.Message;}
        }
        if(teachMode && !String.IsNullOrWhiteSpace(why))WriteNote("なぜ、このコードで動く？",why);
        return main;
    }
}
