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
    }
}
