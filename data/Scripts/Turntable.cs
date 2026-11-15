using System;
using Unravel.Core;

/// <summary>
/// Turns its entity slowly about the world's up axis, so an exhibit shows every side of its material to the lights.
/// </summary>
[ScriptSourceFile]
public class Turntable : ScriptComponent
{
    /// <summary>Degrees per second; negative turns clockwise seen from above.</summary>
    public float degreesPerSecond = 12.0f;

    public override void OnUpdate()
    {
        transform.RotateAxis(degreesPerSecond * Time.deltaTime, Vector3.up);
    }
}
