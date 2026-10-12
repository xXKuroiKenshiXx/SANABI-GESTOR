using System;
using UnityEngine;
using UnhollowerBaseLib;

namespace SanabiMinigun;

// Original pixel art: rotating barrels, cyan heat sink, leather grip and muzzle flash.
static class GunArt
{
    internal static Sprite[] CreateFrames()
    {
        var frames=new Sprite[4];
        for(var frame=0;frame<4;frame++)
        {
            var pixels=new Color32[32*16];
            void Rect(int x,int y,int w,int h,Color32 c) { for(var j=y;j<y+h;j++) for(var i=x;i<x+w;i++) pixels[j*32+i]=c; }
            var edge=new Color32(16,21,35,255);var dark=new Color32(47,55,77,255);var steel=new Color32(125,151,171,255);var light=new Color32(200,221,228,255);var cyan=new Color32(42,217,225,255);
            Rect(0,4,11,8,edge);Rect(1,5,10,6,dark);Rect(3,3,4,3,edge);Rect(4,3,2,4,new Color32(131,76,55,255));
            Rect(8,3,5,10,edge);Rect(9,4,3,8,steel);Rect(10,6,2,4,cyan);
            Rect(13,4,17,8,edge);Rect(14,5,17,6,dark);
            for(var barrel=0;barrel<3;barrel++) { var y=5+((barrel+frame)%3)*2;Rect(14,y,15,1,steel);Rect(16,y,9,1,light); }
            Rect(26,3,3,10,edge);Rect(27,4,1,8,steel);Rect(29,4,3,8,edge);Rect(30,5,1,6,dark);
            Rect(1,10,6,1,steel);frames[frame]=SpriteFrom(pixels,32,16,new Vector2(.08f,.5f),32);
        }
        return frames;
    }
    internal static Sprite CreateFlash()
    {
        var p=new Color32[12*12];
        for(var y=0;y<12;y++)for(var x=0;x<12;x++)
        {
            var dx=x-5;var dy=y-5;
            if(Math.Abs(dx)+Math.Abs(dy)<6||Math.Abs(dy)<2) p[y*12+x]=new Color32(255,(byte)(Math.Abs(dy)<2?241:157),52,255);
        }
        return SpriteFrom(p,12,12,new Vector2(.15f,.5f),32);
    }
    internal static Sprite CreatePixel() => SpriteFrom(new[]{new Color32(255,255,255,255)},1,1,new Vector2(.5f,.5f),1);
    static Sprite SpriteFrom(Color32[] pixels,int w,int h,Vector2 pivot,float ppu)
    {
        var tex=new Texture2D(w,h,TextureFormat.RGBA32,false);tex.filterMode=FilterMode.Point;
        tex.SetPixels32(new Il2CppStructArray<Color32>(pixels));tex.Apply();
        return Sprite.Create(tex,new Rect(0,0,w,h),pivot,ppu,0,SpriteMeshType.FullRect);
    }
}
