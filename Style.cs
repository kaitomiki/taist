using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

static class Look
{
    internal static readonly Color Ink=Color.FromArgb(28,45,66), Accent=Color.FromArgb(94,99,238), End=Color.FromArgb(91,72,194), Paper=Color.FromArgb(245,247,251);
    public static GraphicsPath Round(Rectangle r,int radius)
    {
        var p=new GraphicsPath();int d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    public static Label Label(string text,int size,bool bold)
    {
        return new Label{Text=text,Dock=DockStyle.Fill,Font=new Font("Yu Gothic UI",size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=Ink,TextAlign=ContentAlignment.MiddleLeft};
    }
}
class SoftButton : Button
{
    public bool Primary {get;set;}
    internal string Symbol, IconLabel;
    static readonly Bitmap Toolbar=new Bitmap(typeof(SoftButton).Assembly.GetManifestResourceStream("Toolbar.png"));
    const string Symbols="\uE8DA\uE8A5\uE8F4\uE713\uE897\uE74D\uE70F\uE71A\uE768\uE740\uE73F";
    public bool Lavender {get;set;}
    bool hover;
    public SoftButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;Padding=new Padding(9,2,9,2);MinimumSize=new Size(74,36);UseVisualStyleBackColor=false;}
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover=true;Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=false;Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(Parent==null?Look.Paper:Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        var r=new Rectangle(1,1,Width-3,Height-3);if(r.Width<2||r.Height<2)return;
        using(var shape=Look.Round(r,12)){
            Color a=!Enabled?Color.FromArgb(230,234,238):Primary?(Lavender?Color.FromArgb(97,99,242):Look.Accent):hover?Color.FromArgb(227,241,242):Color.White;
            using(var b=new LinearGradientBrush(r,a,Primary&&Enabled?(hover?Color.FromArgb(38,58,122):Lavender?Color.FromArgb(94,73,214):Look.End):a,8f))g.FillPath(b,shape);
            using(var pen=new Pen(Focused?Look.Ink:Color.FromArgb(190,204,213),Focused?2:1))g.DrawPath(pen,shape);
        }
        int index=Symbol==null?-1:Symbols.IndexOf(Symbol,StringComparison.Ordinal);
        if(index>=0){int size=Math.Min(20,r.Height-4);g.DrawImage(Toolbar,new Rectangle(IconLabel==null?(Width-size)/2:8,(Height-size)/2,size,size),new Rectangle(index*32,Primary?32:0,32,32),GraphicsUnit.Pixel);
            if(IconLabel!=null)TextRenderer.DrawText(g,IconLabel,Font,new Rectangle(30,0,Width-32,Height),Primary?Color.White:Look.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter);}
        else TextRenderer.DrawText(g,Text,Font,r,!Enabled?Color.DimGray:Primary?Color.White:Look.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(r,-6,-6));
    }
}
class Banner : Control
{
    public string Detail="";
    public Banner(){DoubleBuffered=true;Dock=DockStyle.Fill;Margin=new Padding(0,0,0,6);}
    protected override void OnPaint(PaintEventArgs e){
        var r=new Rectangle(0,0,Width-1,Height-1);if(r.Width<2||r.Height<2)return;
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=Look.Round(r,19))using(var b=new LinearGradientBrush(r,Color.FromArgb(237,246,253),Color.FromArgb(231,226,251),8f)){g.FillPath(b,p);
            g.SetClip(p);using(var pen=new Pen(Color.FromArgb(35,Look.Accent),2)){for(int i=0;i<4;i++)g.DrawEllipse(pen,r.Width-130+i*22,-95+i*5,210,210);}g.ResetClip();}
        using(var heading=new Font("Yu Gothic UI",18,FontStyle.Bold)){
            TextRenderer.DrawText(g,Text,heading,new Rectangle(20,4,Width-40,Detail.Length==0?Height-10:Height/2+2),Look.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);}
        if(Detail.Length>0)TextRenderer.DrawText(g,Detail,Font,new Rectangle(22,Height/2+1,Width-44,Height/2-4),Look.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    }
}
class Steps : Form
{
    readonly Banner banner=new Banner();
    readonly System.Collections.Generic.HashSet<Control> visited=new System.Collections.Generic.HashSet<Control>();
    protected override void Dispose(bool disposing){if(disposing){foreach(Control page in visited)page.Dispose();visited.Clear();}base.Dispose(disposing);}
    readonly Panel pages=new Panel{Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(14)};
    public readonly FlowLayoutPanel Actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
    public readonly Label Note=Look.Label("",10,false);
    public Steps(string title){
        Text=title;Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);Font=new Font("Yu Gothic UI",11);
        BackColor=Look.Paper;StartPosition=FormStartPosition.CenterScreen;ClientSize=new Size(720,485);MinimumSize=new Size(630,475);
        var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,Padding=new Padding(20)};
        Controls.Add(grid);Add(grid,banner,88);Add(grid,pages,0);Add(grid,Note,45);Add(grid,Actions,52);
    }
    public static void Add(TableLayoutPanel grid,Control c,int height){int i=grid.RowCount++;grid.RowStyles.Add(height==0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,height));grid.Controls.Add(c,0,i);}
    public static TableLayoutPanel Page(){return new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,BackColor=Color.White};}
    public void ShowPage(Control page,string heading,string hint){visited.Add(page);pages.Controls.Clear();pages.Controls.Add(page);banner.Text=heading;banner.Detail=hint;banner.Invalidate();Note.Text="";while(Actions.Controls.Count>0)Actions.Controls[0].Dispose();}
    public SoftButton Button(string label,Action action,bool primary){var b=new SoftButton{AutoSize=true,Text=label,Primary=primary,MinimumSize=new Size(105,42)};b.Click+=(s,e)=>action();Actions.Controls.Add(b);return b;}
}
