using System;

namespace LENet
{
    public class Acknowledgement : LList<Acknowledgement>.Element
    {
        public uint SentTime { get; set; }
        public Protocol Command { get; set; }
    }
}
