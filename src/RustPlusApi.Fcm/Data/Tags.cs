namespace RustPlusApi.Fcm.Data;

/// <summary>MCS protocol constants used to identify protobuf message types over the FCM socket.</summary>
public static class Tags
{
    /// <summary>MCS protocol tag IDs as defined in the Google Mobile Connectivity Suite protocol specification.</summary>
    public enum McsProtoTag
    {
        /// <summary>Heartbeat ping from the server.</summary>
        KHeartbeatPingTag = 0,

        /// <summary>Heartbeat acknowledgement.</summary>
        KHeartbeatAckTag = 1,

        /// <summary>Client login request.</summary>
        KLoginRequestTag = 2,

        /// <summary>Server login response.</summary>
        KLoginResponseTag = 3,

        /// <summary>Connection close signal.</summary>
        KCloseTag = 4,

        /// <summary>Message stanza (unused in Rust+ flow).</summary>
        KMessageStanzaTag = 5,

        /// <summary>Presence stanza (unused in Rust+ flow).</summary>
        KPresenceStanzaTag = 6,

        /// <summary>IQ stanza (ignored; received but not processed).</summary>
        KIqStanzaTag = 7,

        /// <summary>Data message stanza — the carrier for FCM push notifications.</summary>
        KDataMessageStanzaTag = 8,

        /// <summary>Batch presence stanza (unused in Rust+ flow).</summary>
        KBatchPresenceStanzaTag = 9,

        /// <summary>Stream error stanza.</summary>
        KStreamErrorStanzaTag = 10,

        /// <summary>HTTP request (unused in Rust+ flow).</summary>
        KHttpRequestTag = 11,

        /// <summary>HTTP response (unused in Rust+ flow).</summary>
        KHttpResponseTag = 12,

        /// <summary>Bind account request (unused in Rust+ flow).</summary>
        KBindAccountRequestTag = 13,

        /// <summary>Bind account response (unused in Rust+ flow).</summary>
        KBindAccountResponseTag = 14,

        /// <summary>Talk metadata (unused in Rust+ flow).</summary>
        KTalkMetadataTag = 15,

        /// <summary>Sentinel — total count of defined proto types.</summary>
        KNumProtoTypes = 16
    }
}
