using System;
namespace PreLuna.Captain.Tasks {
    internal enum GuardDutyKind { Room, Cover, Entrance }
    internal static class GuardPolicy {
        internal static float Radius(float value)=>float.IsFinite(value)&&value is 2 or 3 or 5?value:3;
        internal static bool InBound(bool sameRoom,float horizontal,float vertical,float radius)
            =>sameRoom&&float.IsFinite(horizontal)&&float.IsFinite(vertical)&&horizontal<=radius&&Math.Abs(vertical)<=1.25f;
        // Only rank already visible, awake native candidates. No sleeping-enemy discovery.
        internal static float Score(GuardDutyKind kind,bool sameRoom,bool victimMatch,bool victimInBound,float distance,float cosine){
            if(!float.IsFinite(distance)||distance<0||distance>30)return float.MinValue;
            if(kind==GuardDutyKind.Room)return sameRoom?200-distance:float.MinValue;
            if(kind==GuardDutyKind.Cover)return victimMatch&&victimInBound?1000-distance*4:float.MinValue;
            return float.IsFinite(cosine)&&cosine>=.45f?300-distance*2:float.MinValue;
        }
        internal static bool Hold(bool previousValid,float elapsed,float oldScore,float bestScore)
            =>previousValid&&(elapsed<1||bestScore-oldScore<20);
    }
}