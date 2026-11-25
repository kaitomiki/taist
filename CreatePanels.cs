using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

sealed partial class Editor
{
    Form runWindow;
    bool detachedRun;
    readonly ToolTip createTips=new ToolTip{ShowAlways=true};
    void MakeIcon(Button button,string glyph,string caption){
        var soft=button as SoftButton;if(soft==null)return;
        soft.Symbol=glyph;soft.AccessibleName=caption;soft.AutoSize=false;
        string label=caption=="実行パネルを開く"?"実行":caption=="フォルダーを開く"?"開く":caption=="新規ファイル"?"ファイル":caption=="新規フォルダー"?"フォルダー":caption=="AI設定"?"AI設定":caption=="使い方を見る"?"使い方":null;
        soft.IconLabel=label;soft.MinimumSize=Size.Empty;soft.Size=new Size(label==null?40:36+TextRenderer.MeasureText(label,soft.Font).Width,35);createTips.SetToolTip(soft,caption);
    }
    void OpenRunPanel(){
        if(runWindow==null || runWindow.IsDisposed)return;
        if(!runWindow.Visible)runWindow.Show(this);
        if(runWindow.WindowState==FormWindowState.Minimized)runWindow.WindowState=FormWindowState.Normal;
        runWindow.Activate();runWindow.BringToFront();
    }
    void ArrangeCreate(TableLayoutPanel grid,TableLayoutPanel middle,Control editCard,Control sideCard,
        Control logCard,Control preview,FlowLayoutPanel toolbar,Button help,Button clear){
        detachedRun=true;
        grid.SuspendLayout();middle.SuspendLayout();
        try{
            sideCard.Visible=false;grid.Controls.Remove(sideCard);
            grid.Controls.Add(editCard,2,0);grid.SetRowSpan(editCard,2);
            grid.SetColumnSpan(logCard,1);grid.ColumnStyles[0].Width=18;grid.ColumnStyles[1].Width=41;grid.ColumnStyles[2].Width=41;
            grid.RowStyles[1].Height=103;
            var notes=(TableLayoutPanel)sideTabs.TabPages[1].Controls[0];
            // Create has a single reading area. Its classroom coaching controls belong to Teach.
            var coachLabel=notes.GetControlFromPosition(0,4);if(coachLabel!=null)coachLabel.Visible=false;
            var audioTools=notes.GetControlFromPosition(0,3);audioTools.Controls.Add(reflect);
            foreach(int row in new[]{4,5,6})notes.RowStyles[row].Height=0;
            var answers=Stack();Row(answers,Section("AIの回答・解説"),32);Row(answers,notes,0);var answerCard=new StudioCard();answerCard.Controls.Add(answers);middle.Controls.Add(answerCard,0,1);
            var launch=AddButton(toolbar,"実行パネルを開く",OpenRunPanel);
            toolbar.Controls.SetChildIndex(launch,0);
            var header=new Panel{Dock=DockStyle.Fill};var title=Section("実行パネル　｜　■ で停止");header.Controls.Add(title);
            var wrapper=Stack();Row(wrapper,header,34);Row(wrapper,preview,0);
            runWindow=new Form{Text="Create  |  実行パネル",Icon=Icon,BackColor=Look.Paper,Font=Font,StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(820,580),MinimumSize=new Size(440,350)};
            runWindow.Controls.Add(wrapper);Tint(wrapper);
            runWindow.FormClosing+=(s,e)=>{if(e.CloseReason==CloseReason.UserClosing&&!Disposing){e.Cancel=true;runWindow.Hide();}};
            Disposed+=(s,e)=>{runWindow.Dispose();createTips.Dispose();sideCard.Dispose();};
            MakeIcon(open,"\uE8DA","フォルダーを開く");MakeIcon(create,"\uE8A5","新規ファイル");
            MakeIcon(folderAction,"\uE8F4","新規フォルダー");MakeIcon(setup,"\uE713","AI設定");
            MakeIcon(help,"\uE897","使い方を見る");MakeIcon(clear,"\uE74D","ログをクリア");
            MakeIcon(manualEdit,"\uE70F","自分でコードを編集");MakeIcon(stopAction,"\uE71A","実行を停止");
            MakeIcon(launch,"\uE768","実行パネルを開く");((SoftButton)launch).Primary=true;
            MakeIcon(expand["code"],"\uE740","コードエディタを拡大／復帰");
            MakeIcon(expand["log"],"\uE740","実行ログを拡大／復帰");
            previewArea=middle;editorArea=editCard;
        }finally{middle.ResumeLayout(true);grid.ResumeLayout(true);}
    }
    void FocusCreate(string id){
        if(id=="preview"){OpenRunPanel();runWindow.WindowState=FormWindowState.Maximized;return;}
        paneGrid.SuspendLayout();
        try{
            bool all=id=="";focusedPane=id;
            projectArea.Visible=all;previewArea.Visible=all;editorArea.Visible=all||id=="code";logArea.Visible=all||id=="log";
            float[] widths=all?new float[]{18,41,41}:id=="code"?new float[]{0,0,100}:new float[]{0,100,0};
            for(int i=0;i<3;i++)paneGrid.ColumnStyles[i].Width=widths[i];
            paneGrid.RowStyles[0].SizeType=id=="log"?SizeType.Absolute:SizeType.Percent;paneGrid.RowStyles[0].Height=id=="log"?0:100;
            paneGrid.RowStyles[1].SizeType=id=="log"?SizeType.Percent:SizeType.Absolute;paneGrid.RowStyles[1].Height=id=="log"?100:all?103:0;
            foreach(string key in new[]{"code","log"}){
                ((SoftButton)expand[key]).Symbol=key==id?"\uE73F":"\uE740";expand[key].Invalidate();
            }
        }finally{paneGrid.ResumeLayout(true);}
    }
}
