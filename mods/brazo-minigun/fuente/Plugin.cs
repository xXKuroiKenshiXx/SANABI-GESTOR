using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.IL2CPP;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnhollowerBaseLib;
using Object = UnityEngine.Object;

namespace SanabiMinigun;

[BepInPlugin("kuro.sanabi.minigun", "SANABI · Brazo minigun", "0.2.0")]
[BepInProcess("SNB.exe")]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource Logger = null!;
    internal static ConfigEntry<float> Rate = null!;
    internal static ConfigEntry<float> Damage = null!, BulletSpeed = null!;
    internal static ConfigEntry<float> Range = null!;
    public override void Load()
    {
        Logger = Log;
        Rate = Config.Bind("Minigun", "ShotsPerSecond", 12f, new ConfigDescription("Cadencia: 1 a 30 disparos por segundo.", new AcceptableValueRange<float>(1,30)));
        Damage = Config.Bind("Minigun", "DamagePerBullet", .04f, new ConfigDescription("Daño fraccionario por impacto: 0.04 requiere 25 impactos para quitar 1 HP. El ajuste antiguo Damage ya no se usa.",new AcceptableValueRange<float>(.005f,.1f)));
        Range = Config.Bind("Minigun", "Range", 18f, new ConfigDescription("Alcance en unidades del juego.",new AcceptableValueRange<float>(2,30)));
        BulletSpeed = Config.Bind("Minigun", "BulletSpeed", 36f, new ConfigDescription("Velocidad de los proyectiles visibles.",new AcceptableValueRange<float>(8,70)));
        AddComponent<MinigunBehaviour>();
        Log.LogInfo("Minigun experimental: 2 alterna gancho/arma; clic izquierdo dispara; soltá el clic después de alternar.");
    }
}

public sealed class MinigunBehaviour : MonoBehaviour
{
    public MinigunBehaviour(IntPtr ptr) : base(ptr) { }
    readonly GunLogic logic = new();
    readonly FractionalDamage damageLedger = new();
    readonly List<Bullet> bullets = new();
    sealed class Bullet
    {
        public GameObject Visual=null!;
        public Vector2 Position, Direction;
        public float Remaining;
    }
    Player? actor;
    bool originalHook, originalArm;
    GameObject? rig;
    SpriteRenderer? gun, flash, tracer;
    Sprite? bulletSprite;
    Sprite[] frames = Array.Empty<Sprite>();
    AudioSource? audio;
    AudioClip? shotSound;
    float tracerUntil, failedUntil;
    bool loggedFailure;

    public void Update()
    {
        if (Time.unscaledTime < failedUntil) return;
        try
        {
            var current = PlayerBase.Instance?.TryCast<Player>();
            if (current == null || actor == null || current.Pointer != actor.Pointer)
            {
                Restore(); actor = current; logic.Reset();damageLedger.Clear();
                if (actor == null) return;
            }
            var canAct = Time.timeScale > 0 && !actor.IsDead && !actor.IsIgnoreAllInput && !actor.IsControlClone;
            if (canAct && Input.GetKeyDown(KeyCode.Alpha2))
            {
                if (!logic.Equipped && !actor.HookActive) return; // Respect story unlocks and disabled abilities.
                if (!logic.Equipped)
                {
                    originalHook = actor.HookActive; originalArm = actor.armRenderer != null && actor.armRenderer.enabled;
                    actor.ResetHook(); CreateRig();
                }
                logic.Toggle(Time.time);
                if (!logic.Equipped) RestoreVisuals();
            }
            if (!logic.Equipped) { logic.Tick(Time.time,Time.deltaTime,false,canAct,Plugin.Rate.Value); return; }
            if (actor.IsDead) { Restore(); logic.Reset(); return; }
            actor.SetActiveHook(false);
            if (actor.armRenderer != null) actor.armRenderer.enabled = false;
            if (rig == null) CreateRig();
            rig!.SetActive(canAct);
            if (!canAct) { ClearBullets();logic.Tick(Time.time,0,false,false,Plugin.Rate.Value); return; }
            var visualOrigin=actor.bodyRenderer!=null ? actor.bodyRenderer.transform.position : actor.transform.position;
            var origin = (Vector2)visualOrigin + new Vector2(actor.IsFacingLeft ? -.22f : .22f,.15f);
            var cam = Camera.main;
            if (cam == null) return;
            var mouse = Input.mousePosition; mouse.z = Math.Abs(cam.transform.position.z-actor.transform.position.z);
            var aim = (Vector2)cam.ScreenToWorldPoint(mouse)-origin;
            if (aim.sqrMagnitude < .001f) aim = actor.IsFacingLeft ? Vector2.left : Vector2.right;
            aim.Normalize(); var angle = Mathf.Atan2(aim.y,aim.x)*57.29578f;
            rig.transform.position = new Vector3(origin.x,origin.y,visualOrigin.z-.02f);
            rig.transform.rotation = Quaternion.Euler(0,0,angle);
            gun!.sprite = frames[logic.Shots%frames.Length];
            var order = actor.bodyRenderer != null ? actor.bodyRenderer.sortingOrder : 0;
            if (actor.bodyRenderer != null) { gun.sortingLayerID=actor.bodyRenderer.sortingLayerID; flash!.sortingLayerID=gun.sortingLayerID; tracer!.sortingLayerID=gun.sortingLayerID; }
            gun.sortingOrder=order+3; flash!.sortingOrder=order+4; tracer!.sortingOrder=order+2;
            if (logic.Tick(Time.time,Time.deltaTime,Input.GetMouseButton(0),canAct,Plugin.Rate.Value)) Fire(origin,aim);
            UpdateBullets();
            flash.enabled = tracerUntil > Time.time; tracer.enabled = false;
        }
        catch (Exception e)
        {
            // Restore the hook if a game update invalidates a binding; avoid breaking the player controller.
            try { Restore(); } catch { }
            logic.Reset(); failedUntil = Time.unscaledTime+5;
            if (!loggedFailure) { Plugin.Logger.LogError("Minigun suspendida; gancho restaurado. " + e); loggedFailure=true; }
        }
    }

    void Fire(Vector2 origin, Vector2 aim)
    {
        if(bullets.Count>=64)return;
        var visual=new GameObject("SANABI_Minigun_Bullet");
        var sprite=visual.AddComponent<SpriteRenderer>();sprite.sprite=bulletSprite;
        sprite.color=new Color(1,.85f,.3f,.95f);sprite.sortingLayerID=gun!.sortingLayerID;sprite.sortingOrder=gun.sortingOrder+1;
        visual.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(aim.y,aim.x)*57.29578f);
        visual.transform.localScale=new Vector3(.22f,.055f,1);
        visual.transform.position=new Vector3(origin.x,origin.y,rig!.transform.position.z);
        bullets.Add(new Bullet { Visual=visual,Position=origin,Direction=aim,Remaining=Plugin.Range.Value });
        tracerUntil=Time.time+.045f;
        if (audio!=null && shotSound!=null) audio.PlayOneShot(shotSound,.25f);
    }

    void UpdateBullets()
    {
        for(var i=bullets.Count-1;i>=0;i--)
        {
            var bullet=bullets[i];if(bullet.Visual==null) { bullets.RemoveAt(i);continue; }
            var step=Math.Min(bullet.Remaining,Plugin.BulletSpeed.Value*Math.Min(.1f,Time.deltaTime));
            var hits=Physics2D.RaycastAll(bullet.Position,bullet.Direction,step);
            var ordered=new List<RaycastHit2D>();foreach(var hit in hits)ordered.Add(hit);ordered.Sort((a,b)=>a.distance.CompareTo(b.distance));
            var consumed=false;
            foreach(var hit in ordered)
            {
                var col=hit.collider;if(col==null || col.transform.IsChildOf(actor!.transform))continue;
                var box=col.GetComponentInParent<EnemyHitBox>();
                if(box!=null)
                {
                    if(box.enemy!=null && !box.enemy.IsDead && !box.IsInvincible() && !box.IsProjectileInvincible())
                    {
                        var damage=damageLedger.Hit(box.enemy.GetInstanceID(),Time.time,Plugin.Damage.Value);
                        if(damage>0)box.TakeDamage(AttackType.PlayerBullet,hit.point,damage,out var blocked);
                    }
                    consumed=true;break;
                }
                if(!col.isTrigger) { consumed=true;break; }
            }
            bullet.Position+=bullet.Direction*step;bullet.Remaining-=step;
            if(consumed || bullet.Remaining<=0) { Object.Destroy(bullet.Visual);bullets.RemoveAt(i); }
            else bullet.Visual.transform.position=new Vector3(bullet.Position.x,bullet.Position.y,rig!.transform.position.z);
        }
    }
    void ClearBullets() { foreach(var bullet in bullets)if(bullet.Visual!=null)Object.Destroy(bullet.Visual);bullets.Clear(); }

    void CreateRig()
    {
        if (rig!=null) return;
        rig=new GameObject("SANABI_Minigun");
        var go=new GameObject("Barrels");go.transform.SetParent(rig.transform,false);gun=go.AddComponent<SpriteRenderer>();
        if (frames.Length==0) frames=GunArt.CreateFrames();
        gun.sprite=frames[0];
        var spark=new GameObject("Muzzle");spark.transform.SetParent(rig.transform,false);spark.transform.localPosition=new Vector3(.9f,0,0);flash=spark.AddComponent<SpriteRenderer>();
        flash.sprite=GunArt.CreateFlash();flash.enabled=false;
        var line=new GameObject("Tracer");line.transform.SetParent(rig.transform,false);tracer=line.AddComponent<SpriteRenderer>();
        bulletSprite=GunArt.CreatePixel();tracer.sprite=bulletSprite;tracer.color=new Color(1,.85f,.3f,.85f);tracer.enabled=false;
        audio=rig.AddComponent<AudioSource>();audio.spatialBlend=0;
        if (shotSound==null)
        {
            const int samples=2205; var pcm=new float[samples];var random=new System.Random(2099);
            for(var i=0;i<samples;i++) pcm[i]=(float)((random.NextDouble()*2-1)*Math.Exp(-i/320.0)*.6);
            shotSound=AudioClip.Create("MinigunShot",samples,1,44100,false);shotSound.SetData(new Il2CppStructArray<float>(pcm),0);
        }
    }
    void RestoreVisuals()
    {
        ClearBullets();
        if (actor!=null) { actor.SetActiveHook(originalHook); if(actor.armRenderer!=null) actor.armRenderer.enabled=originalArm; }
        if(rig!=null) rig.SetActive(false);
    }
    void Restore()
    {
        if(logic.Equipped) RestoreVisuals();
        if(rig!=null) Object.Destroy(rig);
        rig=null;gun=null;flash=null;tracer=null;audio=null;
    }
    public void OnDestroy() { Restore(); }
    public void OnGUI()
    {
        if (!logic.Equipped || rig == null || !rig.activeSelf) return;
        GUI.Label(new Rect(20,Screen.height-65,450,50),
            "MINIGUN  ·  [2] Gancho  ·  " + (logic.Overheated ? "ENFRIANDO" : "CALOR " + (int)(logic.Heat*100) + "%"));
    }
}
