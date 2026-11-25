using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed partial class Editor
{
    bool teachMode, needsRevision;
    Button reject, updatePlan, reflect;
    string reflection="", revisionBase="", reviewImagePath;
    byte[] reviewImage;
    readonly Dictionary<string,string> accepted=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);

    void ResetReview(){pending.Clear();staged.Clear();reviewImagePath=null;reviewImage=null;needsRevision=false;revisionBase="";}
    void Reject(){
        if(busy||!hasProposal)return;
        string previous=instruction.Text.Trim();
        revisionBase=(revisionBase.Length>0?revisionBase+"\n":"")+previous;
        ResetRejectedFiles();
        instruction.Clear();needsRevision=true;stagedImplementation=false;hasProposal=false;proposal.Clear();editorTabs.SelectedIndex=0;
        status.Text="却下しました。「AIへのお願い」に直したい点を追加してください。";RefreshButtons();instruction.Focus();
    }
    void ResetRejectedFiles(){pending.Clear();staged.Clear();reviewImage=null;reviewImagePath=null;}
    async Task Refine(){
        string extra=instruction.Text.Trim();
        if(!teachMode||phase!=Phase.Plan||busy||extra.Length==0)return;
        try{busy=true;RefreshButtons();string response=await AskText("コードは出さず、次の方針を追加指示に合わせて更新してください。\n"+Json.Serialize(new{goal=objective,plan=strategy,additional_instruction=extra}));
            strategy=response;objective+="\n追加指示: "+extra;instruction.Clear();ShowWorkflow();status.Text="方針を更新しました。さらに追記するか、構成案に進めます。";}
        catch(Exception ex){status.Text=ex.Message;}finally{busy=false;RefreshButtons();}
    }
    async Task Reflect(){
        if(busy||phase!=Phase.Finished||accepted.Count==0)return;
        try{busy=true;RefreshButtons();status.Text="学びを振り返っています…";
            string answer=await AskText("学習の振り返りを初学者向けの日本語で書く。今回作った機能、コードの一行を使った仕組みの説明、この学習で得られたこと、次に試せる小さな変更。本人の習熟度を断定しない。根拠は承認されたコード。\n"+Json.Serialize(new{goal=objective,plan=strategy,approved=accepted.ToDictionary(p=>p.Key,p=>p.Value.Length>4000?p.Value.Substring(0,4000):p.Value)}));
            reflection=answer;WriteNote("この制作で学べたこと",answer);status.Text="作品を保存し、学習の振り返りを表示しました。";
        }catch(Exception ex){WriteNote("作品を保存しました","振り返りの取得に失敗しました。「振り返り」で再試行できます。");status.Text=ex.Message;}
        finally{busy=false;RefreshButtons();}
    }

    void AttachLicences(Dictionary<string,object> edit,List<KeyValuePair<string,string>> files){
        object raw;if(!edit.TryGetValue("dependencies",out raw)||raw==null)return;
        var entries=raw as System.Collections.IEnumerable;if(entries==null||raw is string)throw new UserNotice("依存ライブラリの記載を確認できません。再生成してください。");
        var blocks=new List<string>();
        foreach(var item in entries){
            var dep=item as Dictionary<string,object>;
            string[] fields=new[]{"name","version","url","license","notice"}.Select(k=>StringField(dep,k)).ToArray();
            Uri uri;if(fields.Any(String.IsNullOrWhiteSpace)||!Uri.TryCreate(fields[2],UriKind.Absolute,out uri)||(uri.Scheme!="https"&&uri.Scheme!="http"))
                throw new UserNotice("ライブラリの出典かライセンス表示が不足しています。再生成してください。");
            blocks.Add(String.Join("\n",fields));
        }
        if(blocks.Count==0)return;
        var destination=ProjectPath("licence.txt");
        if(String.Equals(destination,path,StringComparison.OrdinalIgnoreCase)||files.Any(x=>String.Equals(x.Key,destination,StringComparison.OrdinalIgnoreCase)))throw new UserNotice("ライセンスの記載が重複しています。再生成してください。");
        string content="";if(File.Exists(destination)){var previous=ReadBytes(destination);content=Decode(previous);licenceBaseline=previous;}
        foreach(var block in blocks){if(!content.Contains(block))content=content.TrimEnd()+"\n\n"+block+"\n";}
        if(content.Length>Limit)throw new UserNotice("licence.txt の文字数が上限を超えています。");
        files.Add(new KeyValuePair<string,string>(destination,content.TrimStart()));
    }
    byte[] licenceBaseline;
    sealed class Change {public string Path;public byte[] Old,Bytes;public string Text;}
    List<Change> Changes(){
        var edits=new List<Change>();
        Action<string,string,byte[]> add=(target,value,before)=>{
            ProjectPath(Relative(target));value=value.Replace("\r\n","\n");
            if(before==null||Decode(before).Contains("\r\n"))value=value.Replace("\n","\r\n");
            if(value.Length>Limit)throw new UserNotice("ファイルが10万文字を超えています。");
            edits.Add(new Change{Path=target,Text=value,Old=before,Bytes=Encode(value,HasBom(before))});};
        add(path,proposal.Text,baseline);
        foreach(var p in pending){byte[] before;staged.TryGetValue(p.Key,out before);if(licenceBaseline!=null && String.Equals(p.Key,ProjectPath("licence.txt"),StringComparison.OrdinalIgnoreCase))before=licenceBaseline;add(p.Key,p.Value,before);}
        if(reviewImage!=null)edits.Add(new Change{Path=reviewImagePath,Bytes=reviewImage});
        return edits;
    }
    bool ConfirmChanges(List<Change> changes){
        using(var f=new Steps("承認内容の確認")){
            var p=Steps.Page();var selected=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
            var view=new SourceBox(true);Steps.Add(p,selected,36);Steps.Add(p,view,0);
            foreach(var c in changes)selected.Items.Add(Relative(c.Path));
            selected.SelectedIndexChanged+=(s,e)=>{var c=changes[selected.SelectedIndex];view.Language(c.Path);view.Text=c.Text??"生成画像";};selected.SelectedIndex=0;
            f.ShowPage(p,"変更を承認しますか？",changes.Count+" ファイル。ファイル名を選ぶと内容を確認できます。");
            f.Button("戻る",()=>f.Close(),false);f.Button("承認して保存",()=>f.DialogResult=DialogResult.OK,true);
            f.Note.Text="作品完成時に、承認したコードの抜粋を都立AIに送り、学びを振り返ります。";
            return f.ShowDialog(this)==DialogResult.OK;
        }
    }
    void ApplyChanges(List<Change> edits){
        foreach(var c in edits){
            ProjectPath(Relative(c.Path));
            if(c.Old==null?File.Exists(c.Path)||Directory.Exists(c.Path):!File.Exists(c.Path)||!ReadBytes(c.Path).SequenceEqual(c.Old))
                throw new IOException("別の変更があります。開き直してください。 "+Relative(c.Path));}
        var written=new Stack<Change>();
        try{foreach(var c in edits){ProjectPath(Relative(c.Path));Directory.CreateDirectory(Path.GetDirectoryName(c.Path));Commit(c.Path,c.Old,c.Bytes);written.Push(c);}}
        catch{foreach(var c in written){if(c.Old==null){if(File.ReadAllBytes(c.Path).SequenceEqual(c.Bytes))File.Delete(c.Path);}else Commit(c.Path,c.Bytes,c.Old);}throw;}
    }
    async Task Approve(){
        if(!hasProposal||busy)return;
        try{var changes=Changes();if(!ConfirmChanges(changes))return;ApplyChanges(changes);
            if(stagedImplementation){foreach(var c in changes.Where(c=>c.Text!=null))accepted[Relative(c.Path)]=c.Text;
                if(!teachMode||++cursor>=sequence.Count)phase=Phase.Finished;}
            baseline=changes[0].Bytes;source=changes[0].Text;original.Text=DisplayText(source);proposal.Clear();hasProposal=false;stagedImplementation=false;
            ResetReview();savedOnce=true;RefreshTree();RefreshButtons();status.Text="承認した変更を保存しました。";
            if(phase==Phase.Finished)await Reflect();
        }catch(Exception ex){status.Text="承認できませんでした。 "+ex.Message;}
    }
}

