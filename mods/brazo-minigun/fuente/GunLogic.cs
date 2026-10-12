using System;

namespace SanabiMinigun;

// Timing uses game time: pausing never accumulates a burst of deferred shots.
public sealed class GunLogic
{
    public bool Equipped { get; private set; }
    public float Heat { get; private set; }
    public bool Overheated { get; private set; }
    public int Shots { get; private set; }
    float nextShot;
    bool releaseRequired;
    public void Toggle(float now)
    { Equipped = !Equipped; releaseRequired = true; nextShot = now + .15f; }
    public void Reset()
    { Equipped = false; Heat = 0; Overheated = false; releaseRequired = true; Shots = 0; nextShot = 0; }
    public bool Tick(float now, float delta, bool trigger, bool canAct, float shotsPerSecond)
    {
        delta = Math.Max(0, Math.Min(delta, .1f));
        if (!canAct) { releaseRequired = true; return false; }
        if (!trigger) releaseRequired = false;
        Heat = Math.Max(0, Heat - delta * (trigger && Equipped && !Overheated ? .025f : .32f));
        if (Overheated && Heat <= .25f) Overheated = false;
        if (!Equipped || releaseRequired || !trigger || Overheated || now < nextShot) return false;
        nextShot = now + 1f / Math.Max(1, Math.Min(shotsPerSecond, 30));
        Heat = Math.Min(1, Heat + .075f); Overheated = Heat >= .999f; Shots++;
        return true;
    }
}
