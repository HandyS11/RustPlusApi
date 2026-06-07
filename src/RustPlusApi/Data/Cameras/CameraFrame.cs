namespace RustPlusApi.Data.Cameras;

/// <summary>
/// A single camera frame streamed from the server (mapped from the <c>AppCameraRays</c>
/// broadcast). Carries the run-length-encoded depth/entity <see cref="RayData"/> plus the
/// entities in view; turning it into an image is the job of the separate
/// <c>RustPlusApi.Camera</c> rendering package.
/// </summary>
public record CameraFrame
{
    /// <summary>Vertical field-of-view of the camera, in degrees.</summary>
    public float VerticalFov { get; init; }

    /// <summary>Index into the shuffled sample-position buffer where this frame's rays begin.</summary>
    public int SampleOffset { get; init; }

    /// <summary>Run-length-encoded ray data for the frame.</summary>
    public byte[] RayData { get; init; } = [];

    /// <summary>Maximum ray cast distance used when encoding this frame.</summary>
    public float Distance { get; init; }

    /// <summary>Entities visible in this frame.</summary>
    public IEnumerable<CameraEntity> Entities { get; init; } = [];
}
