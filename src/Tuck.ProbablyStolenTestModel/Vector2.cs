// Signature-only substitute for the placement overload. Finite screen coordinates are unsupported.
namespace UnityEngine;

public struct Vector2(float x, float y)
{
    public float x = x;
    public float y = y;
    public static Vector2 negativeInfinity => new(float.NegativeInfinity, float.NegativeInfinity);
}
