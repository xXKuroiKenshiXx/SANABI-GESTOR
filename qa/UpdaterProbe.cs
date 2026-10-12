// Compiled only by the isolated self-update verification, never shipped.
class Probe
{
    static void Main() { System.IO.File.WriteAllText(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory,"probe-launched"),"ok"); }
}
