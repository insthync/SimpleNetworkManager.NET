namespace Insthync.SimpleNetworkManager.NET.Network
{
    public delegate void MessageReceivedHandler(BaseClientConnection clientConnection, byte[] buffer, int length);
    public delegate void DisconnectedHandler(BaseClientConnection clientConnection);
}
