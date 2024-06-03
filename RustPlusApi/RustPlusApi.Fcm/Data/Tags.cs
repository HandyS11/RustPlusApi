namespace RustPlusApi.Fcm.Data
{
    public static class Tags
    {
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
