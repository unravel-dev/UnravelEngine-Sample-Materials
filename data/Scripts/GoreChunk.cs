using Unravel.Core;

/// <summary>
/// A piece thrown off the training dummy (GoreDummy): a chunk of flesh, a length of guts or a part cut off. Where it
/// hits something while it flies, it leaves a splatter of blood, attached to what it hit when that can move.
/// </summary>
[ScriptSourceFile]
public class GoreChunk : ScriptComponent
{
    /// <summary>The Decal material of the splatter a hit leaves.</summary>
    public Material splatter;
    /// <summary>The smallest and largest splatter, in metres.</summary>
    public Vector2 sizeRange = new Vector2(0.1f, 0.24f);
    /// <summary>The most splatters one piece leaves.</summary>
    public int maxSplatters = 3;
    /// <summary>A hit slower than this, in metres per second, leaves none.</summary>
    public float minSpeed = 0.6f;
    /// <summary>Seconds a splatter stays before it fades out.</summary>
    public float lifetime = 30.0f;

    private int splatters;
    private float lastSplatter = -1.0f;

    public override void OnCollisionEnter(Collision collision)
    {
        if (splatter == null || splatters >= maxSplatters || Time.time - lastSplatter < 0.2f)
        {
            return;
        }
        if (collision.contacts == null || collision.contacts.Length == 0)
        {
            return;
        }
        var physics = owner.GetComponent<PhysicsComponent>();
        if (physics == null || physics.bodyType != RigidbodyType.Dynamic)
        {
            return;
        }
        float speed = physics.velocity.magnitude;
        if (speed < minSpeed)
        {
            return;
        }
        var contact = collision.contacts[0];
        // The normal out of the surface it hit: toward this piece.
        Vector3 normal = contact.normal.normalized;
        if (Vector3.Dot(normal, transform.position - contact.point) < 0.0f)
        {
            normal = -normal;
        }
        Entity attachTo = Entity.Invalid;
        var other = collision.entity;
        var otherPhysics = other.IsValid() ? other.GetComponent<PhysicsComponent>() : null;
        if (otherPhysics != null && otherPhysics.bodyType != RigidbodyType.Static)
        {
            attachTo = other;
        }
        float size = Random.Range(sizeRange.x, sizeRange.y) * Mathf.Clamp(speed * 0.35f, 0.6f, 1.3f);
        Decals.Spawn(splatter, contact.point, normal, new Vector2(size, size), Random.Range(0.0f, 360.0f), lifetime, 3.0f, attachTo);
        splatters++;
        lastSplatter = Time.time;
    }
}
