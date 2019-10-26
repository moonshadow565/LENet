using System;

namespace LENet
{
    [Flags]
    public enum PacketFlags
    {
        RELIABLE = (1 << 0),
        UNSEQUENCED = (1 << 1),
    }
}
