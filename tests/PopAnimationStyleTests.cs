using System;
using System.IO;
using System.Reflection;

internal static class PopAnimationStyleTests
{
    private static Type style;
    private static int checks;
    private static readonly BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic;
    private static void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
    private static object Call(string name,params object[] args) { return style.GetMethod(name,Static).Invoke(null,args); }
    private static float Number(string name,params object[] args) { return (float)Call(name,args); }
    private static float Field(object pose,string name) { return (float)pose.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pose); }
    private static bool Finite(float number) { return !float.IsNaN(number) && !float.IsInfinity(number); }
    public static int Main(string[] args)
    {
        try
        {
            style=Assembly.LoadFile(Path.GetFullPath(args[0])).GetType("TotemMod.PopAnimationStyle");
            Check(style!=null,"The deployed plugin needs the compact pop animation evaluator.");
            float duration=Number("Duration",3);
            Check(duration>=0.55f && duration<=0.9f,"Minimal pop completes in less than a second at normal speed.");
            Check((int)Call("ParticleCount",0,3)==0,"Minimal respects particles disabled.");
            Check((int)Call("ParticleCount",64,3)<=12,"Minimal bounds screen coverage to twelve sparks.");
            Check((int)Call("ParticleCount",1,3)==1,"Minimal preserves a smaller requested count.");
            Check((int)Call("ParticleCount",64,0)==64,"Other styles preserve the configured particle count.");
            Check(Number("ParticleSize",3)<1 && Number("ParticleSpread",3)<1,"Minimal uses smaller sparks and tighter spread.");
            Check(Number("ParticleTime",duration,3)==Number("Duration",0),"Minimal sparks reach their fade endpoint with the icon.");
            Check(Math.Abs(Number("ParticleTime",0.4f,0)-0.4f)<0.00001f,"Existing particle timing is unchanged.");
            Check(Field(Call("Evaluate",0f,3),"Scale")==0,"Minimal begins invisibly.");
            Check(Field(Call("Evaluate",duration,3),"Scale")==0,"Minimal ends invisibly.");
            object ready=Call("Evaluate",0.15f,3),fading=Call("Evaluate",duration-0.05f,3);
            Check(Field(ready,"Scale")>=0.6f,"Minimal becomes readable quickly.");
            Check(Field(fading,"Scale")<Field(ready,"Scale")*0.15f,"Minimal shrinks smoothly before cleanup.");
            string[] fields={"X","Y","Z","Scale","Pitch","Yaw","Roll"};
            for(int mode=0;mode<4;mode++)
            {
                float length=Number("Duration",mode);
                for(int frame=0;frame<=200;frame++)
                {
                    float time=length*frame/200;
                    object pose=Call("Evaluate",time,mode);
                    foreach(string field in fields) Check(Finite(Field(pose,field)),"Every animation frame is finite.");
                    Check(Field(pose,"Scale")>=0,"Scale never turns inside-out.");
                    if(mode==3)
                    {
                        Check(Field(pose,"Scale")<=0.75f,"Minimal never grows into the old large center-screen icon.");
                        Check(Math.Abs(Field(pose,"X"))<0.1f && Math.Abs(Field(pose,"Y"))<0.06f,"Minimal stays near its small anchored area.");
                        Check(Math.Abs(Field(pose,"Yaw"))<=10 && Math.Abs(Field(pose,"Roll"))<=5,"Minimal has a gentle settle rather than a large wobble.");
                    }
                    if(mode==2) Check(Field(pose,"X")==0 && Field(pose,"Y")==0 && Field(pose,"Yaw")==0 && Field(pose,"Roll")==0,"Still keeps its static pose.");
                }
            }
            foreach(float invalid in new[] {float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1f,100f})
            {
                object pose=Call("Evaluate",invalid,3);
                foreach(string field in fields) Check(Finite(Field(pose,field)),"Invalid config/timing cannot poison transforms.");
            }
            Check((int)Call("ParticleCount",-1,3)==0 && (int)Call("ParticleCount",int.MaxValue,1)==64,"Particle counts stay bounded for malformed settings.");
            Console.WriteLine("PASS "+checks+" compact pop timing, screen coverage, fade, compatibility and finite-pose checks");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
