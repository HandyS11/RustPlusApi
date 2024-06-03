﻿using RustPlusApi.Fcm.Data.Events;

namespace RustPlusApi.Fcm.Data
{
    public class Notification<T>
    {
        public ulong PlayerId { get; set; }
        public int PlayerToken { get; set; }
        public Guid ServerId { get; set; }
        public T? Data { get; set; }
    }
}
