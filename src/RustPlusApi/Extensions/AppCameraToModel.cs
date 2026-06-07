using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;

using AppCameraInfo = RustPlusContracts.AppCameraInfo;
using AppCameraRays = RustPlusContracts.AppCameraRays;
using ProtoVector3 = RustPlusContracts.Vector3;
using ProtoCameraEntity = RustPlusContracts.AppCameraRays.Entity;
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf camera messages to camera model types.</summary>
public static class AppCameraToModel
{
    /// <summary>Maps an <see cref="AppCameraInfo"/> to a <see cref="CameraInfo"/>.</summary>
    /// <param name="appCameraInfo">The protobuf camera info response.</param>
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

    /// <summary>Maps an <see cref="AppCameraRays"/> broadcast to a <see cref="CameraFrame"/>.</summary>
    /// <param name="appCameraRays">The protobuf camera rays broadcast.</param>
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

    /// <summary>Maps an <see cref="AppCameraRays"/> broadcast to a <see cref="CameraRaysEventArg"/>.</summary>
    /// <param name="appCameraRays">The protobuf camera rays broadcast.</param>
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

    /// <summary>Maps a single protobuf camera entity to a <see cref="CameraEntity"/>.</summary>
    /// <param name="entity">The protobuf camera entity.</param>
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

    /// <summary>Maps a sequence of protobuf camera entities to <see cref="CameraEntity"/> instances.</summary>
    /// <param name="entities">The protobuf camera entities to map.</param>
    public static IEnumerable<CameraEntity> ToCameraEntities(this IEnumerable<ProtoCameraEntity> entities)
    {
        return entities.Select(ToCameraEntity);
    }

    /// <summary>Maps a protobuf <see cref="ProtoVector3"/> to a <see cref="Vector3"/>.</summary>
    /// <param name="vector">The protobuf vector, or <see langword="null"/> to return a zero vector.</param>
    public static Vector3 ToVector3(this ProtoVector3? vector)
    {
        if (vector is null) return new Vector3();
        return new Vector3 { X = vector.X, Y = vector.Y, Z = vector.Z };
    }
}
