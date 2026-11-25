using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

sealed partial class Editor
{
    public sealed class Room
    {
        public Course Course {get;set;} public string Hint {get;set;} public string Name {get;set;} public string Url {get;set;} public string Lesson {get;set;}
        public override string ToString(){return Name;}
    }
    static string BookFile {get{return Path.Combine(DataDir,"classrooms.json");}}
    static List<Room> ReadBook(){return File.Exists(BookFile)?Json.Deserialize<List<Room>>(File.ReadAllText(BookFile)) : new List<Room>();}
    static List<Room> Store(Room room,bool remove)
    {
        using(var mutex=new Mutex(false,@"Local\ToritsuClassrooms")){
            try{if(!mutex.WaitOne(4000))throw new IOException("教室一覧は別の画面で使用中です。");}catch(AbandonedMutexException){}
            try{
                Directory.CreateDirectory(DataDir);var book=ReadBook();book.RemoveAll(r=>r.Url==room.Url);if(!remove)book.Add(room);
                string tmp=BookFile+".tmp";File.WriteAllText(tmp,Json.Serialize(book),Utf8);
                if(File.Exists(BookFile))File.Replace(tmp,BookFile,null);else File.Move(tmp,BookFile);return book;
            }finally{mutex.ReleaseMutex();}
        }
    }
    static Form RoomForm(string title,Size size){
        return new Form{Text=title,ClientSize=size,MinimumSize=new Size(540,420),Font=new Font("Yu Gothic UI",10),BackColor=Look.Paper,StartPosition=FormStartPosition.CenterScreen,Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath)};
    }
    public void Teach()
    {
        using(var f=new Steps("先生の教室")){
            var home=Steps.Page();var list=new ListBox{Dock=DockStyle.Fill,IntegralHeight=false};
            Steps.Add(home,Look.Label("この端末に保存した教室",15,true),46);Steps.Add(home,list,0);
            var utility=Bar();Steps.Add(home,utility,46);
            var write=Steps.Page();var name=new TextBox{Dock=DockStyle.Fill,MaxLength=100};
            var lesson=new TextBox{Dock=DockStyle.Fill,Multiline=true,MaxLength=8000,ScrollBars=ScrollBars.Vertical};
            Steps.Add(write,Look.Label("教室の名前",12,true),35);Steps.Add(write,name,39);
            var tabs=new TabControl{Dock=DockStyle.Fill};var planTab=new TabPage("授業の指示（必須）");var hintTab=new TabPage("左側に表示するヒント");
            var hint=new TextBox{Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Vertical,MaxLength=2000};
            planTab.Controls.Add(lesson);hintTab.Controls.Add(hint);tabs.TabPages.Add(planTab);tabs.TabPages.Add(hintTab);
            var courseTab=new TabPage("モード・単元");tabs.TabPages.Add(courseTab);
            var settings=Steps.Page();courseTab.Controls.Add(settings);
            var modes=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
            modes.Items.AddRange(new object[]{"作品制作","ラーニング","ドリル"});
            var units=new TextBox{Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Vertical,MaxLength=4000};
            Steps.Add(settings,modes,36);Steps.Add(settings,Look.Label("単元を1行に1つ入力。ドリルの加点は「変数|1」のように指定できます。",10,false),44);Steps.Add(settings,units,0);
            Action<Course> loadCourse=c=>{c=c??new Course();modes.SelectedIndex=Array.IndexOf(Course.Modes,c.mode);units.Lines=c.units;};
            Func<Course> currentCourse=()=>new Course{name=name.Text.Trim(),mode=Course.Modes[modes.SelectedIndex],units=units.Lines.Select(x=>x.Trim()).Where(x=>x.Length>0).ToArray()}.Check();
            Steps.Add(write,tabs,0);
            var network=Steps.Page();var key=new TextBox{Dock=DockStyle.Fill,UseSystemPasswordChar=true};
            Steps.Add(network,Look.Label("先生のGitHubトークン",14,true),44);
            Steps.Add(network,Look.Label("教室への書き込みに使います。GitHubで gist 権限を付けたトークンを貼り付けてください。",11,false),60);
            Steps.Add(network,key,38);
            var help=new SoftButton{Text="GitHubのトークン作成画面を開く",Dock=DockStyle.Left,Width=290};Steps.Add(network,help,46);
            help.Click+=(s,e)=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/settings/tokens/new?scopes=gist"){UseShellExecute=true});}catch(Exception ex){f.Note.Text=ex.Message;}};
            Steps.Add(network,Look.Label("初回はGitHubで名前と期限を選び、gist にチェックして発行します。",10,false),45);
            var share=Steps.Page();var url=new TextBox{Dock=DockStyle.Fill,ReadOnly=true};
            Steps.Add(share,Look.Label("生徒に配るリンク",15,true),47);Steps.Add(share,url,44);
            Steps.Add(share,Look.Label("生徒は制作準備の「AIと教室」から、このリンクで参加できます。",11,false),57);
            Room selected=null;bool transmitting=false;
            Action dashboard=null,compose=null,authorize=null,done=null;
            dashboard=()=>{
                f.ShowPage(home,"先生の教室","教室を選ぶ → 指示とヒントを書く → 配信してリンクを共有");
                list.Items.Clear();try{foreach(var r in ReadBook().OrderBy(x=>x.Name))list.Items.Add(r);}catch(Exception ex){f.Note.Text=ex.Message;}
                if(list.Items.Count>0)list.SelectedIndex=0;else f.Note.Text="作成した教室はここに保存されます。";
                f.Button("教室を作る",()=>{selected=null;loadCourse(null);name.Clear();lesson.Clear();hint.Clear();tabs.SelectedIndex=0;compose();},true);
                var edit=f.Button("指示・ヒントを変更",()=>{selected=list.SelectedItem as Room;if(selected!=null){loadCourse(selected.Course);name.Text=selected.Name;lesson.Text=selected.Lesson;hint.Text=selected.Hint??"";tabs.SelectedIndex=0;compose();}},false);
                var link=f.Button("リンクを表示",()=>{selected=list.SelectedItem as Room;if(selected!=null)done();},false);
                edit.Enabled=link.Enabled=list.Items.Count>0;
            };
            compose=()=>{
                f.ShowPage(write,"1 / 3　授業の内容","例：「1年A組 情報Ⅰ」「コードには初心者向けのコメントを付けてください」");
                f.Button("教室一覧に戻る",dashboard,false);
                f.Button("次へ",()=>{if(string.IsNullOrWhiteSpace(name.Text)||string.IsNullOrWhiteSpace(lesson.Text)){f.Note.Text="教室名と授業の指示を入力してください。";return;}try{currentCourse();authorize();}catch(Exception ex){f.Note.Text=ex.Message;}},true);
            };
            authorize=()=>{
                f.ShowPage(network,"2 / 3　指示を配信","「"+name.Text+"」に授業の指示を送ります。");f.Button("戻る",compose,false);
                f.Button("この内容で配信",async()=>{
                    if(string.IsNullOrWhiteSpace(key.Text)){f.Note.Text="GitHubトークンを入力してください。";return;}
                    transmitting=true;f.Actions.Enabled=key.Enabled=false;f.Note.Text="配信しています…";
                    try{string result=await Publish(selected==null?"":selected.Url,key.Text.Trim(),lesson.Text,hint.Text,currentCourse());
                        selected=new Room{Course=currentCourse(),Name=name.Text.Trim(),Lesson=lesson.Text,Hint=hint.Text,Url=result};done();
                        Store(selected,false);
                    }catch(Exception ex){f.Note.Text=ex.Message;}finally{transmitting=false;f.Actions.Enabled=key.Enabled=true;}
                },true);
            };
            done=()=>{
                url.Text=selected.Url;f.ShowPage(share,"3 / 3　リンクを共有","「"+selected.Name+"」の教室リンクです。");
                f.Button("教室一覧に戻る",dashboard,false);f.Button("リンクをコピー",()=>{try{Clipboard.SetText(url.Text);f.Note.Text="コピーしました。授業の連絡に貼り付けてください。";}catch{url.Focus();url.SelectAll();f.Note.Text="Ctrl+Cでコピーしてください。";}},true);
            };
            AddButton(utility,"以前のリンクを登録",async()=>{
                string link=Microsoft.VisualBasic.Interaction.InputBox("先生が以前配ったリンクを貼り付けてください。","以前の教室を登録").Trim();
                if(link=="")return;if(!IsRoom(link)){f.Note.Text="教室リンクを確認してください。";return;}
                string title=Microsoft.VisualBasic.Interaction.InputBox("一覧に表示する教室名","教室の名前").Trim();if(title=="")return;
                transmitting=true;f.Enabled=false;
                try{Store(new Room{Course=await ReadCourse(link),Name=title,Url=link,Lesson=await ReadLesson(link),Hint=await FetchHint(link)},false);dashboard();}catch(Exception ex){f.Note.Text=ex.Message;}finally{f.Enabled=true;transmitting=false;}
            });
            AddButton(utility,"一覧から外す",()=>{
                var r=list.SelectedItem as Room;if(r==null)return;
                if(MessageBox.Show(f,"この端末の一覧から「"+r.Name+"」を外しますか？",f.Text,MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
                try{Store(r,true);dashboard();}catch(Exception ex){f.Note.Text=ex.Message;}
            });
            AddButton(utility,"自分でも制作",()=>{StudentSession(f);});
            dashboard();f.FormClosing+=(s,e)=>{if(transmitting)e.Cancel=true;};f.ShowDialog();
        }
    }
    Room destination;
    bool startingInRoom;
    static string HistoryFile {get{return Path.Combine(DataDir,"student-rooms.json");}}
    static List<Room> History(){
        return File.Exists(HistoryFile)?Json.Deserialize<List<Room>>(File.ReadAllText(HistoryFile)):new List<Room>();
    }
    static void Remember(Room room){
        using(var gate=new Mutex(false,@"Local\ToritsuStudentRooms")){
            try{if(!gate.WaitOne(2000))return;}catch(AbandonedMutexException){}
            try{Directory.CreateDirectory(DataDir);var items=History();items.RemoveAll(r=>r.Url==room.Url);items.Insert(0,new Room{Name=room.Name,Url=room.Url});
                string tmp=HistoryFile+".tmp";File.WriteAllText(tmp,Json.Serialize(items.Take(20).ToArray()),Utf8);
                if(File.Exists(HistoryFile))File.Replace(tmp,HistoryFile,null);else File.Move(tmp,HistoryFile);
            }finally{gate.ReleaseMutex();}
        }
    }
    public static void StudentSession(IWin32Window owner){
        Room next=null;string key="";
        do{
            using(var student=new Editor()){
                student.token.Text=key;
                if(next!=null){student.EnterRoom(next);student.startingInRoom=true;}
                if(next!=null && next.Course.mode!="project"){student.teachMode=true;student.RunCourse();}
                else if(student.StartStudent())student.ShowDialog(owner);
                next=student.destination;key=student.token.Text;
            }
        }while(next!=null);
    }
    void EnterRoom(Room room){
        roomUrl=room.Url;lastLesson=room.Lesson;course=room.Course;conversation="";SetHint(roomUrl,room.Hint);
    }
    void ChangeRoom(Form owner,bool unfinished=false){
        if(busy)return;
        var next=ChooseRoom(owner);if(next==null)return;
        if(unfinished && MessageBox.Show(owner,"入力中の回答を終了して「"+next.Name+"」へ移動しますか？","教室を変更",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        destination=next;owner.Close();
        if(owner.Visible)destination=null;
    }
    void Join(IWin32Window owner){
        if(busy)return;var next=ChooseRoom(owner);if(next!=null)EnterRoom(next);
    }
    Room ChooseRoom(IWin32Window owner){
        using(var f=RoomForm("教室を選ぶ",new Size(640,460))){
            var panel=Stack();f.Controls.Add(panel);
            Row(panel,Heading("どの教室で学びますか？"),48);
            Row(panel,Caption("以前に参加した教室"),30);
            var recent=new ListBox{Dock=DockStyle.Fill,IntegralHeight=false};Row(panel,recent,0);
            try{foreach(var room in History())recent.Items.Add(room);}catch(IOException){}catch(InvalidOperationException){}catch(ArgumentException){}
            Row(panel,Caption("新しい教室は、先生から受け取ったリンクを貼り付けます"),34);
            var link=new TextBox{Dock=DockStyle.Fill};Row(panel,link,38);
            recent.SelectedIndexChanged+=(s,e)=>{var chosen=recent.SelectedItem as Room;if(chosen!=null)link.Text=chosen.Url;};
            var ops=Bar();Row(panel,ops,46);var info=Caption("");Row(panel,info,40);
            Room result=null;bool loading=false;
            var go=AddButton(ops,"この教室に参加",async()=>{
                if(loading)return;string url=link.Text.Trim();
                if(!IsRoom(url)){info.Text="先生から配られた教室リンクを確認してください。";return;}
                loading=true;panel.Enabled=false;info.Text="授業内容を読み込んでいます…";
                try{
                    var lesson=await ReadLesson(url);var config=await ReadCourse(url);
                    string hint="";try{hint=await FetchHint(url);}catch(System.Net.Http.HttpRequestException){}catch(UserNotice){}catch(System.Threading.Tasks.TaskCanceledException){}
                    string first=lesson.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"教室";
                    result=new Room{Url=url,Lesson=lesson,Course=config,Hint=hint,Name=!String.IsNullOrWhiteSpace(config.name)?config.name+"　("+config.Title+")":config.Title+"　"+first.Substring(0,Math.Min(32,first.Length))+"　("+new Uri(url).Segments[2].Substring(0,6)+")"};
                    try{Remember(result);}catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
                    loading=false;f.DialogResult=DialogResult.OK;
                }catch(Exception ex){info.Text=ex.Message;}finally{loading=false;panel.Enabled=true;}
            });
            AddButton(ops,"キャンセル",()=>f.Close());f.AcceptButton=go;recent.DoubleClick+=(s,e)=>{if(recent.SelectedItem!=null)go.PerformClick();};
            f.FormClosing+=(s,e)=>{if(loading)e.Cancel=true;};return f.ShowDialog(owner)==DialogResult.OK?result:null;
        }
    }
}

