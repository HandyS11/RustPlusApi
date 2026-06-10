namespace RustPlusApi.Data.Cameras;

/// <summary>
/// Describes a subscribed camera (the reply to <c>SubscribeToCameraAsync</c>),
/// mapped from <c>AppCameraInfo</c>.
/// </summary>
public sealed record CameraInfo
{
    /// <summary>Render width of the camera, in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Render height of the camera, in pixels.</summary>
    public int Height { get; init; }

    /// <summary>Near clip-plane distance.</summary>
    public float NearPlane { get; init; }

    /// <summary>Far clip-plane distance (maximum ray cast range).</summary>
    public float FarPlane { get; init; }

    /// <summary>Bitmask of inputs the camera accepts.</summary>
    public CameraControlFlags ControlFlags { get; init; }
}
