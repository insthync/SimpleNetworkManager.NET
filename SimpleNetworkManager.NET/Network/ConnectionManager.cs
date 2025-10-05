using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Insthync.SimpleNetworkManager.NET.Network
{
    public class ConnectionManager : IDisposable
    {
        private readonly ConcurrentDictionary<uint, BaseClientConnection> _connections;
        private readonly ILogger<ConnectionManager> _logger;
        private bool _disposed;

        /// <summary>
        /// Current number of active connections
        /// </summary>
        public int ConnectionCount => _connections.Count;

        /// <summary>
        /// Initializes a new ConnectionManager instance
        /// </summary>
        /// <param name="logger">Logger instance</param>
        public ConnectionManager(ILogger<ConnectionManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connections = new ConcurrentDictionary<uint, BaseClientConnection>();
        }

        /// <summary>
        /// Adds a new connection to the manager
        /// </summary>
        /// <param name="connection">Connection to add</param>
        public void AddConnection(BaseClientConnection connection)
        {
            if (_disposed)
            {
                _logger.LogWarning("Attempted to add connection to disposed ConnectionManager");
                return;
            }

            if (connection == null)
            {
                _logger.LogError("Attempted to add null connection");
                throw new ArgumentNullException(nameof(connection));
            }

            if (_connections.TryAdd(connection.ConnectionId, connection))
            {
                _logger.LogInformation("Added connection {ConnectionId}. Total connections: {ConnectionCount}",
                    connection.ConnectionId, ConnectionCount);
            }
            else
            {
                _logger.LogWarning("Failed to add connection {ConnectionId} - already exists",
                    connection.ConnectionId);
            }
        }

        /// <summary>
        /// Removes a connection by connection ID
        /// </summary>
        /// <param name="connectionId">Connection ID of connection to remove</param>
        public void RemoveConnection(uint connectionId)
        {
            if (_disposed)
            {
                _logger.LogWarning("Attempted to remove connection from disposed ConnectionManager");
                return;
            }

            if (_connections.TryRemove(connectionId, out var connection))
            {
                _logger.LogInformation("Removed connection {ConnectionId}. Total connections: {ConnectionCount}",
                    connectionId, ConnectionCount);
            }
            else
            {
                _logger.LogDebug("Attempted to remove non-existent connection {ConnectionId}", connectionId);
            }
        }

        /// <summary>
        /// Retrieves a connection by connection ID
        /// </summary>
        /// <param name="connectionId">Connection ID to lookup</param>
        /// <returns>Connection instance or null if not found</returns>
        public BaseClientConnection? GetConnection(uint connectionId)
        {
            if (_disposed)
            {
                _logger.LogWarning("Attempted to get connection from disposed ConnectionManager");
                return null;
            }

            _connections.TryGetValue(connectionId, out var connection);
            return connection;
        }

        /// <summary>
        /// Gets all active connections
        /// </summary>
        /// <returns>Enumerable of all active connections</returns>
        public IEnumerable<BaseClientConnection> GetAllConnections()
        {
            if (_disposed)
            {
                _logger.LogWarning("Attempted to get all connections from disposed ConnectionManager");
                return Enumerable.Empty<BaseClientConnection>();
            }

            return _connections.Values.ToList(); // Return a snapshot to avoid enumeration issues
        }

        /// <summary>
        /// Disconnects all active client connections gracefully
        /// </summary>
        /// <returns>Task representing the async operation</returns>
        public async UniTask DisconnectAllClientsAsync()
        {
            var connections = GetAllConnections().ToList();

            if (connections.Count == 0)
            {
                _logger.LogDebug("No active connections to disconnect");
                return;
            }

            _logger.LogInformation("Disconnecting {ConnectionCount} active connections", connections.Count);

            var disconnectionTasks = connections.Select(async connection =>
            {
                try
                {
                    await connection.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disconnecting client {ConnectionId}", connection.ConnectionId);
                }
            });

            // Wait for all disconnections to complete
            await UniTask.WhenAll(disconnectionTasks);

            _logger.LogInformation("Client disconnection process completed");
        }

        /// <summary>
        /// Disposes the connection manager and cleans up all connections
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _logger.LogInformation("Disposing ConnectionManager with {ConnectionCount} active connections",
                ConnectionCount);

            // Disconnect all active connections with session cleanup
            var connections = _connections.Values.ToList();
            foreach (var connection in connections)
            {
                try
                {
                    if (connection.IsConnected)
                    {
                        connection.DisconnectAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disconnecting connection {ConnectionId} during disposal",
                        connection.ConnectionId);
                }
            }

            _connections.Clear();
            _logger.LogInformation("ConnectionManager disposed");
        }
    }
}
