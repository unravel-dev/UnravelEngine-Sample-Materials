using System;
using Unravel.Core;

/// <summary>
/// Throws blood splatters at a patch of wall while the game runs: Decals.Spawn places each one, with a random size and
/// turn, and removes it after its lifetime. The splatter material dries by the decal's age, so the wall shows fresh and
/// old blood side by side.
/// </summary>
[ScriptSourceFile]
public class BloodSpawner : ScriptComponent
{
    /// <summary>The Decal material each splatter draws.</summary>
    public Material splatter;
    /// <summary>The middle of the patch of wall, on its surface.</summary>
    public Vector3 wallPoint;
    /// <summary>The wall's normal, pointing out of it.</summary>
    public Vector3 wallNormal = new Vector3(1.0f, 0.0f, 0.0f);
    /// <summary>Width and height of the patch, in metres.</summary>
    public Vector2 area = new Vector2(1.8f, 1.4f);
    /// <summary>Seconds between two splatters.</summary>
    public float interval = 0.9f;
    /// <summary>Seconds a splatter stays before it fades out.</summary>
    public float lifetime = 7.0f;

    private float timer;

    public override void OnUpdate()
    {
        timer += Time.deltaTime;
        if (timer < interval)
        {
            return;
        }
        timer -= interval;
        var normal = wallNormal.normalized;
        var across = Vector3.Cross(Vector3.up, normal).normalized;
        var point = wallPoint + across * Random.Range(-0.5f, 0.5f) * area.x + Vector3.up * Random.Range(-0.5f, 0.5f) * area.y;
        var size = Random.Range(0.35f, 0.8f);
        Decals.Spawn(splatter, point, normal, new Vector2(size, size), Random.Range(0.0f, 360.0f), lifetime, 1.5f);
    }
}
