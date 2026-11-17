using System;
using System.Collections.Generic;
using System.Globalization;
using Unravel.Core;

/// <summary>
/// The Material Gallery's camera: it glides between the gallery's stops, one per bay, and shows each bay's title
/// and text on the HUD. Keys: 1 to 9 go to a stop, Left / Right (or the HUD's arrows) to the one before or after,
/// Space starts or stops the tour that moves on by itself, F toggles free flight. Free flight: hold the right mouse
/// button to look, WASD to move, Q / E to sink and rise, Shift to hurry, the wheel to change the speed.
/// </summary>
[ScriptSourceFile]
public class GalleryTour : ScriptComponent
{
    /// <summary>The stops, one per line: "x y z pitch yaw|Title|Text".</summary>
    public string stops = "";

    /// <summary>Seconds a glide from one stop to the next takes.</summary>
    public float glideSeconds = 2.6f;

    /// <summary>Seconds the tour stays at a stop before it moves on.</summary>
    public float dwellSeconds = 9.0f;

    /// <summary>The tour moves on by itself from the start.</summary>
    public bool autoTour = false;

    /// <summary>Free flight speed in meters per second, and the mouse look's sensitivity.</summary>
    public float flySpeed = 3.0f;
    public float lookSensitivity = 10.0f;

    /// <summary>The name of the entity holding the HUD's UI document.</summary>
    public string hudEntity = "HUD";

    private struct Stop
    {
        public Vector3 position;
        public Quaternion rotation;
        public string title;
        public string text;
    }

    private const float MAX_PITCH = 89.0f;
    private const float MIN_FLY_SPEED = 0.5f;
    private const float MAX_FLY_SPEED = 30.0f;

    private readonly List<Stop> points = new List<Stop>();
    private int current;
    private Vector3 fromPosition;
    private Quaternion fromRotation;
    private float glideTime;
    private bool isGliding;
    private float dwellTime;
    private bool isTouring;
    private bool isFlying;
    private float flyPitch;
    private float flyYaw;

    private UIElement titleElement;
    private UIElement textElement;
    private UIElement indexElement;
    private UIElement tourElement;

    public override void OnStart()
    {
        ParseStops();
        isTouring = autoTour;
        ConnectHud();
        if(points.Count > 0)
        {
            current = 0;
            transform.position = points[0].position;
            transform.rotation = points[0].rotation;
            ShowStop();
        }
    }

    public override void OnUpdate()
    {
        HandleKeys();
        if(isFlying)
        {
            UpdateFlight();
            return;
        }
        if(isGliding)
        {
            UpdateGlide();
        }
        else if(isTouring && points.Count > 1)
        {
            dwellTime += Time.deltaTime;
            if(dwellTime >= dwellSeconds)
            {
                GoTo(current + 1);
            }
        }
    }

    private void HandleKeys()
    {
        for(int digit = 0; digit < 9; ++digit)
        {
            if(Input.IsPressed(KeyCode.Digit1 + digit) && digit < points.Count)
            {
                GoTo(digit);
            }
        }
        // 0 is the tenth bay, as on a keyboard's row of digits.
        if(Input.IsPressed(KeyCode.Digit0) && points.Count > 9)
        {
            GoTo(9);
        }
        if(Input.IsPressed(KeyCode.Right))
        {
            GoTo(current + 1);
        }
        if(Input.IsPressed(KeyCode.Left))
        {
            GoTo(current - 1);
        }
        if(Input.IsPressed(KeyCode.Space))
        {
            ToggleTour();
        }
        if(Input.IsPressed(KeyCode.F))
        {
            ToggleFlight();
        }
    }

    private void ParseStops()
    {
        points.Clear();
        foreach(string entry in stops.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = entry.Split('|');
            string[] values = parts[0].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if(values.Length < 5)
            {
                continue;
            }
            float[] numbers = new float[5];
            for(int i = 0; i < 5; ++i)
            {
                numbers[i] = float.Parse(values[i], CultureInfo.InvariantCulture);
            }
            points.Add(new Stop
            {
                position = new Vector3(numbers[0], numbers[1], numbers[2]),
                rotation = Quaternion.Euler(numbers[3], numbers[4], 0.0f),
                title = parts.Length > 1 ? parts[1].Trim() : "",
                text = parts.Length > 2 ? parts[2].Trim() : "",
            });
        }
    }

    private void GoTo(int index)
    {
        if(points.Count == 0)
        {
            return;
        }
        isFlying = false;
        current = ((index % points.Count) + points.Count) % points.Count;
        fromPosition = transform.position;
        fromRotation = transform.rotation;
        glideTime = 0.0f;
        isGliding = true;
        dwellTime = 0.0f;
        ShowStop();
    }

    private void UpdateGlide()
    {
        glideTime += Time.deltaTime;
        float t = Mathf.Clamp01(glideTime / Mathf.Max(glideSeconds, 0.01f));
        float eased = t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
        Stop target = points[current];
        transform.position = Vector3.Lerp(fromPosition, target.position, eased);
        transform.rotation = Quaternion.Slerp(fromRotation, target.rotation, eased);
        if(t >= 1.0f)
        {
            isGliding = false;
        }
    }

    private void ToggleTour()
    {
        isTouring = !isTouring;
        dwellTime = 0.0f;
        ShowStop();
    }

    private void ToggleFlight()
    {
        isFlying = !isFlying;
        isGliding = false;
        if(!isFlying)
        {
            return;
        }
        Vector3 forward = transform.forward;
        flyYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        flyPitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1.0f, 1.0f)) * Mathf.Rad2Deg;
    }

    private void UpdateFlight()
    {
        float speedAdjust = Input.GetAxis("Mouse ScrollWheel");
        flySpeed = Mathf.Clamp(flySpeed * (1.0f + speedAdjust * 0.1f), MIN_FLY_SPEED, MAX_FLY_SPEED);
        if(Input.IsDown("Mouse Right"))
        {
            flyYaw += Input.GetAxis("Mouse X") * lookSensitivity * Time.deltaTime;
            flyPitch = Mathf.Clamp(flyPitch - Input.GetAxis("Mouse Y") * lookSensitivity * Time.deltaTime, -MAX_PITCH, MAX_PITCH);
        }
        Quaternion rotation = Quaternion.AngleAxis(flyYaw, Vector3.up) * Quaternion.AngleAxis(flyPitch, Vector3.right);
        float rise = (Input.IsDown(KeyCode.E) ? 1.0f : 0.0f) - (Input.IsDown(KeyCode.Q) ? 1.0f : 0.0f);
        Vector3 move = new Vector3(Input.GetAxis("Horizontal"), 0.0f, Input.GetAxis("Vertical"));
        float speed = flySpeed * (Input.IsDown(KeyCode.LShift) ? 3.0f : 1.0f);
        transform.rotation = rotation;
        transform.position = transform.position + (rotation * move + Vector3.up * rise) * (speed * Time.deltaTime);
    }

    private void ConnectHud()
    {
        Entity hud = Scene.FindEntityByName(hudEntity);
        var component = hud.IsValid() ? hud.GetComponent<UIDocumentComponent>() : null;
        UIDocument document = component != null ? component.GetDocument() : null;
        if(document == null)
        {
            Log.Warning("GalleryTour: no HUD document found on " + hudEntity + ".");
            return;
        }
        titleElement = document.GetElementById("bay-title");
        textElement = document.GetElementById("bay-text");
        indexElement = document.GetElementById("bay-index");
        tourElement = document.GetElementById("tour");
        UIElement previous = document.GetElementById("previous");
        UIElement next = document.GetElementById("next");
        if(previous != null)
        {
            previous.RegisterCallback<UIPointerEvent>("click", evt => GoTo(current - 1));
        }
        if(next != null)
        {
            next.RegisterCallback<UIPointerEvent>("click", evt => GoTo(current + 1));
        }
        if(tourElement != null)
        {
            tourElement.RegisterCallback<UIPointerEvent>("click", evt => ToggleTour());
        }
    }

    private void ShowStop()
    {
        if(points.Count == 0)
        {
            return;
        }
        Stop stop = points[current];
        if(titleElement != null)
        {
            titleElement.InnerRml = stop.title;
        }
        if(textElement != null)
        {
            textElement.InnerRml = stop.text;
        }
        if(indexElement != null)
        {
            indexElement.InnerRml = string.Format(CultureInfo.InvariantCulture, "{0} / {1}", current + 1, points.Count);
        }
        if(tourElement != null)
        {
            tourElement.InnerRml = isTouring ? "Pause Tour" : "Play Tour";
        }
    }
}
