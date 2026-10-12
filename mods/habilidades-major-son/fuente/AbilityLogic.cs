using System;

namespace CharacterAbilities;

// Independent of Unity so resource/cooldown rules can be verified in isolation.
public sealed class AbilityLogic
{
    public float Energy { get; private set; } = 100;
    public int Combo { get; private set; }
    public bool AirJumpUsed { get; private set; }
    float meleeAt, shotAt, dashAt, burstAt, comboUntil;
    public void Reset() { Energy=100; Combo=0; AirJumpUsed=false; meleeAt=shotAt=dashAt=burstAt=comboUntil=0; }
    public void Tick(float delta, bool onGround, bool canAct)
    { if(!canAct)return; Energy=Math.Min(100,Energy+Math.Min(.1f,Math.Max(0,delta))*18); if(onGround)AirJumpUsed=false; }
    public int Melee(float now)
    { if(now<meleeAt)return 0; if(now>comboUntil)Combo=0; Combo=Combo%3+1;comboUntil=now+.85f;meleeAt=now+.22f;return Combo; }
    public bool Shot(float now) => Spend(now,ref shotAt,.3f,10);
    public bool Dash(float now) => Spend(now,ref dashAt,.65f,18);
    public bool Burst(float now) => Spend(now,ref burstAt,2f,45);
    bool Spend(float now,ref float next,float cooldown,float cost)
    { if(now<next || Energy<cost)return false;Energy-=cost;next=now+cooldown;return true; }
    public bool AirJump(bool onGround) { if(onGround || AirJumpUsed)return false;AirJumpUsed=true;return true; }
}
