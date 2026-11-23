using HawChow.GTFOKillFeed;
using UnityEngine;

int passed=0;
void Check(bool value,string message){if(!value)throw new Exception("FAIL HUD_REPLAY: "+message);passed++;}
PlayerGuiLayer Setup()
{
    Screen.width=2560;Screen.height=1440;
    GuiManager.PlayerLayer=new();KillCounterHud.Reset();KillTracker.Counters.Reset();Plugin.Instance.Log.Lines.Clear();
    Plugin.KillCounterEnabled.Value=true;GameStateManager.CurrentStateName=eGameStateName.InLevel;
    GUI.Icons=0;GUI.Labels.Clear();Transform.Writes=0;GUI.matrix=default;return GuiManager.PlayerLayer;
}
void Draw(){KillCounterHud.Tick(100);KillCounterHud.Draw(new(),new(),new());}
float Field(string line,string name)=>float.Parse(line.Split(name+"=")[1].Split(';')[0]);
string Receipt(string camera)=>Plugin.Instance.Log.Lines.Single(x=>x.Contains("placed;")&&x.Contains("camera="+camera+";"));
bool NoCorner()=>!Plugin.Instance.Log.Lines.Any(x=>x.Contains("window-size-fallback")||x.Contains("window-bottom-right"));
// The manual CellUI compositor renders into its own texture and lays its canvas out in that texture's pixels.
void Compositor(PlayerGuiLayer hud,int width,int height)
{
    var canvas=hud.GuiLayerBase.m_cellUICanvas;
    canvas.m_UICamera.targetTexture.width=width;canvas.m_UICamera.targetTexture.height=height;
    canvas.m_pixelWidth=width;canvas.m_pixelHeight=height;
}
// Site bar: right edge 1368 and bottom edge 1025.2 in 1080p texture pixels, i.e. 1824/1366.9 in a 1440p window.
void SiteBar(PlayerGuiLayer hud)=>hud.m_playerStatus.m_healthStartPos=new(1368,57.325f);

var layer=Setup();KillTracker.Counters.Add(KillTier.Elite);Draw();
Check(GUI.Icons==3 && GUI.Labels.Count==6,"recorded disabled manual camera renders all three icon/number groups");
Check(GUI.Labels.Contains("1") && GUI.Labels.Contains("0"),"production draw consumes the actual totals, including zero");
Check(Plugin.Instance.Log.Lines.Any(x=>x.Contains("placed;")&&x.Contains("cellui-manual-verified")),"placed receipt identifies the real compositor");
Check(Transform.Writes==0,"production Tick/Draw never moves or rescales the native health bar");
string anchor=Receipt("cellui-manual-verified");
Check(Field(anchor,"healthBottom")==742.5f,"sliced health bar bottom uses its actual 5-unit height, not the source sprite height");
Check(Math.Abs(Field(anchor,"x")+Field(anchor,"width")-Field(anchor,"healthRight"))<0.1f,"the row's right edge is the measured health-bar right edge");
layer=Setup();layer.m_playerStatus.m_health1.transform.localScale=new(0,1,1);Transform.Writes=0;Draw();
Check(Plugin.Instance.Log.Lines.Single(x=>x.Contains("placed;")).Split("healthRight=")[1].Split(';')[0]==anchor.Split("healthRight=")[1].Split(';')[0],"zero-health scale does not move the full-length right edge");
Check(Transform.Writes==0,"zero-health measurement remains read-only");
foreach(string invalid in new[]{"root","mask","texture","canvas","viewport"})
{
    layer=Setup();var canvas=layer.GuiLayerBase.m_cellUICanvas;
    if(invalid=="root")layer.m_playerStatus.m_health1.transform.Inside=false;
    if(invalid=="mask")canvas.m_UICamera.cullingMask=0;
    if(invalid=="texture")canvas.m_UICamera.targetTexture.width=1280;
    if(invalid=="canvas")canvas.m_pixelWidth=1280;
    if(invalid=="viewport")canvas.m_UICamera.rect=new(0,0,.5f,1);
    Draw();Check(GUI.Icons==0 && Plugin.Instance.Log.Lines.Any(x=>x.Contains("held;")),"unverified compositor refuses with a diagnostic: "+invalid);
}
// Survival L4076/L4078: the window becomes 1440p while the render texture and canvas stay 1080p.
layer=Setup();Compositor(layer,1920,1080);SiteBar(layer);Screen.width=1920;Screen.height=1080;Draw();
string direct=Receipt("cellui-manual-verified");
Check(Math.Abs(Field(direct,"healthRight")-1368f)<0.1f && Math.Abs(Field(direct,"healthBottom")-1025.2f)<0.1f
    && direct.Contains("screen=1920x1080;anchor=native-full-health-bar"),"the recorded 1080p compositor measures the real bar edge in its own pixels");
KillTracker.Counters.Add(KillTier.Elite);Screen.width=2560;Screen.height=1440;
GUI.Labels.Clear();KillCounterHud.Draw(new(),new(),new());
Check(GUI.Icons==3,"draw cannot reuse the stale 1080p measurement before the next tick");
KillCounterHud.Tick(100.01f);KillCounterHud.Draw(new(),new(),new());
string scaled=Receipt("cellui-manual-scaled");
Check(GUI.Icons==6 && GUI.Labels.Contains("1") && scaled.Contains("screen=2560x1440;anchor=native-full-health-bar;camera=cellui-manual-scaled;scale=1920x1080->2560x1440"),
    "a texture smaller than the window projects the same bar edge in viewport space, scales it by the window and says so");
Check(Math.Abs(Field(scaled,"healthRight")-1824f)<0.1f && Math.Abs(Field(scaled,"healthBottom")-1366.9f)<0.1f,
    "recorded 1368 -> 1824 and 1025.2 -> 1366.9: the scaled edge equals the site's 1440p receipt");
Check(Math.Abs(Field(scaled,"x")-1640f)<0.1f && Math.Abs(Field(scaled,"y")-1374.9f)<0.1f
    && Math.Abs(Field(scaled,"width")-184f)<0.1f && Math.Abs(Field(scaled,"height")-26.7f)<0.1f,
    "the scaled row keeps the recorded 1640.0/1374.9 at 184.0x26.7 under the real bar");
Check(Math.Abs(Field(scaled,"x")+Field(scaled,"width")-Field(scaled,"healthRight"))<0.1f,
    "the scaled row's right edge is the health-bar coordinate, not a window corner");
Check(NoCorner() && Transform.Writes==0,"the scaled path never uses a window corner and never writes a native object");
Compositor(layer,2560,1440);KillCounterHud.Tick(100.30f);KillCounterHud.Draw(new(),new(),new());
Check(GUI.Icons==9 && Plugin.Instance.Log.Lines.Any(x=>x.Contains("screen=2560x1440;anchor=native-full-health-bar;camera=cellui-manual-verified")&&x.Contains("counts=0/1/0")) && Transform.Writes==0,
    "a caught-up native HUD returns to direct texture pixels without changing native objects or losing totals");
// The same compositor can be 1080p from the first probe: no window-sized anchor ever existed to fall back to.
layer=Setup();Compositor(layer,1920,1080);SiteBar(layer);KillTracker.Counters.Add(KillTier.Boss);Draw();
scaled=Receipt("cellui-manual-scaled");
Check(GUI.Icons==3 && GUI.Labels.Contains("0") && scaled.Contains("scale=1920x1080->2560x1440")
    && Math.Abs(Field(scaled,"healthRight")-1824f)<0.1f && scaled.Contains("counts=0/0/1"),
    "a compositor that is 1080p from the first frame scales immediately with the live tally");
Check(NoCorner() && Transform.Writes==0,"the first-frame scaled placement still never invents a corner or writes a native object");
// Reverse transition: a 1440p compositor can outlive a switch back to a 1080p window.
layer=Setup();Draw();
string high=Receipt("cellui-manual-verified");
Check(Math.Abs(Field(high,"healthRight")-600f)<0.1f && Math.Abs(Field(high,"healthBottom")-742.5f)<0.1f && high.Contains("screen=2560x1440"),
    "a 1440p compositor starts from a proved direct anchor");
KillTracker.Counters.Add(KillTier.Boss);Screen.width=1920;Screen.height=1080;KillCounterHud.Draw(new(),new(),new());
Check(GUI.Icons==3,"draw cannot reuse the stale 1440p measurement in a smaller window");
KillCounterHud.Tick(100.01f);KillCounterHud.Draw(new(),new(),new());
scaled=Receipt("cellui-manual-scaled");
Check(GUI.Icons==6 && scaled.Contains("screen=1920x1080;anchor=native-full-health-bar;camera=cellui-manual-scaled;scale=2560x1440->1920x1080") && scaled.Contains("counts=0/0/1"),
    "reverse mismatch scales the same bar edge down into the smaller window and keeps the tally");
Check(Math.Abs(Field(scaled,"healthRight")-450f)<0.1f && Math.Abs(Field(scaled,"healthBottom")-556.9f)<0.1f,
    "reverse scaling multiplies the viewport answer by the current window with a single y flip");
Check(Math.Abs(Field(scaled,"x")+Field(scaled,"width")-Field(scaled,"healthRight"))<0.1f && NoCorner() && Transform.Writes==0,
    "reverse scaling keeps the row on the bar edge without a corner or a native write");
Compositor(layer,1920,1080);KillCounterHud.Tick(100.30f);KillCounterHud.Draw(new(),new(),new());
Check(GUI.Icons==9 && Plugin.Instance.Log.Lines.Any(x=>x.Contains("screen=1920x1080;anchor=native-full-health-bar;camera=cellui-manual-verified") && x.Contains("counts=0/0/1")) && Transform.Writes==0,
    "reverse recovery reanchors without changing native objects or losing totals");
// Every refused chain must stay refused: there is no window-corner destination left anywhere.
layer=Setup();Compositor(layer,1920,1080);Screen.width=2560;Screen.height=1080;Draw();
Check(GUI.Icons==0 && Plugin.Instance.Log.Lines.Any(x=>x.Contains("held;reason=manual-canvas-target-size-mismatch")) && NoCorner(),
    "a different aspect ratio refuses instead of scaling the edge or using a corner");
layer=Setup();Screen.width=1920;Screen.height=1080;layer.GuiLayerBase.m_cellUICanvas.m_UICamera.rect=new(0,0,.5f,1);Draw();
Check(GUI.Icons==0 && NoCorner(),"a size mismatch does not mask a partial viewport");
layer=Setup();Screen.width=1920;Screen.height=1080;layer.GuiLayerBase.m_cellUICanvas.m_pixelWidth=1200;Draw();
Check(GUI.Icons==0 && NoCorner(),"a size mismatch does not mask a corrupt canvas size");
layer=Setup();Compositor(layer,1920,1080);Draw();
var splitCanvas=layer.GuiLayerBase.m_cellUICanvas;
splitCanvas.m_pixelWidth=2560;splitCanvas.m_pixelHeight=1440;
KillCounterHud.Tick(100.30f);KillCounterHud.Draw(new(),new(),new());
Check(GUI.Icons==3 && Plugin.Instance.Log.Lines.Any(x=>x.Contains("target=1920x1080;canvas=2560x1440;screen=2560x1440")),
    "a split canvas revokes the previous scaled placement rather than reusing the last anchor or drawing elsewhere");
layer=Setup();Compositor(layer,1920,1080);Screen.width=2559;Draw();
Check(GUI.Icons==0,"a near but unequal aspect ratio does not authorize a full-window projection");
layer=Setup();Compositor(layer,1920,1080);layer.GuiLayerBase.m_cellUICanvas.m_UICamera.ViewportDepth=-1f;Draw();
Check(GUI.Icons==0 && Plugin.Instance.Log.Lines.Any(x=>x.Contains("held;reason=health-projection-invalid")) && NoCorner(),
    "a camera-behind projection is held instead of being moved to a corner");
layer=Setup();Draw();layer.Visible=false;KillCounterHud.Draw(new(),new(),new());
Check(GUI.Icons==3,"hiding the native HUD also suppresses an already-measured direct placement");
layer=Setup();Compositor(layer,1920,1080);Draw();layer.Visible=false;KillCounterHud.Draw(new(),new(),new());
Check(GUI.Icons==3,"hiding the native HUD also suppresses an already-measured scaled placement");
layer=Setup();layer.GuiLayerBase.m_cellUICanvas=null;layer.GuiLayerBase.World=new(){enabled=true,targetTexture=null};Draw();
Check(GUI.Icons==3 && Plugin.Instance.Log.Lines.Any(x=>x.Contains("camera=hud-world-verified")),"verified direct HUD world camera remains supported");
foreach(float offset in new[]{-700f,-582f})
{
    layer=Setup();Screen.width=1920;Screen.height=1080;layer.GuiLayerBase.m_cellUICanvas=null;
    layer.GuiLayerBase.World=new(){enabled=true,targetTexture=null,pixelWidth=1920,pixelHeight=1080,ProjectOffset=offset};Draw();
    Check(GUI.Icons==0 && Plugin.Instance.Log.Lines.Any(x=>x.Contains("held;")) && NoCorner(),
        "an unusable world anchor is held instead of borrowing a corner: "+offset);
}
layer=Setup();Screen.width=1920;Screen.height=1080;Draw();
layer.m_playerStatus=new();KillCounterHud.Draw(new(),new(),new());
Check(GUI.Icons==3,"replacement status cannot use the old measurement even if the old object is still active");
layer=Setup();Screen.width=1920;Screen.height=1080;Draw();
GuiManager.PlayerLayer=new(){m_playerStatus=layer.m_playerStatus};KillCounterHud.Draw(new(),new(),new());
Check(GUI.Icons==3,"a replacement layer sharing the same status still needs a fresh measurement");
layer=Setup();layer.m_playerStatus.m_health1.sprite=null;Draw();
Check(GUI.Icons==0 && Plugin.Instance.Log.Lines.Any(x=>x.Contains("health-sprite-unavailable")),"missing sprite has its own reason, not an ambiguous camera error");
layer=Setup();layer.GuiLayerBase.m_cellUICanvas.m_UICamera=null;Draw();
Check(GUI.Icons==0 && Plugin.Instance.Log.Lines.Any(x=>x.Contains("manual-canvas-camera-unavailable")),"missing manual camera has its own reason");
layer=Setup();layer.Visible=false;Draw();Check(GUI.Icons==0,"hidden native HUD hides the counter");
layer=Setup();GameStateManager.CurrentStateName=eGameStateName.InLobby;Draw();Check(GUI.Icons==0,"no counter in the lobby");
layer=Setup();Plugin.KillCounterEnabled.Value=false;Draw();Check(GUI.Icons==0,"counter display switch gates drawing");
layer=Setup();KillCounterHud.Tick(100);KillCounterHud.Draw(null,null,null);
Check(GUI.Labels.Contains("I")&&GUI.Labels.Contains("II")&&GUI.Labels.Contains("III")&&GUI.Labels.Count==9,"missing icons preserve all tier labels and numbers");
Console.WriteLine($"HUD_REPLAY=PASS;ASSERTIONS={passed};PRODUCTION=Tick+Draw;ENGINE=API-boundary-doubles;GAMEPLAY=NOT-RUN");
