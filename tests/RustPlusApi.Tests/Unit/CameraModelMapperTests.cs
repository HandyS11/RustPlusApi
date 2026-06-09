using RustPlusApi.Extensions;
using RustPlusContracts;
using Xunit;
using ProtoVector3 = RustPlusContracts.Vector3;

namespace RustPlusApi.Tests.Unit;

/// <summary>Covers the camera mappers' null/presence branches not hit by the broadcast fixture.</summary>
public class CameraModelMapperTests
{
    [Fact]
    public void ToVector3_Null_ReturnsZeroVector()
    {
        ProtoVector3? v = null;
        var result = v.ToVector3();
        Assert.Equal(0, result.X);
        Assert.Equal(0, result.Y);
        Assert.Equal(0, result.Z);
    }

    [Fact]
    public void ToCameraFrame_NullRayData_BecomesEmpty_AndNullVectorsStayNull()
    {
        var rays = new AppCameraRays { VerticalFov = 90f, SampleOffset = 1, Distance = 50f, RayData = null };

        var frame = rays.ToCameraFrame();

        Assert.Empty(frame.RayData);
        Assert.Null(frame.TimeOfDay);
        Assert.Null(frame.CameraPosition);
        Assert.Null(frame.CameraRotation);
        Assert.Empty(frame.Entities);
    }

    [Fact]
    public void ToCameraFrame_WithTimeOfDayAndCameraVectors_MapsThem()
    {
        var rays = new AppCameraRays
        {
            VerticalFov = 90f,
            RayData = [1],
            TimeOfDay = 0.5f,
            CameraPosition = new ProtoVector3 { X = 1, Y = 2, Z = 3 },
            CameraRotation = new ProtoVector3 { X = 4, Y = 5, Z = 6 }
        };

        var frame = rays.ToCameraFrame();

        Assert.Equal(0.5f, frame.TimeOfDay);
        Assert.Equal(1, frame.CameraPosition!.X);
        Assert.Equal(6, frame.CameraRotation!.Z);
    }

    [Fact]
    public void ToCameraRaysEvent_NullRayData_BecomesEmpty_AndNullVectorsStayNull()
    {
        var rays = new AppCameraRays { VerticalFov = 90f, SampleOffset = 1, Distance = 50f, RayData = null };

        var frame = rays.ToCameraRaysEvent();

        Assert.Empty(frame.RayData);
        Assert.Null(frame.TimeOfDay);
        Assert.Null(frame.CameraPosition);
        Assert.Null(frame.CameraRotation);
    }

    [Fact]
    public void ToCameraRaysEvent_WithTimeOfDayAndCameraVectors_MapsThem()
    {
        var rays = new AppCameraRays
        {
            VerticalFov = 90f,
            RayData = [1],
            TimeOfDay = 0.75f,
            CameraPosition = new ProtoVector3 { X = 10, Y = 20, Z = 30 },
            CameraRotation = new ProtoVector3 { X = 1, Y = 2, Z = 3 }
        };

        var frame = rays.ToCameraRaysEvent();

        Assert.Equal(0.75f, frame.TimeOfDay);
        Assert.Equal(10, frame.CameraPosition!.X);
        Assert.Equal(3, frame.CameraRotation!.Z);
    }
}
