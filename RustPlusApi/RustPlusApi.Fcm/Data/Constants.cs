namespace RustPlusApi.Fcm.Data
{
    public static class Constants
    {
        public enum ProcessingState
        {
            McsVersionTagAndSize = 0,
            McsTagAndSize = 1,
            McsSize = 2,
            McsProtoBytes = 3
        }

        public const int ReadTimeoutSecs = 60 * 60;
        public const int MinResetIntervalSecs = 5 * 60;
        public const int MaxSilentIntervalSecs = 60 * 60;

        public const int KVersionPacketLen = 1;
        public const int KTagPacketLen = 1;
        public const int KSizePacketLenMin = 1;
        public const int KSizePacketLenMax = 5;

        public const int KMcsVersion = 41;

        public enum McsProtoTag
        {
            KHeartbeatPingTag = 0,
            KHeartbeatAckTag = 1,
            KLoginRequestTag = 2,
            KLoginResponseTag = 3,
            KCloseTag = 4,
            KMessageStanzaTag = 5,
            KPresenceStanzaTag = 6,
            KIqStanzaTag = 7,
            KDataMessageStanzaTag = 8,
            KBatchPresenceStanzaTag = 9,
            KStreamErrorStanzaTag = 10,
            KHttpRequestTag = 11,
            KHttpResponseTag = 12,
            KBindAccountRequestTag = 13,
            KBindAccountResponseTag = 14,
            KTalkMetadataTag = 15,
            KNumProtoTypes = 16
        }

        public static readonly byte[] ServerKey = [
            0x04,
            0x33,
            0x94,
            0xf7,
            0xdf,
            0xa1,
            0xeb,
            0xb1,
            0xdc,
            0x03,
            0xa2,
            0x5e,
            0x15,
            0x71,
            0xdb,
            0x48,
            0xd3,
            0x2e,
            0xed,
            0xed,
            0xb2,
            0x34,
            0xdb,
            0xb7,
            0x47,
            0x3a,
            0x0c,
            0x8f,
            0xc4,
            0xcc,
            0xe1,
            0x6f,
            0x3c,
            0x8c,
            0x84,
            0xdf,
            0xab,
            0xb6,
            0x66,
            0x3e,
            0xf2,
            0x0c,
            0xd4,
            0x8b,
            0xfe,
            0xe3,
            0xf9,
            0x76,
            0x2f,
            0x14,
            0x1c,
            0x63,
            0x08,
            0x6a,
            0x6f,
            0x2d,
            0xb1,
            0x1a,
            0x95,
            0xb0,
            0xce,
            0x37,
            0xc0,
            0x9c,
            0x6e
        ];
    }
}
