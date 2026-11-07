using System;
using System.Collections.Generic;

namespace MaceMod.Shared
{
    // BCL-only delegates cross assembly boundaries. Totem and Mace may each
    // contain these sources, but one process owns one transport and hit loop.
    internal sealed class MaceClient : IDisposable
    {
        internal const string Slot="TotemMace.CooperativeRuntime.v2";
        private readonly IDictionary<string,object> actions;
        private bool disposed;
        private MaceClient(IDictionary<string,object> actions) { this.actions=actions; }
        internal static MaceClient Connect(string owner,Func<bool> allowed,Func<bool> tryTotem,Action<string> notice,Action commitTotem=null,Func<bool> authorizeEquip=null,Func<byte[],byte[]> signState=null)
        {
            IDictionary<string,object> bus=AppDomain.CurrentDomain.GetData(Slot) as IDictionary<string,object>;
            if(bus==null)
            {
                bus=new MaceRuntime().Bus;
                AppDomain.CurrentDomain.SetData(Slot,bus);
            }
            var attach=(Func<string,Func<bool>,Func<bool>,Action<string>,Action,Func<bool>,Func<byte[],byte[]>,IDictionary<string,object>>)bus["attach"];
            return new MaceClient(attach(owner,allowed,tryTotem,notice,commitTotem,authorizeEquip,signState));
        }
        internal bool Equipped { get { return !disposed && ((Func<bool>)actions["equipped"])(); } }
        internal string Status { get { return disposed ? "Mace stopped" : ((Func<string>)actions["status"])(); } }
        internal void Configure(bool equipped,int density,int knockback,int windBurst,float scale,bool particles,bool sound,bool glint,bool receiveHits,float power)
        {
            if(!disposed) ((Action<bool,int,int,int,float,bool,bool,bool,bool,float>)actions["configure"])(equipped,density,knockback,windBurst,scale,particles,sound,glint,receiveHits,power);
        }
        internal void Tick() { if(!disposed) ((Action)actions["tick"])(); }
        internal void Toggle() { if(!disposed) ((Action)actions["toggle"])(); }
        internal void Suspend(bool suspended) { if(!disposed) ((Action<bool>)actions["suspend"])(suspended); }
        public void Dispose() { if(disposed) return; disposed=true; ((Action)actions["detach"])(); }
    }
}
