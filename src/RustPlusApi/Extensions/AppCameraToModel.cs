using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;

using AppCameraInfo = RustPlusContracts.AppCameraInfo;
using AppCameraRays = RustPlusContracts.AppCameraRays;
using ProtoVector3 = RustPlusContracts.Vector3;
using ProtoCameraEntity = RustPlusContracts.AppCameraRays.Entity;
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions;

public static class AppCameraToModel
{
    public static CameraInfo ToCameraInfo(this AppCameraInfo appCameraInfo)
    {
        return new CameraInfo
        {
            Width = appCameraInfo.Width,
            Height = appCameraInfo.Height,
            NearPlane = appCameraInfo.NearPlane,
            FarPlane = appCameraInfo.FarPlane,
            ControlFlags = (CameraControlFlags)appCameraInfo.ControlFlags
        };
    }

    public static CameraFrame ToCameraFrame(this AppCameraRays appCameraRays)
    {
        return new CameraFrame
        {
            VerticalFov = appCameraRays.VerticalFov,
            SampleOffset = appCameraRays.SampleOffset,
            RayData = appCameraRays.RayData ?? [],
            Distance = appCameraRays.Distance,
            Entities = appCameraRays.Entities.ToCameraEntities()
        };
    }

    public static CameraRaysEventArg ToCameraRaysEvent(this AppCameraRays appCameraRays)
    {
        return new CameraRaysEventArg
        {
            VerticalFov = appCameraRays.VerticalFov,
            SampleOffset = appCameraRays.SampleOffset,
            RayData = appCameraRays.RayData ?? [],
            Distance = appCameraRays.Distance,
            Entities = appCameraRays.Entities.ToCameraEntities()
        };
    }

    public static CameraEntity ToCameraEntity(this ProtoCameraEntity entity)
    {
        return new CameraEntity
        {
            EntityId = entity.EntityId,
            Type = (CameraEntityType)entity.Type,
            Position = entity.Position.ToVector3(),
            Rotation = entity.Rotation.ToVector3(),
            Size = entity.Size.ToVector3(),
            Name = entity.Name
        };
    }

    public static IEnumerable<CameraEntity> ToCameraEntities(this IEnumerable<ProtoCameraEntity> entities)
    {
        return entities.Select(ToCameraEntity);
    }

    public static Vector3 ToVector3(this ProtoVector3? vector)
    {
        if (vector is null) return new Vector3();
        return new Vector3 { X = vector.X, Y = vector.Y, Z = vector.Z };
    }
}
