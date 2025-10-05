using System;

namespace Insthync.SimpleNetworkManager.NET.Network
{
    public class InvalidMessageSizeException : Exception
    {
        public int Size;
        public int MinSize;
        public int MaxSize;
    }
}
