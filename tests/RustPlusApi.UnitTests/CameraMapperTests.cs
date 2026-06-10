using RustPlusApi.Data.Cameras;
using RustPlusApi.Extensions;
using RustPlusApi.MockServer;
using Xunit;

namespace RustPlusApi.UnitTests;

/// <summary>Guards the camera mappers and the control-flag/entity-type enum mapping.</summary>
public class CameraMapperTests
{
    [Fact]
    public void ToCameraInfo_MapsScalarsAndControlFlags()
    {
        var model = MockResponses.SampleCameraInfo().ToCameraInfo();

        Assert.Equal(640, model.Width);
        Assert.Equal(480, model.Height);
        Assert.Equal(0.1f, model.NearPlane);
        Assert.Equal(
            CameraControlFlags.Movement | CameraControlFlags.Mouse | CameraControlFlags.Fire,
            model.ControlFlags);
    }

    [Fact]
    public void ToCameraRaysEvent_MapsFrameAndEntities()
    {
        var rays = MockResponses.CameraRaysBroadcast().CameraRays;

        var frame = rays.ToCameraRaysEvent();

        Assert.Equal(65f, frame.VerticalFov);
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, frame.RayData);
        var entity = Assert.Single(frame.Entities);
        Assert.Equal(99u, entity.EntityId);
        Assert.Equal(CameraEntityType.Player, entity.Type);
        Assert.Equal("Survivor", entity.Name);
        Assert.Equal(90f, entity.Rotation.Y);
    }
}
