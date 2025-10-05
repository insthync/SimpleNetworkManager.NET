namespace Insthync.SimpleNetworkManager.NET.Network
{
    public delegate void MessageReceivedHandler(BaseClientConnection clientConnection, byte[] message);
    public delegate void DisconnectedHandler(BaseClientConnection clientConnection);
}
