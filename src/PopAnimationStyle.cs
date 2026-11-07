using System;

namespace TotemMod
{
    internal struct PopIconPose
    {
        internal float X,Y,Z,Scale,Pitch,Yaw,Roll;
    }

    internal static class PopAnimationStyle
    {
        internal static float Duration(int motion) { return motion==3 ? 0.78f : PopParticleStyle.Duration; }
        internal static int ParticleCount(int requested,int motion) { return Math.Max(0,Math.Min(motion==3 ? 12 : 64,requested)); }
        internal static float ParticleSize(int motion) { return motion==3 ? 0.65f : 1; }
        internal static float ParticleSpread(int motion) { return motion==3 ? 0.48f : 1; }
        internal static float ParticleTime(float elapsed,int motion)
        {
            float duration=Duration(motion);
            return Time(elapsed,duration)/duration*PopParticleStyle.Duration;
        }
        internal static PopIconPose Evaluate(float elapsed,int motion)
        {
            float duration=Duration(motion);
            elapsed=Time(elapsed,duration);
            float progress=elapsed/duration;
            if(motion==3)
            {
                float enter=Smooth(elapsed/0.11f),exit=1-Smooth((elapsed-0.48f)/0.30f);
                return new PopIconPose {
                    X=0.06f,Y=-0.035f+0.02f*enter,Z=0,
                    Scale=0.72f*enter*exit,Pitch=0,Yaw=8*(1-enter),Roll=-4*(1-enter)
                };
            }
            float scale=Smooth(elapsed/0.18f)*(1-Smooth((progress-0.72f)/0.28f))*(1+0.12f*(float)Math.Sin(progress*Math.PI));
            if(motion==2) return new PopIconPose {Scale=scale};
            return new PopIconPose {
                Scale=scale,X=(float)Math.Sin(elapsed*11)*0.045f*(1-progress),Y=(float)Math.Sin(progress*Math.PI)*0.05f,
                Yaw=motion==1 ? elapsed*220 : (float)Math.Sin(elapsed*16)*32*(1-progress),
                Roll=motion==1 ? 0 : (float)Math.Sin(elapsed*12)*22*(1-progress)
            };
        }
        private static float Smooth(float value) { value=Math.Max(0,Math.Min(1,value)); return value*value*(3-2*value); }
        private static float Time(float elapsed,float duration)
        {
            if(float.IsNaN(elapsed) || elapsed<0) return 0;
            return Math.Min(duration,elapsed);
        }
    }
}
