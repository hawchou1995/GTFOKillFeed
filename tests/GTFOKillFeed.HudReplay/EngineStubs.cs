// API-boundary doubles only. The test executes the actual production Tick/Draw; it is not a Unity visual test.
namespace UnityEngine
{
    public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} }
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;} }
    public struct Bounds { public Vector3 min,max,size; }
    public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;} }
    public struct Rect { public float x,y,width,height; public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;} }
    public class GameObject { public bool activeInHierarchy=true; public int layer=5; }
    public class Transform
    {
        public static int Writes; public bool Inside=true; public Transform parent;
        private Vector3 _position=new(0,0,100),_scale=new(1,1,1);
        public Vector3 localPosition {get=>_position;set{Writes++;_position=value;}}
        public Vector3 localScale {get=>_scale;set{Writes++;_scale=value;}}
        public bool IsChildOf(Transform root)=>Inside && root!=null;
        public Vector3 TransformPoint(Vector3 v)=>v;
    }
    public class RectTransform:Transform {}
    public enum SpriteDrawMode {Simple,Sliced,Tiled}
    public class SpriteRenderer {public Sprite sprite=new();public Transform transform=new(){parent=new()};public GameObject gameObject=new();public SpriteDrawMode drawMode=SpriteDrawMode.Sliced;public Vector2 size=new(240,5);}
    public class Sprite {public Bounds bounds=new(){min=new(-1,-5,0),max=new(0,5,0),size=new(1,10,0)};}
    public class Texture2D {public int width=2560,height=1440;}
    public class Camera
    {
        public bool enabled; public GameObject gameObject=new(); public int cullingMask=32,pixelWidth=2560,pixelHeight=1440;
        public Rect rect=new(0,0,1,1);public Texture2D targetTexture=new();
        public Vector3 WorldToScreenPoint(Vector3 v)=>v;
    }
    public static class Screen {public static int width=2560,height=1440;}
    public static class Mathf {public static float Clamp(float v,float min,float max)=>Math.Clamp(v,min,max);}
    public enum TextAnchor {MiddleRight,MiddleCenter}
    public enum FontStyle {Bold}
    public enum TextClipping {Clip}
    public enum ScaleMode {ScaleToFit}
    public class RectOffset {public RectOffset(int a,int b,int c,int d){}}
    public class GUIContent {public string text;public GUIContent(string text){this.text=text;}}
    public class GUIStyle
    {
        public TextAnchor alignment; public FontStyle fontStyle; public TextClipping clipping;public bool richText;
        public RectOffset padding;public int fontSize;
        public GUIStyle(GUIStyle other){} public Vector2 CalcSize(GUIContent c)=>new(c.text.Length*8,20);
    }
    public class GUISkin {public GUIStyle label=new(null);}
    public struct Quaternion {public static Quaternion identity=>default;}
    public struct Matrix4x4
    {
        public float x,y,scale; public static Matrix4x4 TRS(Vector3 v,Quaternion q,Vector3 s)=>new(){x=v.x,y=v.y,scale=s.x};
        public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b)=>b;
    }
    public static class GUI
    {
        public static Color color;public static Matrix4x4 matrix;public static GUISkin skin=new();
        public static int Icons;public static List<string> Labels=new();
        public static void DrawTexture(Rect r,Texture2D t,ScaleMode s,bool alpha){Icons++;}
        public static void Label(Rect r,GUIContent text,GUIStyle style){Labels.Add(text.text);}
        public static void Label(Rect r,string text,GUIStyle style){Labels.Add(text);}
    }
}
public enum eGameStateName {InLevel,InLobby}
public static class GameStateManager {public static eGameStateName CurrentStateName=eGameStateName.InLevel;}
public static class GuiManager {public static PlayerGuiLayer PlayerLayer;}
public class PlayerGuiLayer
{
    public bool Visible=true; public bool IsVisible()=>Visible;
    public PUI_LocalPlayerStatus m_playerStatus=new(); public GuiLayerBase GuiLayerBase=new();
}
public class PUI_LocalPlayerStatus
{
    public UnityEngine.GameObject gameObject=new();public UnityEngine.SpriteRenderer m_health1=new();
    public float m_healthWidth=240;public UnityEngine.Vector2 m_healthStartPos=new(600,700);
}
public class UI_Canvas
{
    public UnityEngine.Camera m_UICamera=new();public UnityEngine.RectTransform m_rectTransform=new();
    public float m_pixelWidth=2560,m_pixelHeight=1440;
}
public class GuiLayerBase
{
    public UI_Canvas m_cellUICanvas=new();public UnityEngine.Camera World;
    public UnityEngine.Camera GetWorldCamera()=>World;
}
namespace HawChow.GTFOKillFeed
{
    internal enum KillTier {Minor,Elite,Boss}
    internal static class KillPolicy {internal static bool IsFinite(float value)=>float.IsFinite(value);}
    internal class Setting {internal bool Value=true;}
    internal class Log {internal List<string> Lines=new();internal void LogInfo(string line)=>Lines.Add(line);}
    internal class Plugin {internal static Setting KillCounterEnabled=new();internal static Plugin Instance=new();internal Log Log=new();}
    internal static class KillHud {internal static UnityEngine.Color Ink=new(.91f,.925f,.92f),Accent=new(.9f,.67f,.34f);}
    internal static class KillTracker {internal static KillCounterTotals Counters=new();}
}
