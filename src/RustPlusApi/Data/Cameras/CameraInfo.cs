namespace RustPlusApi.Data.Cameras;

/// <summary>
/// Describes a subscribed camera (the reply to <c>SubscribeToCameraAsync</c>),
/// mapped from <c>AppCameraInfo</c>.
/// </summary>
public sealed record CameraInfo
{
    public int Width { get; init; }
    public int Height { get; init; }
    public float NearPlane { get; init; }
    public float FarPlane { get; init; }
    public CameraControlFlags ControlFlags { get; init; }
}
