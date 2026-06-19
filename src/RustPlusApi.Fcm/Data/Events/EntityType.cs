namespace RustPlusApi.Fcm.Data.Events;

/// <summary>Type of a paired Rust+ entity, mirroring the Rust+ contract's <c>AppEntityType</c>.</summary>
public enum EntityType
{
    /// <summary>A Smart Switch.</summary>
    Switch = 1,

    /// <summary>A Smart Alarm.</summary>
    Alarm = 2,

    /// <summary>A Storage Monitor.</summary>
    StorageMonitor = 3
}
