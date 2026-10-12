using System;
namespace SanabiCharacterCore;
public static class HealthBridge
{
    public static int Resolve(int native,int world,int previousNative,int previousWorld)
    {
        var nativeChanged=native!=previousNative;var worldChanged=world!=previousWorld;
        if(nativeChanged && worldChanged)return Math.Min(native,world);
        return nativeChanged?native:worldChanged?world:native;
    }
}
