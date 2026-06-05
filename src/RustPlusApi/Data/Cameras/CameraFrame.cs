namespace RustPlusApi.Data.Cameras;

/// <summary>
/// A single camera frame streamed from the server (mapped from the <c>AppCameraRays</c>
/// broadcast). Carries the run-length-encoded depth/entity <see cref="RayData"/> plus the
/// entities in view; turning it into an image is the job of the separate
/// <c>RustPlusApi.Camera</c> rendering package.
/// </summary>
public record CameraFrame
{
    public float VerticalFov { get; init; }
    public int SampleOffset { get; init; }
    public byte[] RayData { get; init; } = [];
    public float Distance { get; init; }
    public IEnumerable<CameraEntity> Entities { get; init; } = [];
}
