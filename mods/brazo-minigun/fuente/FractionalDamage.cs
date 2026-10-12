using System;
using System.Collections.Generic;

namespace SanabiMinigun;

// SANABI accepts integer HP damage. Accumulate tiny impacts per enemy, never
// round each bullet up to one HP. Expire partial damage after six idle seconds.
public sealed class FractionalDamage
{
    readonly Dictionary<int,(double amount,float time)> totals=new();
    public int Hit(int enemy,float now,float damage)
    {
        if(float.IsNaN(damage)||float.IsInfinity(damage)||damage<=0)return 0;
        totals.TryGetValue(enemy,out var previous);
        var total=(now-previous.time<=6?previous.amount:0)+Math.Min(.1,damage);
        var whole=(int)Math.Floor(total+1e-6);
        if(totals.Count>512)totals.Clear();
        totals[enemy]=(Math.Max(0,total-whole),now);
        return whole;
    }
    public void Clear()=>totals.Clear();
}
