using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

sealed class SourceBox : RichTextBox
{
    readonly Timer delay=new Timer{Interval=200};
    string extension="";
    bool painting;
    int revision,lastRevision=-1,lastStart=-1,lastEnd=-1;
    void Schedule(){if(painting||IsDisposed)return;delay.Stop();delay.Start();}
    [DllImport("user32.dll",EntryPoint="SendMessageW")]static extern IntPtr Send(IntPtr hwnd,int msg,IntPtr a,IntPtr b);
    [DllImport("user32.dll",EntryPoint="SendMessageW")]static extern IntPtr PointMessage(IntPtr hwnd,int msg,IntPtr a,ref Point p);
    const string Strings="(?<literal>\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|`(?:\\\\.|[^`\\\\])*`)";
    const string Terms=@"|(?<number>\b\d+(?:\.\d+)?\b)|(?<keyword>\b(?:if|elif|else|for|while|in|import|from|def|return|class|try|except|raise|with|as|pass|break|continue|and|or|not|True|False|None|async|await|const|let|var|function|new|switch|case|default|export|throw|catch|true|false|null|extends)\b)|(?<call>\b\w+(?=\s*\())";
    static readonly Regex Python=new Regex(@"(?<comment>\#[^\n]*)|"+Strings+Terms);
    static readonly Regex Script=new Regex(@"(?<comment>//[^\n]*|/\*[\s\S]*?(?:\*/|$))|"+Strings+Terms);
    static readonly Regex Markup=new Regex(@"(?<comment><!--[\s\S]*?(?:-->|$))|(?<keyword></?[\w:-]+)|"+Strings+@"|(?<call>\b[\w:-]+(?=\s*=))");
    public SourceBox(bool readOnly){
        Dock=DockStyle.Fill;ReadOnly=readOnly;WordWrap=false;DetectUrls=false;AutoWordSelection=false;ScrollBars=RichTextBoxScrollBars.Both;AcceptsTab=true;MaxLength=200000;
        Font=new Font("Consolas",11);BackColor=Color.FromArgb(248,250,253);ForeColor=Color.FromArgb(32,46,67);
        delay.Tick+=(s,e)=>{delay.Stop();Highlight();};
        TextChanged+=(s,e)=>{if(!painting){revision++;Schedule();}};
        VScroll+=(s,e)=>Schedule();HScroll+=(s,e)=>Schedule();Resize+=(s,e)=>Schedule();VisibleChanged+=(s,e)=>{if(Visible)Schedule();};
    }
    public void Language(string file){extension=Path.GetExtension(file).ToLowerInvariant();revision++;Schedule();}
    void Highlight(){
        if(!IsHandleCreated||!Visible||IsDisposed||painting||TextLength==0)return;
        int first=GetCharIndexFromPosition(new Point(0,0));
        int last=GetCharIndexFromPosition(new Point(Math.Max(0,ClientSize.Width-1),Math.Max(0,ClientSize.Height-1)));
        int start=Math.Max(0,GetFirstCharIndexFromLine(GetLineFromCharIndex(first)));
        int after=GetFirstCharIndexFromLine(GetLineFromCharIndex(last)+1);
        if(after<0)after=TextLength;
        if(after-start>6500){start=Math.Max(start,first-256);after=Math.Min(after,start+6500);}
        if(lastRevision==revision&&lastStart==start&&lastEnd==after)return;
        int pos=SelectionStart,len=SelectionLength;Point scroll=new Point();bool changed=Modified;painting=true;
        PointMessage(Handle,0x04dd,IntPtr.Zero,ref scroll);Send(Handle,11,IntPtr.Zero,IntPtr.Zero);
        try{Select(start,after-start);string code=SelectedText;SelectionColor=ForeColor;
            var lexer=extension==".py"?Python:extension==".html"||extension==".htm"?Markup:Script;
            foreach(Match m in lexer.Matches(code)){
                Select(start+m.Index,m.Length);
                SelectionColor=m.Groups["comment"].Success?Color.FromArgb(76,113,80):m.Groups["literal"].Success?Color.FromArgb(149,62,46):
                    m.Groups["keyword"].Success?Color.FromArgb(102,58,158):m.Groups["number"].Success?Color.FromArgb(15,106,123):Color.FromArgb(34,83,156);
            }
            lastRevision=revision;lastStart=start;lastEnd=after;
        }finally{Select(pos,len);if(len==0)SelectionColor=ForeColor;PointMessage(Handle,0x04de,IntPtr.Zero,ref scroll);Send(Handle,11,(IntPtr)1,IntPtr.Zero);Modified=changed;painting=false;Invalidate();}
    }
    protected override void Dispose(bool disposing){if(disposing)delay.Dispose();base.Dispose(disposing);}
}

