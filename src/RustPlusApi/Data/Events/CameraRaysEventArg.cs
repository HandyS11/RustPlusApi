using RustPlusApi.Data.Cameras;

namespace RustPlusApi.Data.Events;

/// <summary>
/// Event argument raised when a camera rays broadcast is received.
/// Inherits all <see cref="CameraFrame"/> fields so callers can pass it directly to
/// <c>CameraRenderer.AddRays</c>.
/// </summary>
public sealed record CameraRaysEventArg : CameraFrame;
