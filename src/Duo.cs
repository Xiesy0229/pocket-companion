using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace StickerDuo {
    public class Settings {
        public int X = int.MinValue, Y = int.MinValue;
        public float Scale = .5f;
        public int Snacks = 0;
        public string[] Phrases = { "今日はどんな一日だった？", "ちょっと休憩しよ。", "何か音楽でも聴く？", "ここにいるよ。" };
        public void Normalize() {
            if(float.IsNaN(Scale) || float.IsInfinity(Scale)) Scale=1;
            Scale = Math.Max(.4f, Math.Min(1.4f, Scale));
            Snacks=Math.Max(0,Snacks);
            if(Phrases==null || Phrases.Length==0) Phrases=new string[]{"ここにいるよ。"};
        }
        public static Settings Load(string file) {
            try { using(var f=File.OpenRead(file)) { var s=(Settings)new XmlSerializer(typeof(Settings)).Deserialize(f); s.Normalize(); return s; } }
            catch { return new Settings(); }
        }
        public void Save(string file) {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string tmp=file+".tmp";
            using(var f=File.Create(tmp)) new XmlSerializer(typeof(Settings)).Serialize(f,this);
            if(File.Exists(file)) File.Replace(tmp,file,null); else File.Move(tmp,file);
        }
    }
    public class Interaction {
        public const float ImageX=10, ImageY=84, ImageWidth=340;
        public float ImageHeight;
        public bool Feeding;
        public double EatUntil, BounceUntil, BubbleUntil;
        public PointF Food;
        public string Bubble="こんにちは。";
        public int PhraseIndex;
        public Settings Config;
        public Interaction(Settings config, float height) { Config=config; ImageHeight=height; BubbleUntil=7; }
        public RectangleF Companion { get { return new RectangleF(ImageX+ImageWidth*.64f,ImageY+ImageHeight*.68f,ImageWidth*.32f,ImageHeight*.29f); } }
        public PointF Mouth { get { return new PointF(ImageX+ImageWidth*.774f,ImageY+ImageHeight*.811f); } }
        public RectangleF Cookie { get { return new RectangleF(128,ImageY+ImageHeight+7,104,36); } }
        public void Say(double now) { Bubble=Config.Phrases[PhraseIndex++ % Config.Phrases.Length]; BubbleUntil=now+4; BounceUntil=now+.65; }
        public bool Drop(PointF p,double now) {
            Feeding=false;
            if(!Companion.Contains(p)) return false;
            Config.Snacks++; EatUntil=now+1.8; BounceUntil=now+.65;
            Bubble="クッキー、ありがとう！"; BubbleUntil=now+3; return true;
        }
    }
    public class DuoWindow : Form {
        readonly Bitmap art;
        readonly Settings config;
        readonly Interaction state;
        readonly Timer timer=new Timer();
        readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
        readonly ContextMenuStrip menu=new ContextMenuStrip();
        readonly NotifyIcon tray=new NotifyIcon();
        readonly string settingsFile;
        bool pressed,dragged,closing;
        Point downCursor,downWindow;
        double nextFrame;
        readonly int baseHeight;
        public DuoWindow(string asset,string file) {
            settingsFile=file; config=Settings.Load(file); art=new Bitmap(asset);
            state=new Interaction(config,340f*art.Height/art.Width);
            baseHeight=(int)(state.Cookie.Bottom+8);
            Text="口袋搭子 · 人物与大嘴吉";
            FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true;
            StartPosition=FormStartPosition.Manual;
            ClientSize=new Size((int)(360*config.Scale),(int)(baseHeight*config.Scale));
            if(config.X==int.MinValue) ResetPosition(); else Location=new Point(config.X,config.Y);
            ClampPosition();
            menu.Items.Add("说一句",null,delegate { state.Say(clock.Elapsed.TotalSeconds); Render(); });
            menu.Items.Add("修改我的短句…",null,delegate { EditPhrases(); });
            var sizes=new ToolStripMenuItem("桌宠大小");
            foreach(float value in new float[]{.4f,.5f,.7f,1f,1.2f}) {
                float size=value;
                sizes.DropDownItems.Add(((int)(size*100))+"%",null,delegate { config.Scale=size; ClientSize=new Size((int)(360*size),(int)(baseHeight*size)); ClampPosition(); Save(); Render(); });
            }
            menu.Items.Add(sizes);
            menu.Items.Add("回到屏幕右下角",null,delegate { ResetPosition(); Save(); Render(); });
            menu.Items.Add("暂时隐藏（托盘可找回）",null,delegate { Hide(); });
            menu.Items.Add("怎么玩",null,delegate { MessageBox.Show("点击人物：随机感的轮流短句与弹跳\n按住人物拖动：移动整个组合\n拖底部饼干到大嘴吉：喂食\n右键：修改短句、调大小、退出\n\n这是本地互动版，短句不是 AI 自动生成。\n照片不会上传。位置、短句和喂食次数会自动保存。", "口袋搭子"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出",null,delegate { Close(); });
            tray.Icon=SystemIcons.Application; tray.Text="口袋搭子 · 双击找回";
            var trayMenu=new ContextMenuStrip();
            trayMenu.Items.Add("显示桌宠",null,delegate { Restore(); });
            trayMenu.Items.Add("修改短句",null,delegate { EditPhrases(); });
            trayMenu.Items.Add("退出",null,delegate { Close(); });
            tray.ContextMenuStrip=trayMenu;
            tray.DoubleClick+=delegate { Restore(); }; tray.Visible=true;
            MouseDown+=Down; MouseMove+=MovePet; MouseUp+=Up;
            MouseCaptureChanged+=delegate { if(!Capture) { pressed=false; state.Feeding=false; } };
            timer.Interval=25;
            timer.Tick+=delegate {
                if(!Visible) return;
                double now=clock.Elapsed.TotalSeconds;
                bool animated=pressed||now<state.BounceUntil||now<state.EatUntil;
                if(animated||now>=nextFrame) { Render(); nextFrame=now+.15; }
            };
            Shown+=delegate { Render(); timer.Start(); };
            FormClosing+=delegate { closing=true; Save(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x80000|0x80; return p; } }
        void Restore() { ClampPosition(); Show(); Render(); }
        void ResetPosition() { Rectangle a=Screen.PrimaryScreen.WorkingArea; Location=new Point(a.Right-Width-20,a.Bottom-Height-12); }
        void ClampPosition() {
            Rectangle a=Screen.FromRectangle(Bounds).WorkingArea;
            Location=new Point(Math.Max(a.Left,Math.Min(Left,a.Right-Width)),Math.Max(a.Top,Math.Min(Top,a.Bottom-Height)));
        }
        void Save() {
            config.X=Left; config.Y=Top;
            try { config.Save(settingsFile); }
            catch(Exception ex) { if(!closing) { state.Bubble="保存失败，可稍后重试"; state.BubbleUntil=clock.Elapsed.TotalSeconds+4; } System.Diagnostics.Debug.WriteLine(ex.Message); }
        }
        PointF Logical(Point p) { return new PointF(p.X/config.Scale,p.Y/config.Scale); }
        void Down(object sender,MouseEventArgs e) {
            if(e.Button==MouseButtons.Right) { menu.Show(this,e.Location); return; }
            if(e.Button!=MouseButtons.Left) return;
            pressed=true; dragged=false; downCursor=Cursor.Position; downWindow=Location;
            var p=Logical(e.Location); state.Feeding=state.Cookie.Contains(p); state.Food=p; Capture=true; Render();
        }
        void MovePet(object sender,MouseEventArgs e) {
            if(!pressed) return;
            if(state.Feeding) { state.Food=Logical(e.Location); Render(); return; }
            Point p=Cursor.Position; int dx=p.X-downCursor.X,dy=p.Y-downCursor.Y;
            if(Math.Abs(dx)+Math.Abs(dy)>5) dragged=true;
            if(dragged) { Location=new Point(downWindow.X+dx,downWindow.Y+dy); Render(); }
        }
        void Up(object sender,MouseEventArgs e) {
            if(e.Button!=MouseButtons.Left||!pressed) return;
            bool food=state.Feeding;
            if(food) { if(state.Drop(Logical(e.Location),clock.Elapsed.TotalSeconds)) Save(); }
            else if(dragged) { ClampPosition(); Save(); }
            else state.Say(clock.Elapsed.TotalSeconds);
            pressed=false; Capture=false; Render();
        }
        void EditPhrases() {
            using(var dialog=new Form()) {
                dialog.Text="我的短句 · 一行一句"; dialog.ClientSize=new Size(420,290);
                dialog.StartPosition=FormStartPosition.CenterScreen; dialog.TopMost=true;
                dialog.FormBorderStyle=FormBorderStyle.FixedDialog; dialog.MaximizeBox=false; dialog.MinimizeBox=false;
                var label=new Label{Text="点人物时轮流出现。每句最多 36 个字。",Location=new Point(18,16),AutoSize=true};
                var box=new TextBox{Multiline=true,ScrollBars=ScrollBars.Vertical,Location=new Point(18,45),Size=new Size(384,185),Text=string.Join(Environment.NewLine,config.Phrases),Font=new Font("Microsoft YaHei",10)};
                var save=new Button{Text="保存",Location=new Point(316,246),Size=new Size(86,30)};
                save.Click+=delegate {
                    var lines=new System.Collections.Generic.List<string>();
                    foreach(string line in box.Lines) { string s=line.Trim(); if(s.Length>0) lines.Add(s.Substring(0,Math.Min(s.Length,36))); if(lines.Count==100) break; }
                    if(lines.Count==0) { MessageBox.Show(dialog,"至少写一句吧。", "我的短句"); return; }
                    config.Phrases=lines.ToArray(); state.PhraseIndex=0; Save(); dialog.DialogResult=DialogResult.OK;
                };
                dialog.Controls.Add(label);dialog.Controls.Add(box);dialog.Controls.Add(save);dialog.ShowDialog();
            }
        }
        static readonly Color Ink=Color.FromArgb(34,55,62);
        static readonly Color Lime=Color.FromArgb(219,246,68);
        static readonly Color Cyan=Color.FromArgb(31,209,206);
        public Bitmap Frame(double now) {
            var bitmap=new Bitmap(Width,Height,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(bitmap)) {
                g.Clear(Color.Transparent); g.ScaleTransform(config.Scale,config.Scale);
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.SmoothingMode=SmoothingMode.AntiAlias;
                float bounce=now<state.BounceUntil?(float)(-Math.Abs(Math.Sin((state.BounceUntil-now)*Math.PI*3)) *8):0;
                g.DrawImage(art,Interaction.ImageX,Interaction.ImageY+bounce,Interaction.ImageWidth,state.ImageHeight);
                if(now<state.EatUntil) {
                    PointF m=state.Mouth; m.Y+=bounce;
                    float opening=2+(float)(Math.Abs(Math.Sin(now*15))*5);
                    using(var b=new SolidBrush(Ink)) g.FillEllipse(b,m.X-9,m.Y-opening,18,opening*2);
                    for(int i=0;i<5;i++) {
                        float age=(float)((now*1.8+i*.19)%1);
                        using(var b=new SolidBrush(Color.FromArgb((int)(255*(1-age)),221,165,91)))
                            g.FillRectangle(b,m.X+(i-2)*9*age,m.Y+20*age,3,3);
                    }
                }
                RectangleF cookie=state.Cookie;
                using(var path=HandFrame(cookie)) {
                    using(var b=new SolidBrush(Color.FromArgb(252,250,227))) g.FillPath(b,path);
                    CrayonBorder(g,path,Color.FromArgb(141,166,70));
                }
                if(!state.Feeding) DrawCookie(g,new PointF(cookie.X+20,cookie.Y+18),13);
                using(var f=new Font("Yu Gothic UI",Math.Max(9,6/config.Scale),FontStyle.Bold)) using(var b=new SolidBrush(Ink))
                    g.DrawString("おやつ",f,b,cookie.X+37,cookie.Y+9);
                if(state.Feeding) {
                    var r=state.Companion;
                    using(var p=new Pen(Cyan,2)) { p.DashStyle=DashStyle.Dot; g.DrawRectangle(p,r.X,r.Y,r.Width,r.Height); }
                    DrawCookie(g,state.Food,15);
                }
                if(now<state.BubbleUntil) DrawBubble(g,state.Bubble);
            }
            return bitmap;
        }
        static void DrawCookie(Graphics g,PointF p,float radius) {
            using(var b=new SolidBrush(Color.FromArgb(230,174,93))) g.FillEllipse(b,p.X-radius,p.Y-radius,radius*2,radius*2);
            using(var pen=new Pen(Color.FromArgb(117,74,48),2)) g.DrawEllipse(pen,p.X-radius,p.Y-radius,radius*2,radius*2);
            using(var b=new SolidBrush(Color.FromArgb(106,68,47))) {
                g.FillRectangle(b,p.X-6,p.Y-6,4,4);g.FillRectangle(b,p.X+4,p.Y-3,4,4);g.FillRectangle(b,p.X-2,p.Y+5,4,4);
            }
        }
        void DrawBubble(Graphics g,string text) {
            using(var path=HandFrame(new RectangleF(31,9,298,56))) {
                using(var b=new SolidBrush(Color.FromArgb(255,252,233))) g.FillPath(b,path);
                CrayonBorder(g,path,Color.FromArgb(125,151,61));
            }
            using(var p=new Pen(Color.FromArgb(125,151,61),2)) g.DrawLines(p,new PointF[]{new PointF(166,65),new PointF(170,76),new PointF(183,66)});
            using(var p=new Pen(Color.FromArgb(130,219,246,68),3)) g.DrawCurve(p,new PointF[]{new PointF(49,19),new PointF(112,17),new PointF(200,20),new PointF(309,17)});
            using(var font=new Font("Yu Gothic UI",Math.Max(10,8/config.Scale),FontStyle.Bold)) using(var b=new SolidBrush(Ink)) using(var fmt=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
                g.DrawString(text,font,b,new RectangleF(43,20,274,39),fmt);
        }
        static GraphicsPath HandFrame(RectangleF r) {
            var path=new GraphicsPath();
            path.AddClosedCurve(new PointF[]{new PointF(r.Left+9,r.Top+1),new PointF(r.Left+r.Width*.43f,r.Top),new PointF(r.Right-9,r.Top+2),new PointF(r.Right,r.Top+9),new PointF(r.Right-1,r.Bottom-7),new PointF(r.Right-10,r.Bottom),new PointF(r.Left+r.Width*.4f,r.Bottom-1),new PointF(r.Left+7,r.Bottom),new PointF(r.Left,r.Bottom-9),new PointF(r.Left+1,r.Top+8)},.15f);
            return path;
        }
        static void CrayonBorder(Graphics g,GraphicsPath path,Color color) {
            using(var p=new Pen(Color.FromArgb(75,color),4)) g.DrawPath(p,path);
            using(var p=new Pen(Color.FromArgb(190,color),1.5f)) {p.DashPattern=new float[]{4,.8f,1,.6f,3,.7f};g.DrawPath(p,path);}
            var save=g.Save();g.TranslateTransform(.8f,-.6f);
            using(var p=new Pen(Color.FromArgb(115,color),.8f)) {p.DashPattern=new float[]{1,1,5,.7f};g.DrawPath(p,path);}g.Restore(save);
        }
        void Render() { if(!IsHandleCreated||IsDisposed) return; using(var bitmap=Frame(clock.Elapsed.TotalSeconds)) Layered.Show(this,bitmap); }
        protected override void Dispose(bool disposing) {
            if(disposing) { timer.Dispose();tray.Visible=false;if(tray.ContextMenuStrip!=null)tray.ContextMenuStrip.Dispose();tray.Dispose();menu.Dispose();art.Dispose(); }
            base.Dispose(disposing);
        }
        [STAThread] public static int Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string root=AppDomain.CurrentDomain.BaseDirectory;
            try {
                if(args.Length>0&&args[0]=="--self-test") return SelfTest(root);
                bool first;
                using(var mutex=new System.Threading.Mutex(true,"Local\\PhotoStickerDuo_v1",out first)) {
                    if(!first) { MessageBox.Show("桌宠已经运行，可在任务栏托盘中双击找回。","口袋搭子"); return 0; }
                    string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PhotoStickerDuo","settings.xml");
                    Application.Run(new DuoWindow(Path.Combine(root,"duo.png"),file));
                }
                return 0;
            } catch(Exception ex) { File.WriteAllText(Path.Combine(root,"error.txt"),ex.ToString()); if(args.Length==0)MessageBox.Show("启动失败，详情已写入 error.txt。\n"+ex.Message,"口袋搭子"); return 1; }
        }
        static void Assert(bool condition,string name) { if(!condition)throw new Exception("FAIL: "+name); }
        static int SelfTest(string root) {
            string dir=Path.Combine(Path.GetTempPath(),"StickerDuo-test-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            string file=Path.Combine(dir,"settings.xml");
            try {
                var s=new Settings(); var model=new Interaction(s,400);
                model.Feeding=true; Assert(!model.Drop(new PointF(0,0),1)&&s.Snacks==0&&!model.Feeding,"miss resets drag without feeding");
                model.Feeding=true; Assert(model.Drop(model.Mouth,2)&&s.Snacks==1&&model.EatUntil>2,"mouth drop feeds once");
                s.Phrases=new string[]{"自定义甲","自定义乙"}; model.Say(4); Assert(model.Bubble=="自定义甲","custom phrase");model.Say(5);model.Say(6);Assert(model.Bubble=="自定义甲","phrase cycle");
                s.X=-450;s.Y=120;s.Scale=1.2f;s.Save(file);var loaded=Settings.Load(file);
                Assert(loaded.X==-450&&loaded.Y==120&&loaded.Phrases[1]=="自定义乙"&&loaded.Snacks==1&&Math.Abs(loaded.Scale-1.2)<.01,"settings round trip");
                s.Snacks=2;s.Save(file);Assert(Settings.Load(file).Snacks==2,"atomic overwrite");
                File.WriteAllText(file,"broken");Assert(Settings.Load(file).Phrases.Length>0,"corrupt settings recovery");
                using(var a=new Bitmap(Path.Combine(root,"duo.png"))) {
                    int transparent=0,opaque=0;for(int y=0;y<a.Height;y+=10)for(int x=0;x<a.Width;x+=10) {int alpha=a.GetPixel(x,y).A;if(alpha==0)transparent++;if(alpha>200)opaque++;}
                    Assert(transparent>100&&opaque>100,"real transparent art");
                }
                using(var w=new DuoWindow(Path.Combine(root,"duo.png"),file)) {
                    using(var frame=w.Frame(0)) {Assert(frame.GetPixel(0,0).A==0,"transparent render");frame.Save(Path.Combine(root,"preview.png"),ImageFormat.Png);}
                    w.state.Feeding=true;w.state.Drop(w.state.Mouth,1);using(var frame=w.Frame(1.3)) frame.Save(Path.Combine(root,"feeding-preview.png"),ImageFormat.Png);
                    w.Show();Application.DoEvents();w.Render();w.Close();
                }
                File.WriteAllText(Path.Combine(root,"test-results.txt"),"PASS: feed hit/miss, custom phrase cycle, settings round trip and overwrite, corrupted settings fallback, source transparency, render transparency, feeding frame, layered window creation and render.\r\n");
                return 0;
            } finally { foreach(string f in Directory.GetFiles(dir))File.Delete(f);Directory.Delete(dir); }
        }
    }
    static class Layered {
        [StructLayout(LayoutKind.Sequential)] struct Point { public int X,Y; public Point(int x,int y){X=x;Y=y;} }
        [StructLayout(LayoutKind.Sequential)] struct Size { public int W,H; public Size(int w,int h){W=w;H=h;} }
        [StructLayout(LayoutKind.Sequential,Pack=1)] struct Blend { public byte Op,Flags,Alpha,Format; }
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h,IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr h,IntPtr dst,ref Point pos,ref Size size,IntPtr src,ref Point origin,int key,ref Blend blend,int flags);
        public static void Show(Form form,Bitmap bitmap) {
            IntPtr screen=GetDC(IntPtr.Zero), memory=CreateCompatibleDC(screen), hBitmap=IntPtr.Zero,old=IntPtr.Zero;
            try {
                hBitmap=bitmap.GetHbitmap(Color.FromArgb(0));old=SelectObject(memory,hBitmap);
                var pos=new Point(form.Left,form.Top);var size=new Size(bitmap.Width,bitmap.Height);var origin=new Point(0,0);var blend=new Blend{Op=0,Flags=0,Alpha=255,Format=1};
                if(!UpdateLayeredWindow(form.Handle,screen,ref pos,ref size,memory,ref origin,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            } finally { if(old!=IntPtr.Zero)SelectObject(memory,old);if(hBitmap!=IntPtr.Zero)DeleteObject(hBitmap);DeleteDC(memory);ReleaseDC(IntPtr.Zero,screen); }
        }
    }
}
