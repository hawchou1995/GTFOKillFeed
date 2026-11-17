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
var layer=Setup();KillTracker.Counters.Add(KillTier.Elite);Draw();
Check(GUI.Icons==3 && GUI.Labels.Count==6,"recorded disabled manual camera renders all three icon/number groups");
Check(GUI.Labels.Contains("1") && GUI.Labels.Contains("0"),"production draw consumes the actual totals, including zero");
Check(Plugin.Instance.Log.Lines.Any(x=>x.Contains("placed;")&&x.Contains("cellui-manual-verified")),"placed receipt identifies the real compositor");
Check(Transform.Writes==0,"production Tick/Draw never moves or rescales the native health bar");
string anchor=Plugin.Instance.Log.Lines.Single(x=>x.Contains("placed;"));
Check(float.Parse(anchor.Split("healthBottom=")[1].Split(';')[0])==742.5f,"sliced health bar bottom uses its actual 5-unit height, not the source sprite height");
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
layer=Setup();Screen.width=1920;Screen.height=1080;Draw();
Check(GUI.Icons==0 && Plugin.Instance.Log.Lines.Any(x=>x.Contains("target=2560x1440")&&x.Contains("screen=1920x1080")),
    "different target/window grids are refused explicitly, not silently interpreted as the same pixels");
layer=Setup();layer.GuiLayerBase.m_cellUICanvas=null;layer.GuiLayerBase.World=new(){enabled=true,targetTexture=null};Draw();
Check(GUI.Icons==3,"verified direct HUD world camera remains supported");
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
