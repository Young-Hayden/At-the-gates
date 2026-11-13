using UnityEngine;

/// <summary>
/// Slides a cloud across the sky and wraps it back to the other side. Purely cosmetic —
/// clouds sit on the top sorting layer and never touch <see cref="MapGrid"/>.
/// </summary>
public class CloudDrift : MonoBehaviour
{
    public float speed = 0.35f;
    public float minX = -40f;
    public float maxX = 40f;

    void Update()
    {
        var p = transform.position;
        p.x += speed * Time.deltaTime;
        if (speed > 0f && p.x > maxX) p.x = minX;
        else if (speed < 0f && p.x < minX) p.x = maxX;
        transform.position = p;
    }
}
