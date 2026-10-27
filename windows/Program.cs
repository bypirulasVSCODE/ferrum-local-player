using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

internal sealed class FerrumContext : ApplicationContext {
 const int Port=43821;
 const int WM_CLOSE=0x0010, SW_RESTORE=9;
 readonly TcpListener listener;
 readonly byte[] page;
 readonly NotifyIcon tray;
 readonly Icon appIcon;
 readonly MiniForm mini;
 readonly System.Windows.Forms.Timer miniTimer,windowTimer;
 static Mutex mutex;
 static string miniTitle="",miniArtist="";
 static bool miniPlaying=false;
 static readonly object commandLock=new object(); static string miniCommand="";
 static IntPtr appWindow=IntPtr.Zero;
 DateTime startedAt=DateTime.UtcNow;
 bool exiting=false;

 [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback,IntPtr extra);
 [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
 [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
 delegate bool EnumWindowsProc(IntPtr window,IntPtr extra);

 FerrumContext() {
  using(Stream input=Assembly.GetExecutingAssembly().GetManifestResourceStream("FerrumPlayer.html"))
  using(MemoryStream output=new MemoryStream()) {
   if(input==null) throw new InvalidOperationException("Falta la interfaz integrada.");
   input.CopyTo(output); page=output.ToArray();
  }
  listener=new TcpListener(IPAddress.Loopback,Port); listener.Start(8);
  var thread=new Thread(Serve){IsBackground=true,Name="FERRUM local server"}; thread.Start();
  appIcon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
  var menu=new ContextMenuStrip();
  menu.Items.Add("Abrir FERRUM",null,delegate{OpenApp();});
  menu.Items.Add("Salir de FERRUM",null,delegate{Exit();});
  tray=new NotifyIcon{Icon=appIcon??SystemIcons.Application,Text="FERRUM está listo",ContextMenuStrip=menu,Visible=true};
  tray.DoubleClick+=delegate{OpenApp();};
  mini=new MiniForm(delegate{Exit();},delegate(string command){QueueMiniCommand(command);});
  miniTimer=new System.Windows.Forms.Timer{Interval=450};
  miniTimer.Tick+=delegate{mini.RefreshState();};
  miniTimer.Start();
  windowTimer=new System.Windows.Forms.Timer{Interval=800};
  windowTimer.Tick+=delegate{TrackMainWindow();};
  windowTimer.Start();
  tray.ShowBalloonTip(2200,"FERRUM","La app está abierta. Usa su icono para salir.",ToolTipIcon.Info);
  OpenApp();
 }

 public static void Start() {
  bool created; mutex=new Mutex(true,"Local\\FERRUM-Player-43821",out created);
  if(!created){MessageBox.Show("FERRUM ya está abierto. Busca su ventana o icono junto al reloj.","FERRUM");mutex.Dispose();return;}
  try{Application.Run(new FerrumContext());}
  catch(Exception ex){MessageBox.Show("No se pudo abrir FERRUM en el puerto local 43821.\n\n"+ex.Message,"FERRUM",MessageBoxButtons.OK,MessageBoxIcon.Error);}
  finally{mutex.ReleaseMutex();mutex.Dispose();}
 }

 void TrackMainWindow() {
  if(exiting)return;
  if(appWindow!=IntPtr.Zero){if(!IsWindow(appWindow))Exit();return;}
  appWindow=FindMainWindow();
  if(appWindow==IntPtr.Zero&&DateTime.UtcNow-startedAt>TimeSpan.FromSeconds(60))Exit();
 }

 static IntPtr FindMainWindow() {
  IntPtr found=IntPtr.Zero;
  EnumWindows(delegate(IntPtr window,IntPtr extra){
   if(!IsWindowVisible(window))return true;
   var title=new StringBuilder(512); GetWindowText(window,title,title.Capacity);
   string value=title.ToString();
   if(value.IndexOf("FERRUM — música local",StringComparison.OrdinalIgnoreCase)>=0 || value.IndexOf("FERRUM - música local",StringComparison.OrdinalIgnoreCase)>=0){found=window;return false;}
   return true;
  },IntPtr.Zero);
  return found;
 }

 void Serve(){while(true){TcpClient c;try{c=listener.AcceptTcpClient();}catch{return;}using(c)Handle(c);}}

 void Handle(TcpClient c){
  try{
   c.ReceiveTimeout=5000;c.SendTimeout=5000;
   using(NetworkStream s=c.GetStream())using(StreamReader r=new StreamReader(s,Encoding.ASCII,false,1024,true)){
    string first=r.ReadLine();if(String.IsNullOrEmpty(first))return;
    string[] p=first.Split(' ');string method=p[0],raw=p.Length>1?p[1]:"",path=raw.Split('?')[0];
    string line;int bytes=first.Length;
    while(!String.IsNullOrEmpty(line=r.ReadLine())){bytes+=line.Length;if(bytes>16384){Send(s,431,"text/plain; charset=utf-8",Encoding.UTF8.GetBytes("Solicitud demasiado grande"),false);return;}}
    if(path=="/mini/control"&&method=="GET"){string command=QueryValue(raw,"action");if(command=="close")mini.BeginInvoke((MethodInvoker)delegate{Exit();});else QueueMiniCommand(command);Send(s,204,"text/plain; charset=utf-8",new byte[0],false);return;}
    if(path=="/mini/command"&&method=="GET"){string command=TakeMiniCommand();Send(s,200,"text/plain; charset=utf-8",Encoding.UTF8.GetBytes(command),false);return;}
    if(method!="GET"&&method!="HEAD"){Send(s,405,"text/plain; charset=utf-8",Encoding.UTF8.GetBytes("Método no permitido"),method=="HEAD");return;}
    if(path=="/mini/state"&&method=="GET"){UpdateMini(raw);Send(s,204,"text/plain; charset=utf-8",new byte[0],false);return;}
    if(path!="/"){Send(s,404,"text/plain; charset=utf-8",Encoding.UTF8.GetBytes("No encontrado"),method=="HEAD");return;}
    Send(s,200,"text/html; charset=utf-8",page,method=="HEAD");
   }
  }catch(IOException){}catch(SocketException){}catch(ObjectDisposedException){}
 }

 static string QueryValue(string raw,string name){try{string q=raw.IndexOf('?')>=0?raw.Substring(raw.IndexOf('?')+1):"";foreach(string pair in q.Split('&')){string[] kv=pair.Split(new char[]{'='},2);if(kv.Length==2&&Uri.UnescapeDataString(kv[0])==name)return Uri.UnescapeDataString(kv[1].Replace("+"," "));}}catch{}return "";}
 void UpdateMini(string raw){try{miniTitle=QueryValue(raw,"title");miniArtist=QueryValue(raw,"artist");miniPlaying=QueryValue(raw,"playing")=="true";if(miniTitle.Length>100)miniTitle=miniTitle.Substring(0,100);if(miniArtist.Length>120)miniArtist=miniArtist.Substring(0,120);}catch{}}
 internal static void QueueMiniCommand(string command){lock(commandLock)miniCommand=command;}
 static string TakeMiniCommand(){lock(commandLock){string command=miniCommand;miniCommand="";return command;}}
 static void Send(Stream s,int status,string type,byte[] body,bool head){string reason=status==200?"OK":status==204?"No Content":status==404?"Not Found":status==405?"Method Not Allowed":"Request Header Too Large";string h="HTTP/1.1 "+status+" "+reason+"\r\nContent-Type: "+type+"\r\nContent-Length: "+body.Length+"\r\nCache-Control: no-store\r\nReferrer-Policy: strict-origin-when-cross-origin\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline'; worker-src 'self' blob:; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src 'self' blob:; connect-src 'self'; form-action 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'\r\nConnection: close\r\n\r\n";byte[] b=Encoding.ASCII.GetBytes(h);s.Write(b,0,b.Length);if(!head)s.Write(body,0,body.Length);s.Flush();}
 static string Edge(){string[] k={@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe",@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"};foreach(RegistryKey hive in new[]{Registry.LocalMachine,Registry.CurrentUser})foreach(string key in k)using(RegistryKey x=hive.OpenSubKey(key)){string v=x==null?null:x.GetValue(null) as string;if(!String.IsNullOrEmpty(v)&&File.Exists(v))return v;}foreach(Environment.SpecialFolder f in new[]{Environment.SpecialFolder.ProgramFiles,Environment.SpecialFolder.ProgramFilesX86,Environment.SpecialFolder.LocalApplicationData}){string p=Path.Combine(Environment.GetFolderPath(f),"Microsoft","Edge","Application","msedge.exe");if(File.Exists(p))return p;}return null;}
 internal static string MiniTitle{get{return miniTitle;}} internal static string MiniArtist{get{return miniArtist;}} internal static bool MiniPlaying{get{return miniPlaying;}}
 static void OpenApp(){try{if(appWindow!=IntPtr.Zero&&IsWindow(appWindow)){ShowWindow(appWindow,SW_RESTORE);SetForegroundWindow(appWindow);return;}IntPtr existing=FindMainWindow();if(existing!=IntPtr.Zero){appWindow=existing;ShowWindow(existing,SW_RESTORE);SetForegroundWindow(existing);return;}string url="http://127.0.0.1:43821/",edge=Edge();if(edge!=null)Process.Start(new ProcessStartInfo(edge,"--app="+url+" --disable-extensions"){UseShellExecute=false});else Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception e){MessageBox.Show("Abre esta dirección en tu navegador:\nhttp://127.0.0.1:43821/\n\n"+e.Message,"FERRUM");}}
 void Exit(){if(exiting)return;exiting=true;miniTimer.Stop();windowTimer.Stop();if(appWindow!=IntPtr.Zero&&IsWindow(appWindow))PostMessage(appWindow,WM_CLOSE,IntPtr.Zero,IntPtr.Zero);tray.Visible=false;tray.Dispose();mini.Close();mini.Dispose();if(appIcon!=null)appIcon.Dispose();listener.Stop();miniTimer.Dispose();windowTimer.Dispose();ExitThread();}
}

internal static class Program { [STAThread] static void Main(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);FerrumContext.Start();} }

internal sealed class MiniForm : Form {
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern int GetWindowText(IntPtr hWnd,StringBuilder text,int count);
 [DllImport("user32.dll")] static extern bool ReleaseCapture();
 [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd,int msg,IntPtr w,IntPtr l);
 readonly Action closeAction;
 readonly Action<string> controlAction;
 Label title,artist;
 Button toggle;
 public MiniForm(Action close,Action<string> control){
  closeAction=close;controlAction=control;Text="FERRUM mini";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;Width=372;Height=118;BackColor=Color.FromArgb(19,15,17);Padding=new Padding(1);Opacity=.98;MinimumSize=new Size(372,118);MaximumSize=new Size(372,118);
  var shell=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(24,19,21),Padding=new Padding(10)};Controls.Add(shell);
  var accent=new Panel{Dock=DockStyle.Top,Height=3,BackColor=Color.FromArgb(231,39,68)};shell.Controls.Add(accent);
  var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=2,BackColor=Color.FromArgb(24,19,21),Padding=new Padding(0,7,0,0)};
  grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,50));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,146));
  grid.RowStyles.Add(new RowStyle(SizeType.Percent,55));grid.RowStyles.Add(new RowStyle(SizeType.Percent,45));shell.Controls.Add(grid);grid.BringToFront();
  var mark=new Label{Text="F",Dock=DockStyle.Fill,Margin=new Padding(0,1,10,3),BackColor=Color.FromArgb(128,22,43),ForeColor=Color.White,Font=new Font("Segoe UI",22,FontStyle.Bold),TextAlign=ContentAlignment.MiddleCenter};grid.Controls.Add(mark,0,0);grid.SetRowSpan(mark,2);
  var detail=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,BackColor=grid.BackColor,Margin=new Padding(0,3,4,2)};detail.RowStyles.Add(new RowStyle(SizeType.Percent,55));detail.RowStyles.Add(new RowStyle(SizeType.Percent,45));
  title=new Label{Text="FERRUM · nada suena",Dock=DockStyle.Fill,ForeColor=Color.White,Font=new Font("Segoe UI",10,FontStyle.Bold),AutoEllipsis=true,TextAlign=ContentAlignment.BottomLeft};
  artist=new Label{Text="Música a tu alcance",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(174,160,164),Font=new Font("Segoe UI",8),AutoEllipsis=true,TextAlign=ContentAlignment.TopLeft};detail.Controls.Add(title,0,0);detail.Controls.Add(artist,0,1);grid.Controls.Add(detail,1,0);grid.SetRowSpan(detail,2);
  var closeButton=MakeButton("×",Color.FromArgb(69,41,47));closeButton.Font=new Font("Segoe UI",12,FontStyle.Regular);closeButton.Click+=delegate{closeAction();};grid.Controls.Add(closeButton,2,0);
  var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,FlowDirection=FlowDirection.RightToLeft,BackColor=grid.BackColor,Margin=new Padding(0,2,0,0),Padding=Padding.Empty};
  Button next=MakeButton("▶|",Color.FromArgb(48,34,38)),prev=MakeButton("|◀",Color.FromArgb(48,34,38));toggle=MakeButton("▶",Color.FromArgb(118,25,44));
  prev.Click+=delegate{controlAction("previous");};toggle.Click+=delegate{controlAction("play");};next.Click+=delegate{controlAction("next");};buttons.Controls.Add(next);buttons.Controls.Add(toggle);buttons.Controls.Add(prev);grid.Controls.Add(buttons,2,1);
  EnableDrag(this);
 }
 Button MakeButton(string text,Color back){return new Button{Text=text,Width=40,Height=31,FlatStyle=FlatStyle.Flat,BackColor=back,ForeColor=Color.FromArgb(255,105,120),Font=new Font("Segoe UI",8,FontStyle.Bold),Margin=new Padding(3,5,0,0),Cursor=Cursors.Hand};}
 void EnableDrag(Control control){if(control is Button)return;control.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}};foreach(Control child in control.Controls)EnableDrag(child);}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);using(var path=new GraphicsPath()){int r=16,w=Width,h=Height;path.AddArc(0,0,r,r,180,90);path.AddArc(w-r-1,0,r,r,270,90);path.AddArc(w-r-1,h-r-1,r,r,0,90);path.AddArc(0,h-r-1,r,r,90,90);path.CloseFigure();Region=new Region(path);}}
 public void RefreshState(){if(IsDisposed)return;title.Text=String.IsNullOrEmpty(FerrumContext.MiniTitle)?"FERRUM · nada suena":FerrumContext.MiniTitle;artist.Text=String.IsNullOrEmpty(FerrumContext.MiniArtist)?"Música a tu alcance":FerrumContext.MiniArtist;toggle.Text=FerrumContext.MiniPlaying?"❚❚":"▶";IntPtr foreground=GetForegroundWindow();if(foreground==Handle)return;var caption=new StringBuilder(256);GetWindowText(foreground,caption,caption.Capacity);string value=caption.ToString();if(value.IndexOf("FERRUM — música local",StringComparison.OrdinalIgnoreCase)>=0||value.IndexOf("FERRUM - música local",StringComparison.OrdinalIgnoreCase)>=0){if(Visible)Hide();return;}if(!String.IsNullOrEmpty(FerrumContext.MiniTitle)){if(!Visible){Left=Screen.PrimaryScreen.WorkingArea.Right-Width-18;Top=Screen.PrimaryScreen.WorkingArea.Bottom-Height-18;Show();}}else if(Visible)Hide();}
}
